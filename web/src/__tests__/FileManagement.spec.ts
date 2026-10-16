import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  filePath,
  fileQuery,
  fileState,
  fileTerminal,
  hashFile,
  downloadFile,
} from '@/services/fileManagement'

afterEach(() => vi.unstubAllGlobals())
describe('file transfer client boundaries', () => {
  it('keeps Unicode, literal percent and query separators in exact path components', () => {
    const path = filePath('资料', 'a%2f&?.txt')
    expect(new URLSearchParams(fileQuery('files.shared', path)).get('path')).toBe('资料/a%2f&?.txt')
    expect(filePath('', 'file')).toBe('file')
  })
  it.each(['AwaitingUpload', 'Receiving', 'Ready', 'Publishing', 'Unknown'])(
    'does not turn %s into completed',
    (state) => {
      expect(fileTerminal(state)).toBe(false)
      expect(fileState(state)).not.toBe('已发布')
    },
  )
  it.each(['Succeeded', 'Superseded', 'Canceled', 'Failed'])(
    'recognizes durable terminal %s',
    (state) => expect(fileTerminal(state)).toBe(true),
  )
  it('terminates the hash worker on cancellation without storing the file', async () => {
    const terminated = vi.fn<() => void>()
    vi.stubGlobal(
      'Worker',
      class {
        terminate = terminated
        postMessage() {}
      },
    )
    const controller = new AbortController()
    const hashing = hashFile(new File(['private-file'], 'sample'), controller.signal, () => {})
    controller.abort()
    await expect(hashing).rejects.toHaveProperty('name', 'AbortError')
    expect(terminated).toHaveBeenCalledTimes(1)
  })
  it.each(['length', 'truncated'])(
    'never produces a download blob for %s mismatch',
    async (variant) => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          new Response(new Uint8Array([1, 2]), {
            headers: { 'Content-Length': variant === 'length' ? '9' : '3' },
          }),
        ),
      )
      await expect(
        downloadFile(
          'files.shared',
          'test.bin',
          {
            name: 'test.bin',
            kind: 'File',
            sizeBytes: 3,
            modifiedAtUnixMs: 0,
            version: 'A'.repeat(64),
            mode: '0600',
            isProtected: false,
          },
          new AbortController().signal,
          () => {},
        ),
      ).rejects.toThrow(/下载/)
    },
  )
})
