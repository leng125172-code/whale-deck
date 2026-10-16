import { afterEach, describe, expect, it, vi } from 'vitest'
import { parseJobEvent, subscribeToJob } from '@/services/operations'
import type { JobEvent } from '@/services/contracts'

const event: JobEvent = {
  sequence: 1,
  state: 'Running',
  phase: 'Applying',
  progressPercent: 10,
  messageCode: 'RUNNING',
  occurredAtUtc: '2026-10-16T01:00:00Z',
}
class Source {
  static CLOSED = 2
  static instances: Source[] = []
  readyState = 0
  onopen?: () => void
  onerror?: () => void
  progress?: (message: { data: string }) => void
  close = vi.fn<() => void>(() => {
    this.readyState = Source.CLOSED
  })
  constructor(
    public url: string,
    public options: unknown,
  ) {
    Source.instances.push(this)
  }
  addEventListener(_name: string, callback: (message: { data: string }) => void) {
    this.progress = callback
  }
  emit(value = event) {
    this.progress?.({ data: JSON.stringify(value) })
  }
}
afterEach(() => {
  vi.unstubAllGlobals()
  Source.instances = []
})
describe('job event protocol', () => {
  it('accepts camelCase and the previous PascalCase event fields', () => {
    expect(parseJobEvent(JSON.stringify(event))).toEqual(event)
    expect(
      parseJobEvent(
        JSON.stringify(
          Object.fromEntries(
            Object.entries(event).map(([key, value]) => [
              key[0]!.toUpperCase() + key.slice(1),
              value,
            ]),
          ),
        ),
      ),
    ).toEqual(event)
  })
  it.each([
    'null',
    '[]',
    '{',
    JSON.stringify({ ...event, sequence: -1 }),
    JSON.stringify({ ...event, occurredAtUtc: 'bad' }),
    JSON.stringify({ ...event, progressPercent: 200 }),
    'x'.repeat(8193),
  ])('rejects malformed or oversized input %s', (raw) => {
    expect(parseJobEvent(raw)).toBeNull()
  })
  it('keeps the same browser cursor subscription through reconnect and deduplicates events', () => {
    vi.stubGlobal('EventSource', Source)
    const progress = vi.fn<(value: typeof event) => void>(),
      error = vi.fn<() => void>()
    const close = subscribeToJob('job/id', { progress, error })
    const source = Source.instances[0]!
    expect(source.url).toContain('job%2Fid/events')
    expect(source.options).toEqual({ withCredentials: true })
    source.emit()
    source.onerror?.()
    expect(source.close).not.toHaveBeenCalled()
    source.onopen?.()
    source.emit()
    source.emit({ ...event, sequence: 2 })
    expect(Source.instances).toHaveLength(1)
    expect(progress).toHaveBeenCalledTimes(2)
    close()
    source.emit({ ...event, sequence: 3 })
    expect(progress).toHaveBeenCalledTimes(2)
  })
  it('closes on terminal state before clean EOF can trigger a reconnect', () => {
    vi.stubGlobal('EventSource', Source)
    const progress = vi.fn<(value: typeof event) => void>(),
      error = vi.fn<() => void>()
    subscribeToJob('job', { progress, error })
    const source = Source.instances[0]!
    source.emit({ ...event, state: 'Succeeded' })
    source.onerror?.()
    expect(progress).toHaveBeenCalledOnce()
    expect(source.close).toHaveBeenCalledOnce()
    expect(error).not.toHaveBeenCalled()
  })
  it('bounds consecutive failed opens and resets the budget after a successful open', () => {
    vi.stubGlobal('EventSource', Source)
    subscribeToJob('job', { progress: vi.fn<(value: typeof event) => void>() })
    const source = Source.instances[0]!
    for (let index = 0; index < 4; index++) source.onerror?.()
    source.onopen?.()
    for (let index = 0; index < 4; index++) source.onerror?.()
    expect(source.close).not.toHaveBeenCalled()
    source.onerror?.()
    expect(source.close).toHaveBeenCalledOnce()
  })
  it('fails closed on invalid progress without invoking the caller with an invalid shape', () => {
    vi.stubGlobal('EventSource', Source)
    const progress = vi.fn<(value: typeof event) => void>(),
      error = vi.fn<() => void>()
    subscribeToJob('job', { progress, error })
    const source = Source.instances[0]!
    source.progress?.({ data: '{}' })
    expect(progress).not.toHaveBeenCalled()
    expect(error).toHaveBeenCalledOnce()
    expect(source.close).toHaveBeenCalledOnce()
  })
})
