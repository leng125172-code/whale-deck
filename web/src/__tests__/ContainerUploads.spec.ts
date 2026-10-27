import { describe, expect, it } from 'vitest'
import {
  assertContainerUploadIntent,
  assertContainerUploadPlan,
  containerUploadFollows,
  containerUploadState,
  containerUploadTerminal,
  newContainerUploadRegistration,
} from '@/services/containerUploads'
import type { ContainerUploadIntent } from '@/services/containerUploads'

const target = {
  containerId: 'a'.repeat(64),
  instanceId: '777:2026-10-22T01:02:03.123456789Z',
  name: 'demo-web',
  imageId: 'sha256:' + 'b'.repeat(64),
  configuredUser: '1000:1000',
}
const binding = {
  target,
  path: '资料/中文.bin',
  sizeBytes: 3,
  sha256: 'C'.repeat(64),
}
const intent = (state = 'AwaitingUpload', version = 0): ContainerUploadIntent => ({
  id: '7d4fec0a-01b7-494a-9b55-e96e265503a1',
  jobId: '7d4fec0a-01b7-494a-9b55-e96e265503a1',
  location: {
    containerId: target.containerId,
    instanceId: target.instanceId,
    path: binding.path,
  },
  sizeBytes: binding.sizeBytes,
  sha256: binding.sha256,
  state,
  expiresAtUtc: '2099-01-01T00:15:00.000Z',
  resultCode: null,
  version,
  publishedVersion: ['Succeeded', 'Superseded'].includes(state) ? 'D'.repeat(64) : null,
  previousRetained: ['Succeeded', 'Superseded'].includes(state),
})

