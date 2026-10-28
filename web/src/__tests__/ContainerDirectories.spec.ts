import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '@/services/apiClient'
import {
  assertDirectoryIntent,
  directoryRegistration,
  directoryState,
  directoryTerminal,
  planDirectory,
  submitDirectory,
} from '@/services/containerDirectories'
import type { DirectoryIntent, DirectoryPlan } from '@/services/containerDirectories'
import {
  clearDirectoryHints,
  directoryHint,
  reconcileDirectoryHints,
  saveDirectoryHint,
} from '@/services/containerDirectoryHints'

vi.mock('@/services/apiClient', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/apiClient')>()),
  apiRequest: vi.fn(),
}))
const target = {
  containerId: 'a'.repeat(64),
  instanceId: '777:2026-10-28T01:02:03.123456789Z',
  name: 'ordinary',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000:1000',
}
const command = {
  location: { containerId: target.containerId, instanceId: target.instanceId, path: 'work/新目录' },
  mode: '0750',
}
const id = 'ab12ab12-ab12-4b12-ab12-ab12ab12ab12'
const plan: DirectoryPlan = {
  planHash: 'opaque-business-plan',
  expiresAtUtc: '2026-10-28T01:07:03Z',
  command,
  warning: '不会覆盖已有目录',
}
const intent: DirectoryIntent = {
  id,
  jobId: id,
  command,
  state: 'Pending',
  expiresAtUtc: '2026-10-28T01:17:03Z',
  resultCode: null,
  version: 0,
  publishedVersion: null,
}
beforeEach(() => {
  vi.mocked(apiRequest).mockReset()
  clearDirectoryHints()
})

describe('container directory task identity and truthful outcomes', () => {
  it('keeps Unknown protected and distinguishes historical acknowledgement from creation', () => {
    expect(directoryTerminal('Unknown')).toBe(false)
    expect(directoryTerminal('Acknowledged')).toBe(true)
    expect(directoryState('Acknowledged')).toContain('不代表创建成功')
    expect(directoryState('Superseded')).toContain('当前目标已变化')
  })
  it('keeps full container/instance/path and mode in plan and one frozen confirmation', async () => {
    vi.mocked(apiRequest).mockResolvedValueOnce(plan).mockResolvedValueOnce(intent)
    const p = await planDirectory(
      target,
      command.location.path,
      command.mode,
      new AbortController().signal,
    )
    const registration = directoryRegistration(p)
    await submitDirectory(target, registration, new AbortController().signal)
    expect(vi.mocked(apiRequest).mock.calls[0]?.[0]).toBe(
      `containers/${target.containerId}/files/directories/plan`,
    )
    const post = vi.mocked(apiRequest).mock.calls[1]!
    expect(JSON.parse(post[1]!.body as string)).toEqual(registration)
    expect(post[1]!.headers).toEqual({ 'Idempotency-Key': registration.idempotencyKey })
    expect(registration.confirmed).toBe(true)
  })
  it('accepts harmless JSON field ordering without loosening target equality', async () => {
    vi.mocked(apiRequest).mockResolvedValueOnce({
      ...plan,
      command: {
        mode: '0750',
        location: {
          path: command.location.path,
          instanceId: target.instanceId,
          containerId: target.containerId,
        },
      },
    })
    await expect(
      planDirectory(target, command.location.path, command.mode, new AbortController().signal),
    ).resolves.toMatchObject(plan)
  })
  it.each(['4755', '1755', '755', '0999'])(
    'rejects special or malformed permission %s before fetching',
    async (mode) => {
      await expect(
        planDirectory(target, command.location.path, mode, new AbortController().signal),
      ).rejects.toThrow(/权限/)
      expect(apiRequest).not.toHaveBeenCalled()
    },
  )
  it.each(['../escape', '/etc/new', 'a//b', 'a\\b', ''])(
    'rejects invalid target %s before fetching',
    async (path) => {
      await expect(
        planDirectory(target, path, '0750', new AbortController().signal),
      ).rejects.toThrow()
      expect(apiRequest).not.toHaveBeenCalled()
    },
  )
  it.each([
    { id: '00000000-0000-0000-0000-000000000000' },
    { jobId: 'another' },
    { version: -1 },
    { version: Number.MAX_SAFE_INTEGER + 1 },
    { state: 'Receiving' },
    { expiresAtUtc: 'invalid' },
    { state: 'Succeeded', publishedVersion: null },
    { state: 'Unknown', publishedVersion: 'F'.repeat(64) },
    { resultCode: '/private/path' },
  ])('does not acknowledge unverifiable metadata %j', (change) => {
    expect(() => assertDirectoryIntent({ ...intent, ...change }, command, id)).toThrow(/无法核实/)
  })
  it('does not accept a different instance, target, mode or intent', () => {
    for (const change of [
      { location: { ...command.location, path: 'work/elsewhere' } },
      { location: { ...command.location, instanceId: '888:2026-10-28T01:02:03Z' } },
      { mode: '0777' },
    ]) {
      expect(() =>
        assertDirectoryIntent({ ...intent, command: { ...command, ...change } }, command, id),
      ).toThrow()
    }
    expect(() =>
      assertDirectoryIntent(intent, command, 'bb12ab12-ab12-4b12-ab12-ab12ab12ab12'),
    ).toThrow()
  })
})
describe('directory refresh hints are account-bound and never execute', () => {
  it('restores only the original registration metadata, without making a request', () => {
    const registration = directoryRegistration(plan)
    saveDirectoryHint({ subject: 'first-admin', command, registration, id: null })
    expect(directoryHint('first-admin', target.containerId)?.registration).toEqual(registration)
    expect(apiRequest).not.toHaveBeenCalled()
  })
  it('clears hints on account change, demotion, or loss of identity', () => {
    for (const [subject, admin] of [
      ['other-admin', true],
      ['first-admin', false],
      [undefined, false],
    ] as const) {
      saveDirectoryHint({
        subject: 'first-admin',
        command,
        registration: directoryRegistration(plan),
        id,
      })
      reconcileDirectoryHints(subject, admin)
      expect(directoryHint('first-admin', target.containerId)).toBeNull()
    }
  })
  it('does not accept corrupted or over-budget storage', () => {
    sessionStorage.setItem('whaledeck-container-directory-hints-v1', '{broken')
    expect(directoryHint('first-admin', target.containerId)).toBeNull()
    expect(sessionStorage.getItem('whaledeck-container-directory-hints-v1')).toBeNull()
    expect(() =>
      saveDirectoryHint({
        subject: 'first-admin',
        command,
        registration: { planHash: 'opaque', confirmed: true, idempotencyKey: '-'.repeat(36) },
        id,
      }),
    ).toThrow()
  })
})
