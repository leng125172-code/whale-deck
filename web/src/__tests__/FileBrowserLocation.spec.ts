import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  clearFileBrowserLocation,
  fileBrowserLocationKey,
  reconcileFileBrowserLocation,
  rememberFileBrowserLocation,
  restoreFileBrowserLocation,
} from '@/services/fileBrowserLocation'

const roots = [{ id: 'whaledeck.files.shared', name: '共享', actions: ['files-list'] }]
const location = { rootId: roots[0]!.id, path: '资料/中文😀/a[1]*' }
beforeEach(() => sessionStorage.clear())
afterEach(() => vi.restoreAllMocks())
describe('tab-local directory navigation hint, never authorization or execution state', () => {
  it('restores only root/path after current subject and authoritative roots validation', () => {
    rememberFileBrowserLocation('admin-1', location)
    expect(restoreFileBrowserLocation('admin-1', roots)).toEqual(location)
    expect(
      restoreFileBrowserLocation('admin-1', [
        { id: 'new-first-root', name: '新默认根', actions: ['files-list'] },
        ...roots,
      ]),
    ).toEqual(location)
    expect(JSON.parse(sessionStorage.getItem(fileBrowserLocationKey)!)).toEqual({
      schema: 1,
      subject: 'admin-1',
      ...location,
    })
    expect(localStorage.getItem(fileBrowserLocationKey)).toBeNull()
  })
  it.each(['', 'admin-2', undefined])('clears another or missing subject %s', (subject) => {
    rememberFileBrowserLocation('admin-1', location)
    expect(reconcileFileBrowserLocation(subject, true)).toBeNull()
    expect(sessionStorage.getItem(fileBrowserLocationKey)).toBeNull()
  })
  it('clears on ordinary identity, logout, or revoked root, including lost list capability', () => {
    for (const check of [
      () => reconcileFileBrowserLocation('admin-1', false),
      () => restoreFileBrowserLocation('admin-1', []),
      () => restoreFileBrowserLocation('admin-1', [{ ...roots[0]!, actions: [] }]),
      () => clearFileBrowserLocation(),
    ]) {
      rememberFileBrowserLocation('admin-1', location)
      check()
      expect(sessionStorage.getItem(fileBrowserLocationKey)).toBeNull()
    }
  })
  it.each([
    '../a',
    'a/../b',
    '/a',
    'a//b',
    'a/',
    'a\\b',
    'a\n',
    'a\u0085',
    '\ud800',
    '中'.repeat(86),
    Array(18).fill('a'.repeat(254)).join('/'),
  ])('rejects noncanonical or over-budget path %j', (path) => {
    sessionStorage.setItem(
      fileBrowserLocationKey,
      JSON.stringify({ schema: 1, subject: 'admin-1', rootId: location.rootId, path }),
    )
    expect(restoreFileBrowserLocation('admin-1', roots)).toBeNull()
    expect(sessionStorage.getItem(fileBrowserLocationKey)).toBeNull()
    rememberFileBrowserLocation('admin-1', { ...location, path })
    expect(sessionStorage.getItem(fileBrowserLocationKey)).toBeNull()
  })
  it('accepts root and exact UTF8 name limit, rejects corrupt or oversized schema', () => {
    rememberFileBrowserLocation('admin-1', { ...location, path: '' })
    expect(restoreFileBrowserLocation('admin-1', roots)?.path).toBe('')
    rememberFileBrowserLocation('admin-1', { ...location, path: '中'.repeat(85) })
    expect(restoreFileBrowserLocation('admin-1', roots)?.path).toBe('中'.repeat(85))
    for (const raw of [
      'null',
      '{}',
      '{',
      'x'.repeat(24577),
      JSON.stringify({ schema: 2, subject: 'admin-1', ...location }),
    ]) {
      sessionStorage.setItem(fileBrowserLocationKey, raw)
      expect(restoreFileBrowserLocation('admin-1', roots)).toBeNull()
      expect(sessionStorage.getItem(fileBrowserLocationKey)).toBeNull()
    }
  })
  it('ignores storage failures without disabling browsing or logout', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('full')
    })
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    expect(() => rememberFileBrowserLocation('admin-1', location)).not.toThrow()
    expect(() => clearFileBrowserLocation()).not.toThrow()
    expect(restoreFileBrowserLocation('admin-1', roots)).toBeNull()
  })
})
