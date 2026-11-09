import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '../apiClient'
import { sameSourceOrigin, validGitRef, workspaceSources } from '../developmentWorkspaceSources'

vi.mock('../apiClient', () => ({ apiRequest: vi.fn<typeof apiRequest>() }))

const request = vi.mocked(apiRequest)
const workspace = 'f7009e8d-510c-4a19-a89e-82c776421e4a'
const id = 'e9f4667f-9a35-4b60-b50e-7fd1083f91d9'
const signal = new AbortController().signal
const input = { repositoryId: 'qa-private', ref: 'refs/tags/v1' }
const archive = { format: 'tar', archiveBytes: 10240, archiveSha256: 'a'.repeat(64) }
const snapshot = {
  archive,
  commit: 'b'.repeat(40),
  resolvedObject: 'c'.repeat(40),
  fileCount: 1,
  totalBytes: 5,
}
const initial = {
  workspaceId: workspace,
  importId: id,
  state: 'Acquiring',
  archive: { format: 'tar', archiveBytes: 0, archiveSha256: '' },
  receivedBytes: 0,
  plan: null,
  code: null,
  expiresAtUtc: '2026-11-10T01:00:00Z',
  git: { ...input, snapshot: null },
}
const pinned = { ...initial, archive, git: { ...input, snapshot } }

beforeEach(() => {
  request.mockReset()
})

describe('workspace Git import receipts', () => {
  it('accepts an unbound start and verifies a newly bound cancellation receipt', async () => {
    request.mockResolvedValueOnce(initial)
    const opened = await workspaceSources.beginGit(workspace, id, input, signal)
    expect(JSON.parse(String(request.mock.calls[0]?.[1]?.body))).toEqual(input)
    request.mockResolvedValueOnce({
      ...pinned,
      state: 'Canceled',
      finishedAtUtc: initial.expiresAtUtc,
    })
    expect((await workspaceSources.cancel(workspace, opened, signal)).git?.snapshot?.commit).toBe(
      snapshot.commit,
    )
  })

  it('rejects a mismatched archive and refuses changes to a previously pinned commit', async () => {
    request.mockResolvedValueOnce({
      ...pinned,
      archive: { ...archive, archiveSha256: 'd'.repeat(64) },
    })
    await expect(workspaceSources.current(workspace, signal)).rejects.toThrow('导入回执未通过核验')
    request.mockResolvedValueOnce(pinned).mockResolvedValueOnce({
      ...pinned,
      git: { ...input, snapshot: { ...snapshot, commit: 'e'.repeat(40) } },
    })
    const first = await workspaceSources.current(workspace, signal)
    const second = await workspaceSources.current(workspace, signal)
    expect(sameSourceOrigin(first!, second!)).toBe(false)
  })

  it('rejects a foreign workspace entry or a repeating cursor in history', async () => {
    const terminal = { ...pinned, state: 'Canceled', finishedAtUtc: initial.expiresAtUtc }
    request
      .mockResolvedValueOnce({ items: [{ ...terminal, workspaceId: id }], nextBefore: null })
      .mockResolvedValueOnce({ items: [terminal], nextBefore: null })
    await expect(workspaceSources.history(workspace, null, signal)).rejects.toThrow(
      '导入回执未通过核验',
    )
    await expect(workspaceSources.history(workspace, id, signal)).rejects.toThrow(
      '导入回执未通过核验',
    )
  })

  it('accepts full refs and object IDs and rejects URLs, short refs and multiline values', () => {
    expect(['HEAD', input.ref, snapshot.commit, 'f'.repeat(64)].every(validGitRef)).toBe(true)
    expect(
      ['main', 'https://example.invalid', 'a'.repeat(7), input.ref + '\n', '0'.repeat(40)].some(
        validGitRef,
      ),
    ).toBe(false)
  })
})
