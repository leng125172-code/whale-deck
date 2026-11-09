import { sourceArchiveLimit, SourceInputError } from './workspaceSourcePayload'

const blockBytes = 512
const maxEntries = 10_000
const maxPaxBytes = 16 * 1024 * 1024
const encoder = new TextEncoder()
const padding = new Uint8Array(blockBytes * 2)
const unsafeCharacter = /[\\:\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]/u
const protectedNames = new Set([
  '.git',
  '.hg',
  '.svn',
  '.ssh',
  '.npmrc',
  '.pypirc',
  '.netrc',
  'id_rsa',
  'id_ed25519',
])
const environmentExamples = new Set(['.env.example', '.env.sample', '.env.template'])

function validPart(part: string) {
  return (
    part.length > 0 &&
    part !== '.' &&
    part !== '..' &&
    !unsafeCharacter.test(part) &&
    encoder.encode(part).length <= 255
  )
}

function protectedPart(part: string) {
  const lower = part.toLowerCase()
  return (
    protectedNames.has(lower) ||
    /^\.(whaledeck-|images\.env)/.test(lower) ||
    (lower.startsWith('.env') && !environmentExamples.has(lower)) ||
    /\.(pem|key|p12|pfx|jks)$/.test(lower)
  )
}

function sourcePaths(files: readonly File[], directory: boolean) {
  const entries = new Map<string, File>()
  const folders = new Set<string>()
  let root: string | undefined
  for (const file of files) {
    const parts = directory ? file.webkitRelativePath.split('/') : [file.name]
    if (directory) {
      const selectedRoot = parts.shift()
      if (
        !selectedRoot ||
        !validPart(selectedRoot) ||
        (root !== undefined && root !== selectedRoot)
      )
        throw new SourceInputError('请选择单个目录，并保留其相对路径。')
      if (protectedPart(selectedRoot))
        throw new SourceInputError('所选目录包含受保护的版本库或凭据路径。')
      root = selectedRoot
    }
    const path = parts.join('/')
    if (
      parts.length === 0 ||
      parts.length > 32 ||
      parts.some((part) => !validPart(part)) ||
      encoder.encode(path).length > 4096 ||
      (!directory && path.includes('/')) ||
      parts[parts.length - 1] !== file.name
    )
      throw new SourceInputError('所选文件含无效名称或相对路径。')
    if (parts.some(protectedPart))
      throw new SourceInputError('所选内容含受保护的路径。请移除版本库元数据或凭据文件后再选择。')
    if (entries.has(path) || folders.has(path))
      throw new SourceInputError('所选内容含同名文件或文件与目录冲突。')
    for (let depth = 1; depth < parts.length; depth++) {
      const parent = parts.slice(0, depth).join('/')
      if (entries.has(parent)) throw new SourceInputError('所选内容含文件与目录冲突。')
      folders.add(parent)
    }
    entries.set(path, file)
    if (entries.size + folders.size >= maxEntries)
      throw new SourceInputError('所选文件和目录超出导入条目上限。')
  }
  // Locale and picker order may vary. TAR bytes must be stable for resume.
  return [...entries.keys()].sort().map((path) => ({ path, file: entries.get(path)! }))
}

function tarHeader(name: Uint8Array, size: number, type: '0' | 'x') {
  const header = new Uint8Array(blockBytes)
  const octal = (value: number, width: number, offset: number) =>
    header.set(encoder.encode(value.toString(8).padStart(width - 1, '0')), offset)
  header.set(name, 0)
  octal(0o600, 8, 100)
  octal(0, 8, 108)
  octal(0, 8, 116)
  octal(size, 12, 124)
  octal(0, 12, 136)
  header.fill(32, 148, 156)
  header[156] = type.charCodeAt(0)
  header.set(encoder.encode('ustar\0' + '00'), 257)
  octal(
    header.reduce((sum, byte) => sum + byte, 0),
    7,
    148,
  )
  return header
}

function paxPath(path: string) {
  const field = ` path=${path}\n`
  const bytes = encoder.encode(field).length
  let length = bytes + 1
  while (length !== bytes + String(length).length) length = bytes + String(length).length
  return encoder.encode(String(length) + field)
}

function padded(size: number) {
  return (blockBytes - (size % blockBytes)) % blockBytes
}

export function packWorkspaceSources(files: readonly File[], directory: boolean): Blob {
  if (!files.length) throw new SourceInputError('请选择至少一个文件。')
  if (files.length >= maxEntries) throw new SourceInputError('所选文件超出导入条目上限。')
  const parts: BlobPart[] = []
  let size = padding.length
  let metadata = 0
  for (const { path, file } of sourcePaths(files, directory)) {
    const name = encoder.encode(path)
    const pax = name.length > 100 ? paxPath(path) : null
    size += blockBytes + file.size + padded(file.size)
    if (pax) {
      metadata += pax.length
      size += blockBytes + pax.length + padded(pax.length)
    }
    if (size > sourceArchiveLimit || metadata > maxPaxBytes)
      throw new SourceInputError('封装后的归档超过导入大小或路径元数据上限。')
    if (pax)
      parts.push(
        tarHeader(encoder.encode('PaxHeaders/source'), pax.length, 'x'),
        pax,
        padding.slice(0, padded(pax.length)),
      )
    parts.push(
      tarHeader(pax ? encoder.encode('source') : name, file.size, '0'),
      file,
      padding.slice(0, padded(file.size)),
    )
  }
  parts.push(padding)
  return new Blob(parts, { type: 'application/x-tar' })
}
