import type { FileSearchPage } from './fileBrowser'
import type { FileEntry } from './fileManagement'

export interface FileContentHit {
  path: string
  entry: FileEntry
  lineNumber: number
  excerpt: string
  excerptTruncated: boolean
}
export interface FileContentPage extends FileSearchPage {
  entries: FileContentHit[]
  scannedFiles: number
  readBytes: number
  skippedNonTextFiles: number
  skippedOversizedFiles: number
}
export const contentFileLimit = 16 * 1024 * 1024
function unsafeText(value: string) {
  return [...value].some((character) => {
    const code = character.codePointAt(0)!
    return code < 32 || (code >= 127 && code < 160) || (code >= 0xd800 && code <= 0xdfff)
  })
}
export function validContentQuery(value: string) {
  return (
    value.trim().length > 0 && new TextEncoder().encode(value).length <= 256 && !unsafeText(value)
  )
}
function integer(value: unknown, max: number, min = 0): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= min && value <= max
}
function relative(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length > 0 &&
    new TextEncoder().encode(value).length <= 4096 &&
    !value.includes('\\') &&
    !unsafeText(value) &&
    value
      .split('/')
      .every(
        (part) =>
          part !== '' &&
          part !== '.' &&
          part !== '..' &&
          new TextEncoder().encode(part).length <= 255,
      )
  )
}
export function validateContentPage(
  value: unknown,
  path: string,
  take: number,
  maxFileBytes: number,
): FileContentPage {
  if (!value || typeof value !== 'object') throw new Error('正文搜索结果无效')
  const row = value as Record<string, unknown>
  if (
    !Array.isArray(row.entries) ||
    row.entries.length > take ||
    typeof row.nextCursor !== 'string' ||
    row.nextCursor.length > 32768 ||
    !/^[A-Za-z0-9_.-]*$/.test(row.nextCursor) ||
    ![
      'scannedEntries',
      'excludedEntries',
      'scannedFiles',
      'skippedNonTextFiles',
      'skippedOversizedFiles',
    ].every((key) => integer(row[key], 10000)) ||
    !integer(row.readBytes, 64 * 1024 * 1024) ||
    typeof row.complete !== 'boolean' ||
    typeof row.depthLimited !== 'boolean' ||
    typeof row.stopReason !== 'string' ||
    ![
      '',
      'ScanBudget',
      'DirectoryScanLimit',
      'ResultBytes',
      'ResultPage',
      'DepthLimit',
      'ContentByteBudget',
      'FileReadTimeLimit',
    ].includes(row.stopReason) ||
    (row.complete && (row.nextCursor !== '' || row.depthLimited)) ||
    (row.stopReason === 'FileReadTimeLimit' && (row.complete || row.nextCursor !== ''))
  )
    throw new Error('正文搜索结果无效')
  const paths = new Set<string>()
  for (const hit of row.entries) {
    if (
      !hit ||
      typeof hit !== 'object' ||
      !relative(hit.path) ||
      (path && !hit.path.startsWith(path + '/')) ||
      paths.has(hit.path) ||
      !hit.entry ||
      hit.entry.kind !== 'File' ||
      hit.entry.isProtected !== false ||
      !relative(hit.entry.name) ||
      hit.entry.name.includes('/') ||
      hit.path.split('/').at(-1) !== hit.entry.name ||
      !integer(hit.entry.sizeBytes, maxFileBytes) ||
      !/^[A-F0-9]{64}$/.test(hit.entry.version) ||
      !integer(hit.lineNumber, contentFileLimit + 1, 1) ||
      typeof hit.excerpt !== 'string' ||
      !hit.excerpt ||
      new TextEncoder().encode(hit.excerpt).length > 1280 ||
      unsafeText(hit.excerpt) ||
      typeof hit.excerptTruncated !== 'boolean'
    )
      throw new Error('正文搜索结果无效')
    paths.add(hit.path)
  }
  return value as FileContentPage
}
export function contentSearchSummary(result: FileContentPage) {
  if (result.complete) return '可搜索文件扫描结束；跳过项未搜索，不代表整个目录的一致性快照'
  if (result.stopReason === 'FileReadTimeLimit')
    return '单文件读取或匹配超时，结果不完整；请降低单文件上限或缩小目录后重新搜索'
  if (result.stopReason === 'DirectoryScanLimit')
    return '目录过大或扫描超时，结果不完整；请缩小搜索目录'
  if (result.depthLimited && !result.nextCursor)
    return '已达到32层子目录限制，结果不完整；请进入更深目录搜索'
  if (result.stopReason === 'ContentByteBudget')
    return '已达到本页读取预算，结果不完整；可继续扫描下一批文件'
  return '尚未搜完，可继续扫描；当前结果不是全部匹配'
}
