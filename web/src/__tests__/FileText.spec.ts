import { describe, expect, it } from 'vitest'
import { sha256 } from '@noble/hashes/sha2.js'
import {
  maximumTextBytes,
  normalizedText,
  prepareText,
  textBytes,
  textEligible,
  textLineEnding,
  validateTextSnapshot,
} from '@/services/fileText'
import type { FileTextSnapshot } from '@/services/fileText'

function snapshot(text = '中英 O0Il\r\nline\r\n', bom = true): FileTextSnapshot {
  const content = new TextEncoder().encode((bom ? '\uFEFF' : '') + text)
  return {
    rootId: 'files.shared',
    path: 'code/sample.txt',
    version: 'A'.repeat(64),
    text,
    utf8Bom: bom,
    sizeBytes: content.length,
    sha256: Array.from(sha256(content), (byte) => byte.toString(16).padStart(2, '0'))
      .join('')
      .toUpperCase(),
    lineEnding: textLineEnding(text),
  }
}
describe('bounded version-frozen text editing', () => {
  it('verifies the full raw BOM/UTF-8 size and hash before accepting text', () => {
    const value = snapshot()
    expect(validateTextSnapshot(value, value.rootId, value.path, value.version)).toBe(value)
    for (const changed of [
      { ...value, path: 'other' },
      { ...value, version: 'B'.repeat(64) },
      { ...value, sha256: 'F'.repeat(64) },
      { ...value, sizeBytes: value.sizeBytes + 1 },
      { ...value, lineEnding: 'LF' as const },
    ])
      expect(() => validateTextSnapshot(changed, value.rootId, value.path, value.version)).toThrow(
        /文本预览(元数据无效|校验不一致)/,
      )
  })
  it('keeps the original filename, source version and exact BOM/CRLF bytes', () => {
    const value = snapshot(),
      prepared = prepareText(value, normalizedText(value.text) + 'new\n', 'CRLF', true)
    expect(prepared.expectedVersion).toBe(value.version)
    expect(prepared.path).toBe(value.path)
    expect(prepared.file.name).toBe('sample.txt')
    expect(textBytes(value, normalizedText(value.text), 'CRLF', true)).toEqual(
      new TextEncoder().encode('\uFEFF' + value.text),
    )
    expect(prepared.file.size).toBe(
      new TextEncoder().encode('\uFEFF' + value.text + 'new\r\n').length,
    )
  })
  it.each(['LF', 'CRLF', 'CR'] as const)(
    'supports explicit %s without inventing a final newline',
    (ending) => {
      const value = snapshot('a\nb', false)
      const separator = ending === 'LF' ? '\n' : ending === 'CRLF' ? '\r\n' : '\r'
      expect(textBytes(value, 'a\nb', ending, false)).toEqual(
        new TextEncoder().encode('a' + separator + 'b'),
      )
    },
  )
  it('preserves mixed bytes until the user explicitly chooses a normalization', () => {
    const value = snapshot('a\r\nb\rc\n', false)
    expect(textBytes(value, normalizedText(value.text), 'Keep', false)).toEqual(
      new TextEncoder().encode(value.text),
    )
    expect(() => textBytes(value, normalizedText(value.text) + 'edit', 'Keep', false)).toThrow(
      '混合换行',
    )
    expect(textBytes(value, normalizedText(value.text) + 'edit', 'LF', false)).toEqual(
      new TextEncoder().encode('a\nb\nc\nedit'),
    )
  })
  it.each(['\u0000', '\uD800', '\uDC00'])(
    'rejects NUL and malformed UTF-16 before File/TextEncoder can silently replace it: %s',
    (text) => {
      expect(() => prepareText(snapshot(), text, 'LF', false)).toThrow(/NUL|无效 Unicode/)
    },
  )
  it('uses byte limits, not character counts or lossy truncation', () => {
    const value = snapshot('', false)
    expect(textBytes(value, 'a'.repeat(maximumTextBytes), 'LF', false)).toHaveLength(
      maximumTextBytes,
    )
    expect(() => textBytes(value, '中'.repeat(350000), 'LF', false)).toThrow('1 MiB')
  })
  it('does not offer edit for protected, non-file or oversized entries', () => {
    const entry = {
      name: 'sample',
      kind: 'File',
      sizeBytes: 0,
      modifiedAtUnixMs: 0,
      version: 'A'.repeat(64),
      mode: '0644',
      isProtected: false,
    }
    expect(textEligible(entry)).toBe(true)
    expect(textEligible({ ...entry, isProtected: true })).toBe(false)
    expect(textEligible({ ...entry, kind: 'Directory' })).toBe(false)
    expect(textEligible({ ...entry, sizeBytes: maximumTextBytes + 1 })).toBe(false)
  })
})
