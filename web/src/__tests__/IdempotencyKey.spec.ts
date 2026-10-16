import { afterEach, expect, it, vi } from 'vitest'
import { createIdempotencyKey } from '@/services/apiClient'

afterEach(() => vi.unstubAllGlobals())
it('uses native secure-context UUID when available', () => {
  vi.stubGlobal('crypto', { randomUUID: () => '4871e73a-84ac-4267-a3f1-a243221ddf16' })
  expect(createIdempotencyKey()).toBe('4871e73a-84ac-4267-a3f1-a243221ddf16')
})
it('supports HTTP/IP contexts using cryptographic random bytes with UUIDv4 bits', () => {
  const source = globalThis.crypto.getRandomValues.bind(globalThis.crypto)
  vi.stubGlobal('crypto', { getRandomValues: source })
  const first = createIdempotencyKey()
  const second = createIdempotencyKey()
  expect(first).toMatch(/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/)
  expect(second).not.toBe(first)
})
