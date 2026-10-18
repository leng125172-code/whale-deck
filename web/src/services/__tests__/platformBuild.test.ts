import { describe, expect, it } from 'vitest'
import { validateBuildIdentity } from '../platformBuild'

describe('immutable build identity', () => {
  const valid = { component: 'WhaleDeck.Api', version: '0.1.0', sourceRevision: 'a'.repeat(40) }
  it('preserves the exact full binary revision, not a runtime version guess', () => {
    expect(validateBuildIdentity({ ...valid, secret: 'not-projected' }, 'WhaleDeck.Api')).toEqual(
      valid,
    )
  })
  it('does not replace a missing recorded revision', () => {
    expect(
      validateBuildIdentity({ ...valid, sourceRevision: null }, 'WhaleDeck.Api').sourceRevision,
    ).toBeNull()
  })
  it.each([
    null,
    { ...valid, component: 'WhaleDeck.Worker' },
    { ...valid, version: '<img>' },
    { ...valid, sourceRevision: 'a'.repeat(7) },
    { ...valid, sourceRevision: 'a'.repeat(40) + '.dirty' },
    { ...valid, sourceRevision: undefined },
  ])('rejects malformed or wrong-component provenance %j', (value) => {
    expect(() => validateBuildIdentity(value, 'WhaleDeck.Api')).toThrow('版本信息无效')
  })
})
