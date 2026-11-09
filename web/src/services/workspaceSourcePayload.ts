import { sha256 } from '@noble/hashes/sha2.js'
import { bytesToHex } from '@noble/hashes/utils.js'

export const sourceChunkBytes = 1024 * 1024
export const sourceArchiveLimit = 128 * sourceChunkBytes
const formats = ['zip', 'tar', 'tar.gz'] as const

export interface SourceArchive {
  format: (typeof formats)[number]
  archiveBytes: number
  archiveSha256: string
}

export class SourceInputError extends Error {}

export function sourceFormat(file: File): SourceArchive['format'] {
  const name = file.name.toLowerCase()
  if (file.size === 0 || file.size > sourceArchiveLimit)
    throw new SourceInputError('请选择大于 0 且不超过 128 MiB 的归档。')
  if (name.endsWith('.tar.gz') || name.endsWith('.tgz')) return 'tar.gz'
  if (name.endsWith('.tar')) return 'tar'
  if (name.endsWith('.zip')) return 'zip'
  throw new SourceInputError('请选择 ZIP、tar 或 tar.gz 归档。')
}

export async function identifySource(
  file: Blob,
  format: SourceArchive['format'],
  signal: AbortSignal,
): Promise<SourceArchive> {
  const digest = sha256.create()
  for (let offset = 0; offset < file.size; offset += sourceChunkBytes) {
    signal.throwIfAborted()
    digest.update(new Uint8Array(await file.slice(offset, offset + sourceChunkBytes).arrayBuffer()))
  }
  signal.throwIfAborted()
  return { format, archiveBytes: file.size, archiveSha256: bytesToHex(digest.digest()) }
}

export function sameSourceArchive(left: SourceArchive, right: SourceArchive) {
  return (
    left.format === right.format &&
    left.archiveBytes === right.archiveBytes &&
    left.archiveSha256 === right.archiveSha256
  )
}

export function isSourceFormat(value: unknown): boolean {
  return formats.some((format) => format === value)
}
