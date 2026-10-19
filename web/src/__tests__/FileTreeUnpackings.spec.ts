import { describe, expect, it } from 'vitest'
import {
  assertUnpackingIntent,
  canUnpackArchive,
  canReceiveUnpacking,
  sameUnpackingCommand,
  unpackingState,
  validUnpackingCommand,
} from '@/services/fileTreeUnpackings'
import type { TreeUnpackingIntent } from '@/services/fileTreeUnpackings'

function ready(): TreeUnpackingIntent {
  const id = 'fc1cac12-5815-4426-b80e-23db4fd3e974'
  return {
    id,
    planningJobId: id,
    executionJobId: null,
    state: 'Ready',
    command: {
      source: { rootId: 'test.tree', path: '输入.archive' },
      expectedVersion: 'A'.repeat(64),
      target: { rootId: 'test.tree', path: '新目录' },
      format: 'tar.gz',
    },
    entryCount: 10000,
    totalBytes: 16 * 1024 ** 3,
    readyExpiresAtUtc: new Date().toISOString(),
    resultCode: null,
    version: 3,
    publishedVersion: null,
    partialRetained: false,
    confirmation: {
      planHash: 'fixture-plan',
      expiresAtUtc: new Date().toISOString(),
      warning: '尚未解压',
    },
    archiveBytes: 17 * 1024 ** 3,
    archiveSha256: 'B'.repeat(64),
  }
}
describe('unpacking input proof and independent archive authorization', () => {
  it('requires a regular input with its own root grant and separate destination grants', () => {
    const root = {
      id: 'test.tree',
      name: 'test',
      actions: ['files-list', 'files-download', 'files-upload', 'files-mutate', 'files-archive'],
    }
    const entry = {
      name: 'input.archive',
      kind: 'File',
      isProtected: false,
      version: 'A'.repeat(64),
      sizeBytes: 1073756160,
      modifiedAtUnixMs: 0,
      mode: '0640',
    }
    expect(canUnpackArchive(root, entry)).toBe(false)
    expect(canReceiveUnpacking(root)).toBe(false)
    root.actions.push('files-tree-unpack')
    expect(canUnpackArchive(root, entry)).toBe(true)
    expect(canReceiveUnpacking(root)).toBe(true)
    expect(canUnpackArchive(root, { ...entry, kind: 'Directory' })).toBe(false)
    expect(canUnpackArchive(root, { ...entry, isProtected: true })).toBe(false)
    expect(canUnpackArchive(root, { ...entry, version: 'invalid' })).toBe(false)
    expect(canUnpackArchive(root, { ...entry, sizeBytes: 0 })).toBe(false)
    expect(canUnpackArchive(root, { ...entry, sizeBytes: 17 * 1024 ** 3 + 1 })).toBe(false)
  })
  it('never uses suffixes as permission or forces archive suffixes on the new directory', () => {
    const command = ready().command
    expect(validUnpackingCommand(command)).toBe(true)
    expect(validUnpackingCommand({ ...command, format: 'tar' })).toBe(true)
    for (const path of [
      '../escape',
      '/absolute',
      '输入.archive',
      '输入.archive/child',
      'a//b',
      'a\\b',
    ])
      expect(validUnpackingCommand({ ...command, target: { ...command.target, path } })).toBe(false)
    expect(sameUnpackingCommand(command, { ...command, format: 'tar' })).toBe(false)
    expect(sameUnpackingCommand(command, { ...command, expectedVersion: 'F'.repeat(64) })).toBe(
      false,
    )
  })
  it.each([
    'Ready',
    'UnpackingQueued',
    'Unpacking',
    'Unknown',
    'Succeeded',
    'Acknowledged',
    'Failed',
    'Canceled',
    'Expired',
  ])(
    '%s retains the original input proof rather than treating it as a new output archive',
    (state) => {
      const row = {
        ...ready(),
        state,
        confirmation: state === 'Ready' ? ready().confirmation : null,
        executionJobId: state === 'Ready' ? null : '129f14ed-3254-4868-9885-acad522acefa',
        publishedVersion: state === 'Succeeded' ? 'F'.repeat(64) : null,
        partialRetained: state === 'Unknown',
      }
      expect(assertUnpackingIntent(row)).toBe(row)
      for (const changed of [
        { ...row, archiveBytes: null },
        { ...row, archiveBytes: 0 },
        { ...row, archiveBytes: 17 * 1024 ** 3 + 1 },
        { ...row, archiveSha256: null },
        { ...row, archiveSha256: 'bad' },
      ])
        expect(() => assertUnpackingIntent(changed)).toThrow(/无法核实/)
    },
  )
  it('rejects fake output versions, missing execution bindings and foreign workflow states', () => {
    const row = ready()
    for (const changed of [
      { ...row, state: 'Succeeded' },
      { ...row, state: 'Copying' },
      { ...row, state: 'PackingQueued' },
      { ...row, state: 'Unknown', confirmation: null },
      { ...row, publishedVersion: 'F'.repeat(64) },
      { ...row, totalBytes: null },
    ])
      expect(() => assertUnpackingIntent(changed)).toThrow(/无法核实/)
    expect(unpackingState('Ready')).toContain('尚未解压')
    expect(unpackingState('Unknown')).toContain('结果未知')
  })
})
