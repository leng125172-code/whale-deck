import { describe, expect, it } from 'vitest'
import {
  cloneRecursiveAclCommand,
  sameRecursiveAclCommand,
  validRecursiveAclCommand,
} from '@/services/containerRecursiveAcls'
import type { ContainerRecursiveAclCommand } from '@/services/containerRecursiveAcls'
const entries = () => [
  { tag: 'user_obj' as const, id: null, permissions: 6 },
  { tag: 'user' as const, id: 0, permissions: 0 },
  { tag: 'group_obj' as const, id: null, permissions: 0 },
  { tag: 'mask' as const, id: null, permissions: 4 },
  { tag: 'other' as const, id: null, permissions: 0 },
]
function command(): ContainerRecursiveAclCommand {
  return {
    location: {
      containerId: 'a'.repeat(64),
      instanceId: '777:2026-11-04T01:02:03Z',
      path: 'work/树',
    },
    aclType: 'access',
    fileEntries: entries(),
    directoryEntries: entries(),
    expectedVersion: 'A'.repeat(64),
  }
}
describe('recursive ACL independent target snapshots', () => {
  it('captures both arrays, location, named zero and permissions zero without aliasing', () => {
    const original = command(),
      clone = cloneRecursiveAclCommand(original)
    expect(sameRecursiveAclCommand(original, clone)).toBe(true)
    original.fileEntries![1]!.id = 2201
    original.directoryEntries[0]!.permissions = 7
    original.location.path = 'work/other'
    expect(clone.fileEntries![1]).toEqual({ tag: 'user', id: 0, permissions: 0 })
    expect(clone.directoryEntries[0]!.permissions).toBe(6)
    expect(clone.location.path).toBe('work/树')
    expect(sameRecursiveAclCommand(original, clone)).toBe(false)
  })
  it.each(['fileEntries', 'directoryEntries'] as const)(
    'rejects duplicate, unordered, missing-mask and invalid numeric targets in %s',
    (key) => {
      for (const mutate of [
        (v: ContainerRecursiveAclCommand) => v[key]!.push({ ...v[key]![4]! }),
        (v: ContainerRecursiveAclCommand) => v[key]!.reverse(),
        (v: ContainerRecursiveAclCommand) => v[key]!.splice(3, 1),
        (v: ContainerRecursiveAclCommand) => {
          v[key]![1]!.id = Number.NaN
        },
      ]) {
        const v = command()
        mutate(v)
        expect(validRecursiveAclCommand(v)).toBe(false)
      }
    },
  )
  it('default explicit clear keeps null files, not an empty access list', () => {
    const v: ContainerRecursiveAclCommand = {
        ...command(),
        aclType: 'default',
        fileEntries: null,
        directoryEntries: [],
      },
      clone = cloneRecursiveAclCommand(v)
    expect(validRecursiveAclCommand(clone)).toBe(true)
    expect(clone.fileEntries).toBeNull()
    expect(clone.directoryEntries).toEqual([])
    expect(validRecursiveAclCommand({ ...clone, fileEntries: [] })).toBe(false)
  })
})
