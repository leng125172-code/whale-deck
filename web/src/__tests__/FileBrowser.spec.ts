import { describe, expect, it } from 'vitest'
import { parentDirectory, searchSummary, validFileSearch } from '@/services/fileBrowser'
import type { FileSearchPage } from '@/services/fileBrowser'
const page: FileSearchPage = {
  entries: [],
  nextCursor: '',
  scannedEntries: 0,
  excludedEntries: 0,
  complete: false,
  stopReason: '',
  depthLimited: false,
}
describe('registered directory browser', () => {
  it('validates literal names using UTF8 byte budgets rather than character counts', () => {
    expect(validFileSearch('a[1]*.中文')).toBe(true)
    expect(validFileSearch('中'.repeat(85))).toBe(true)
    for (const value of ['', ' ', '../file', 'a\\b', 'x\n', 'x\u0085', '中'.repeat(86)])
      expect(validFileSearch(value)).toBe(false)
  })
  it('never describes a partial scan as no matches or complete', () => {
    expect(searchSummary({ ...page, stopReason: 'ScanBudget', nextCursor: 'cursor' })).toContain(
      '尚未搜完',
    )
    expect(searchSummary({ ...page, stopReason: 'DirectoryScanLimit' })).toContain('结果不完整')
    expect(searchSummary({ ...page, depthLimited: true })).toContain('32 层')
    expect(searchSummary({ ...page, complete: true })).toContain('搜索完成')
  })
  it('opens containing directory without manufacturing an absolute host path', () => {
    expect(parentDirectory('file.txt')).toBe('')
    expect(parentDirectory('a/中文/file.txt')).toBe('a/中文')
  })
})
