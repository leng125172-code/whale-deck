import { describe, expect, it } from 'vitest'
import {
  contentSearchSummary,
  validContentQuery,
  validateContentPage,
} from '@/services/fileContentSearch'
import type { FileContentPage } from '@/services/fileContentSearch'

const hit = {
  path: '资料/file.txt',
  entry: {
    name: 'file.txt',
    kind: 'File',
    sizeBytes: 10,
    modifiedAtUnixMs: 1,
    version: 'A'.repeat(64),
    mode: '0644',
    isProtected: false,
  },
  lineNumber: 2,
  excerpt: 'needle <script> 中文',
  excerptTruncated: false,
}
const page: FileContentPage = {
  entries: [hit],
  nextCursor: '',
  scannedEntries: 1,
  excludedEntries: 0,
  complete: true,
  stopReason: '',
  depthLimited: false,
  scannedFiles: 1,
  readBytes: 10,
  skippedNonTextFiles: 0,
  skippedOversizedFiles: 0,
}

describe('literal UTF8 content search', () => {
  it('allows literal paths, metacharacters, emoji and 256 UTF8 bytes', () => {
    for (const query of ['Api/V1\\[test]*', '😀', '中'.repeat(85), '  needle  '])
      expect(validContentQuery(query)).toBe(true)
    for (const query of ['', ' ', '\n', '\u0085', '\ud800', '中'.repeat(86)])
      expect(validContentQuery(query)).toBe(false)
  })
  it('accepts only a bounded file-version-scoped page and preserves escaped source text', () => {
    expect(validateContentPage(page, '资料', 50, 16777216)).toBe(page)
  })
  it.each([
    { ...page, readBytes: 67108865 },
    { ...page, readBytes: -1 },
    { ...page, scannedFiles: 10001 },
    { ...page, complete: true, nextCursor: 'opaque' },
    { ...page, nextCursor: '../bad' },
    { ...page, stopReason: 'FileReadTimeLimit', nextCursor: 'opaque', complete: false },
    { ...page, entries: [hit, hit] },
    { ...page, entries: [{ ...hit, path: 'outside/file.txt' }] },
    { ...page, entries: [{ ...hit, lineNumber: 0 }] },
    { ...page, entries: [{ ...hit, excerpt: 'x'.repeat(1281) }] },
    { ...page, entries: [{ ...hit, excerpt: '\n' }] },
    { ...page, entries: [{ ...hit, entry: { ...hit.entry, isProtected: true } }] },
  ])('rejects invalid or cross-scope result metadata %j', (value) => {
    expect(() => validateContentPage(value, '资料', 50, 16777216)).toThrow('正文搜索结果无效')
  })
  it('never presents skipped or budget-limited files as a full-directory snapshot', () => {
    expect(contentSearchSummary(page)).toContain('跳过项未搜索')
    expect(
      contentSearchSummary({
        ...page,
        complete: false,
        nextCursor: 'opaque',
        stopReason: 'ContentByteBudget',
      }),
    ).toContain('结果不完整')
    expect(
      contentSearchSummary({ ...page, complete: false, stopReason: 'FileReadTimeLimit' }),
    ).toContain('超时')
  })
})
