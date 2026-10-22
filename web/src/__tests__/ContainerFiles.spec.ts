import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  canDownloadContainerFile,
  containerFileBase,
  containerFileQuery,
  downloadContainerFile,
} from '@/services/containerFiles'
import type { FileEntry } from '@/services/fileManagement'

const target = {
  containerId: 'a'.repeat(64),
  instanceId: '777:2026-10-22T01:02:03.123456789Z',
  name: 'ordinary',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000',
}
const entry: FileEntry = {
  name: '中文.bin',
  kind: 'File',
  sizeBytes: 3,
  modifiedAtUnixMs: 0,
  version: 'A'.repeat(64),
  mode: '0640',
  isProtected: false,
}
afterEach(() => vi.unstubAllGlobals())
describe('instance-bound container files', () => {
  it('keeps nanosecond identity and literal Unicode path without a host root alias', () => {
    expect(containerFileBase(target.containerId)).toBe(`containers/${target.containerId}/files`)
    const query = new URLSearchParams(
      containerFileQuery(target, '中文/a%2f&?.bin', { after: '下一页' }),
    )
    expect(query.get('instanceId')).toBe(target.instanceId)
    expect(query.get('path')).toBe('中文/a%2f&?.bin')
    expect(query.has('rootId')).toBe(false)
    expect(query.get('after')).toBe('下一页')
  })
  it.each(['ordinary', 'a'.repeat(12), 'A'.repeat(64), '../core'])(
    'refuses non-canonical id %s',
    (id) => expect(() => containerFileBase(id)).toThrow(/完整容器 ID/),
  )
  it('does not allow extra query fields to replace the bound instance or path', () => {
    const query = new URLSearchParams(
      containerFileQuery(target, 'a', { instanceId: 'other', path: 'other' }),
    )
    expect(query.get('instanceId')).toBe(target.instanceId)
    expect(query.get('path')).toBe('a')
  })
  it.each([
    { isProtected: true },
    { kind: 'Directory' },
    { sizeBytes: -1 },
    { sizeBytes: 1024 ** 3 + 1 },
    { version: 'absent' },
  ])('rejects unsafe download metadata before fetching %j', async (change) => {
    const fetching = vi.fn<typeof fetch>()
    vi.stubGlobal('fetch', fetching)
    expect(canDownloadContainerFile({ ...entry, ...change })).toBe(false)
    await expect(
      downloadContainerFile(
        target,
        '中文.bin',
        { ...entry, ...change },
        new AbortController().signal,
        () => {},
      ),
    ).rejects.toThrow(/受保护|下载上限/)
    expect(fetching).not.toHaveBeenCalled()
  })
  it.each(['length', 'short', 'long', 'stream-error'])(
    'never completes a %s transfer',
    async (mode) => {
      const body =
        mode === 'stream-error'
          ? new ReadableStream({
              start(controller) {
                controller.enqueue(new Uint8Array([1]))
                controller.error(new Error('interrupted'))
              },
            })
          : new Uint8Array(mode === 'short' ? [1, 2] : mode === 'long' ? [1, 2, 3, 4] : [1, 2, 3])
      vi.stubGlobal(
        'fetch',
        vi
          .fn<typeof fetch>()
          .mockResolvedValue(
            new Response(body, { headers: { 'Content-Length': mode === 'length' ? '9' : '3' } }),
          ),
      )
      await expect(
        downloadContainerFile(target, '中文.bin', entry, new AbortController().signal, () => {}),
      ).rejects.toThrow(/下载|interrupted/)
    },
  )
  it('only returns a complete blob and keeps credentials and cancellation', async () => {
    const fetching = vi
      .fn<typeof fetch>()
      .mockResolvedValue(
        new Response(new Uint8Array([1, 2, 3]), { headers: { 'Content-Length': '3' } }),
      )
    vi.stubGlobal('fetch', fetching)
    const controller = new AbortController()
    const blob = await downloadContainerFile(target, '中文.bin', entry, controller.signal, () => {})
    expect(blob.size).toBe(3)
    expect(fetching.mock.calls[0]![1]).toMatchObject({
      credentials: 'include',
      signal: controller.signal,
    })
    const url = new URL(String(fetching.mock.calls[0]![0]), 'http://127.0.0.1')
    expect(url.searchParams.get('expectedVersion')).toBe(entry.version)
    expect(url.searchParams.get('instanceId')).toBe(target.instanceId)
  })
})