describe('instance-bound container uploads', () => {
  it('accepts only the exact four-field plan and freezes a UUID registration key', () => {
    const plan = {
      planHash: 'signed-plan',
      expiresAtUtc: '2099-01-01T00:05:00.000Z',
      overwrites: true,
      warning: '覆盖前先验证旧版本留存。',
    }
    expect(assertContainerUploadPlan(plan)).toBe(plan)
    expect(() => assertContainerUploadPlan({ ...plan, ownerHash: 'private' } as never)).toThrow(
      /计划无法核实/,
    )
    const registration = newContainerUploadRegistration(plan.planHash)
    expect(registration).toMatchObject({ planHash: plan.planHash, confirmed: true })
    expect(Object.keys(registration).sort()).toEqual(['confirmed', 'idempotencyKey', 'planHash'])
    expect(registration.idempotencyKey).toMatch(/^[a-f0-9-]{36}$/)
    expect(
      assertContainerUploadPlan({ ...plan, planHash: 'x'.repeat(65536) }).planHash,
    ).toHaveLength(65536)
    expect(() => assertContainerUploadPlan({ ...plan, planHash: 'x'.repeat(65537) })).toThrow(
      /计划无法核实/,
    )
  })

  it('binds the public intent to exact container, instance, path, bytes and digest', () => {
    const row = intent()
    expect(assertContainerUploadIntent(row, binding)).toBe(row)
    expect(Object.keys(row)).toHaveLength(11)
    expect(Object.keys(row.location).sort()).toEqual(['containerId', 'instanceId', 'path'])
    for (const changed of [
      { ...row, location: { ...row.location, instanceId: '778:2026-10-22T01:02:04Z' } },
      { ...row, location: { ...row.location, path: '其他.bin' } },
      { ...row, sizeBytes: 4 },
      { ...row, sha256: 'E'.repeat(64) },
      {
        ...row,
        id: '00000000-0000-0000-0000-000000000000',
        jobId: '00000000-0000-0000-0000-000000000000',
      },
      { ...row, jobId: '129f14ed-3254-4868-9885-acad522acefa' },
      { ...row, ownerHash: 'private' },
    ])
      expect(() => assertContainerUploadIntent(changed as ContainerUploadIntent, binding)).toThrow(
        /无法核实/,
      )
  })

  it('requires a published version for success and rejects malformed paths and private expansion', () => {
    expect(assertContainerUploadIntent(intent('Succeeded', 4), binding).previousRetained).toBe(true)
    expect(() =>
      assertContainerUploadIntent({ ...intent('Succeeded', 4), publishedVersion: null }, binding),
    ).toThrow(/无法核实/)
    expect(() =>
      assertContainerUploadIntent({ ...intent('Superseded', 4), publishedVersion: null }, binding),
    ).toThrow(/无法核实/)
    expect(() =>
      assertContainerUploadIntent(
        { ...intent(), location: { ...intent().location, path: '../secret' } },
        { ...binding, path: '../secret' },
      ),
    ).toThrow(/无法核实/)
  })

  it('rejects trailing newlines in otherwise plausible container and instance identifiers', () => {
    const row = intent()
    expect(() =>
      assertContainerUploadIntent({
        ...row,
        location: { ...row.location, containerId: `${row.location.containerId}\n` },
      }),
    ).toThrow(/无法核实/)
    expect(() =>
      assertContainerUploadIntent({
        ...row,
        location: { ...row.location, instanceId: `${row.location.instanceId}\n` },
      }),
    ).toThrow(/无法核实/)
  })

  it('matches Linux instance and UTF-8 path boundaries without normalizing nanoseconds', () => {
    const accepted = intent()
    accepted.location.instanceId = '2147483647:2026-10-22T01:02:03.123456789Z'
    accepted.location.path = `${'界'.repeat(84)}abc`
    expect(assertContainerUploadIntent(accepted)).toBe(accepted)
    expect(accepted.location.instanceId).toBe('2147483647:2026-10-22T01:02:03.123456789Z')

    for (const invalid of [
      { instanceId: '1:2026-10-22T01:02:03.123456789Z', path: binding.path },
      { instanceId: '2147483648:2026-10-22T01:02:03.123456789Z', path: binding.path },
      { instanceId: target.instanceId, path: '界'.repeat(86) },
      { instanceId: target.instanceId, path: `目录/坏\u0085名字` },
      { instanceId: target.instanceId, path: `目录/坏\ud800名字` },
    ]) {
      const row = intent()
      row.location = { ...row.location, ...invalid }
      expect(() => assertContainerUploadIntent(row)).toThrow(/无法核实/)
    }
  })

  it('accepts only container-upload result codes up to 64 characters', () => {
    expect(
      assertContainerUploadIntent({
        ...intent('Canceled', 2),
        resultCode: 'CONTAINER_UPLOAD_CANCELED',
      }).resultCode,
    ).toBe('CONTAINER_UPLOAD_CANCELED')
    for (const resultCode of [
      'FILES_CANCELED',
      'CONTAINER_UPLOAD_',
      `CONTAINER_UPLOAD_${'A'.repeat(48)}`,
      'CONTAINER_UPLOAD_BAD-CHARACTER',
    ])
      expect(() => assertContainerUploadIntent({ ...intent('Canceled', 2), resultCode })).toThrow(
        /无法核实/,
      )
  })

  it('allows monotonic progress but never changes identity, bytes, digest or retained proof', () => {
    const awaiting = intent()
    const ready = { ...intent('Ready', 1), previousRetained: true }
    const completed = intent('Succeeded', 2)
    expect(containerUploadFollows(awaiting, ready)).toBe(true)
    expect(containerUploadFollows(ready, completed)).toBe(true)
    expect(containerUploadFollows(completed, completed)).toBe(true)
    expect(containerUploadFollows(ready, { ...completed, previousRetained: false })).toBe(false)
    expect(containerUploadFollows(ready, { ...completed, sha256: 'F'.repeat(64) })).toBe(false)
    expect(
      containerUploadFollows(ready, {
        ...completed,
        expiresAtUtc: '2099-01-01T00:16:00.000Z',
      }),
    ).toBe(false)
    expect(containerUploadFollows(awaiting, { ...awaiting, state: 'Receiving' })).toBe(false)
    expect(containerUploadFollows(awaiting, { ...awaiting, previousRetained: true })).toBe(false)
    expect(containerUploadFollows(completed, { ...completed, version: 3 })).toBe(false)
  })

  it('stops automatic lifecycle at Unknown while keeping it manually queryable and cancellable', () => {
    expect(containerUploadTerminal('Unknown')).toBe(false)
    expect(containerUploadTerminal('Succeeded')).toBe(true)
    expect(containerUploadState('Unknown')).toContain('停止自动操作')
  })
})

it('attribute task policy cannot disappear, appear unexpectedly or change during polling', () => {
  const row = intent()
  expect(
    assertContainerUploadIntent(
      { ...row, restoreAttributes: true },
      { ...binding, restoreAttributes: true },
    ).restoreAttributes,
  ).toBe(true)
  expect(() => assertContainerUploadIntent(row, { ...binding, restoreAttributes: true })).toThrow(
    /容器上传任务或实例绑定无法核实/,
  )
  expect(() => assertContainerUploadIntent({ ...row, restoreAttributes: true }, binding)).toThrow(
    /容器上传任务或实例绑定无法核实/,
  )
  expect(containerUploadFollows(row, { ...row, restoreAttributes: true, version: 1 })).toBe(false)
  expect(containerUploadFollows({ ...row, restoreAttributes: true }, { ...row, version: 1 })).toBe(
    false,
  )
})
