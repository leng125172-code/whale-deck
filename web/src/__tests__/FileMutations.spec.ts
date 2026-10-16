import { describe, expect, it } from 'vitest'
import {
  mutationEligible,
  mutationLabel,
  mutationState,
  mutationTarget,
  mutationTerminal,
} from '@/services/fileMutations'

describe('registered-file operation presentation and validation', () => {
  it.each(['../a', '/etc/passwd', 'a//b', 'a/./b', 'a\\b', 'a\nb', '', '中'.repeat(86)])(
    'rejects unsafe or excessive leaf %s',
    (path) => expect(mutationTarget(path)).toBe(false),
  )
  it('accepts relative Unicode paths without treating characters as byte count', () => {
    expect(mutationTarget('资料/中文.txt')).toBe(true)
    expect(mutationTarget('a'.repeat(255))).toBe(true)
    expect(mutationTarget('a'.repeat(256))).toBe(false)
  })
  it('only exposes single regular unprotected objects to mutation menus', () => {
    const entry = {
      name: 'source',
      kind: 'File',
      sizeBytes: 1,
      modifiedAtUnixMs: 0,
      version: 'A'.repeat(64),
      mode: '0600',
      isProtected: false,
    }
    expect(mutationEligible(entry)).toBe(true)
    expect(mutationEligible({ ...entry, isProtected: true })).toBe(false)
    expect(mutationEligible({ ...entry, kind: 'Directory' })).toBe(false)
  })
  it('never calls Unknown terminal/success or human acceptance successful', () => {
    expect(mutationTerminal('Unknown')).toBe(false)
    expect(mutationTerminal('Acknowledged')).toBe(true)
    expect(mutationState('Acknowledged')).toContain('不代表成功')
    expect(mutationLabel('chmod')).toBe('修改权限')
  })
})
