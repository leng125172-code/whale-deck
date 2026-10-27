import { afterEach, describe, expect, it, vi } from 'vitest'
import { readContainerText, prepareContainerText } from '@/services/containerText'
import { maximumTextBytes, normalizedText } from '@/services/fileText'
import type { FileEntry } from '@/services/fileManagement'

const target = {
  containerId: 'a'.repeat(64),
  instanceId: '777:2026-10-27T01:02:03.123456789Z',
  name: 'ordinary',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000',
}
const entry: FileEntry = {
  name: 'code.txt',
  kind: 'File',
  sizeBytes: 0,
  modifiedAtUnixMs: 0,
  version: 'A'.repeat(64),
  mode: '0640',
  isProtected: false,
}
afterEach(() => vi.unstubAllGlobals())
function respond(bytes: Uint8Array<ArrayBuffer>) {
  const fetching = vi
    .fn<typeof fetch>()
    .mockResolvedValue(new Response(bytes, { headers: { 'Content-Length': String(bytes.length) } }))
  vi.stubGlobal('fetch', fetching)
  return fetching
}
describe('container UTF-8 documents without host root aliases', () => {
  it.each(['', '中文 O0Il\r\nline\r\n', 'a\r\nb\rc\n', '\uFEFF\uFEFF中\r\n'])(
    'preserves original BOM, bytes and nanosecond instance: %j',
    async (text) => {
      const bytes = new TextEncoder().encode(text)
      respond(bytes)
      const snapshot = await readContainerText(
        target,
        'work/code.txt',
        { ...entry, sizeBytes: bytes.length },
        new AbortController().signal,
      )
      expect((snapshot.utf8Bom ? '\uFEFF' : '') + snapshot.text).toBe(text)
      expect(snapshot.location).toEqual({
        containerId: target.containerId,
        instanceId: target.instanceId,
        path: 'work/code.txt',
      })
      expect('rootId' in snapshot).toBe(false)
      const prepared = prepareContainerText(
        snapshot,
        normalizedText(snapshot.text) + 'new\n',
        'CRLF',
        snapshot.utf8Bom,
      )
      expect(prepared.expectedVersion).toBe(entry.version)
      expect(prepared.file.name).toBe(entry.name)
      expect(prepared.draft.snapshot).toEqual(snapshot)
      expect(prepared.draft.text).toContain('new\n')
      expect('rootId' in prepared).toBe(false)
    },
  )
  it.each([{ bytes: [0xff] }, { bytes: [0xed, 0xa0, 0x80] }, { bytes: [0x61, 0x00, 0x62] }])(
    'refuses malformed UTF-8 or NUL binary bytes %j',
    async ({ bytes }) => {
      respond(Uint8Array.from(bytes))
      await expect(
        readContainerText(
          target,
          'work/code.txt',
          { ...entry, sizeBytes: bytes.length },
          new AbortController().signal,
        ),
      ).rejects.toThrow(/UTF-8|NUL/)
    },
  )
  it.each([
    { isProtected: true },
    { kind: 'Symlink' },
    { sizeBytes: maximumTextBytes + 1 },
    { version: 'A'.repeat(64) + '\n' },
  ])('refuses unsafe metadata before fetching %j', async (change) => {
    const fetching = respond(new Uint8Array())
    await expect(
      readContainerText(
        target,
        'work/code.txt',
        { ...entry, ...change },
        new AbortController().signal,
      ),
    ).rejects.toThrow(/普通文件|UTF-8/)
    expect(fetching).not.toHaveBeenCalled()
  })
  it('freezes input metadata rather than adopting a later directory version', async () => {
    const bytes = new TextEncoder().encode('old\n')
    let release!: (value: Response) => void
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockImplementation(
        () =>
          new Promise((resolve) => {
            release = resolve
          }),
      ),
    )
    const mutable = { ...entry, sizeBytes: bytes.length }
    const pending = readContainerText(
      target,
      'work/code.txt',
      mutable,
      new AbortController().signal,
    )
    mutable.version = 'B'.repeat(64)
    mutable.sizeBytes = 999
    release(new Response(bytes, { headers: { 'Content-Length': String(bytes.length) } }))
    const result = await pending
    expect(result.version).toBe(entry.version)
    expect(result.sizeBytes).toBe(bytes.length)
  })
  it('refuses a changed draft location and preserves explicit mixed newline policy', async () => {
    const bytes = new TextEncoder().encode('a\r\nb\rc\n')
    respond(bytes)
    const snapshot = await readContainerText(
      target,
      'work/code.txt',
      { ...entry, sizeBytes: bytes.length },
      new AbortController().signal,
    )
    expect(() =>
      prepareContainerText(snapshot, normalizedText(snapshot.text) + 'edit', 'Keep', false),
    ).toThrow(/混合换行/)
    expect(() =>
      prepareContainerText({ ...snapshot, path: 'work/other.txt' }, 'changed', 'LF', false),
    ).toThrow(/目标已变化/)
  })
})
