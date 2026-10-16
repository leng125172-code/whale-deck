import type { FileEntry } from './fileManagement'
export interface FileSearchHit {
  path: string
  entry: FileEntry
}
export interface FileSearchPage {
  entries: FileSearchHit[]
  nextCursor: string
  scannedEntries: number
  excludedEntries: number
  complete: boolean
  stopReason: string
  depthLimited: boolean
}
export function validFileSearch(value: string) {
  return (
    value.trim().length > 0 &&
    new TextEncoder().encode(value).length <= 256 &&
    !value.includes('/') &&
    !value.includes('\\') &&
    ![...value].some(
      (character) =>
        character.charCodeAt(0) < 32 ||
        (character.charCodeAt(0) >= 127 && character.charCodeAt(0) < 160),
    )
  )
}
export function parentDirectory(path: string) {
  return path.slice(0, Math.max(0, path.lastIndexOf('/')))
}
export function fileTreeKey(path: string, continuation = false) {
  return `${continuation ? 'more' : 'directory'}:${path}`
}
export function searchSummary(result: FileSearchPage) {
  if (result.complete) return '搜索完成（不含受保护目录、链接和嵌套挂载）'
  if (result.stopReason === 'DirectoryScanLimit')
    return '目录过大或扫描超时，结果不完整；请缩小搜索目录'
  if (result.depthLimited && !result.nextCursor)
    return '已达到 32 层子目录限制，结果不完整；请进入更深目录搜索'
  return '尚未搜完，可继续扫描；当前结果不是全部匹配'
}
