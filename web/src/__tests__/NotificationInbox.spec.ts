import { describe, expect, it, vi } from 'vitest'
import { ApiError, type apiRequest } from '@/services/apiClient'
import { createNotificationInbox, type InboxMessage } from '@/services/notificationInbox'

const message = (id: string, sequence: number): InboxMessage => ({
  id,
  sequence,
  sourceKind: 'Job',
  sourceId: 'job-one',
  messageCode: 'JOB_SUCCEEDED',
  severity: 'Info',
  title: '任务已完成',
  summary: '后台任务已执行完成。',
  occurredAtUtc: new Date().toISOString(),
  publishedAtUtc: new Date().toISOString(),
  readAtUtc: null,
  targetPath: null,
})
const page = (items = [message('one', 10)], nextBefore: number | null = null) => ({
  items,
  nextBefore,
  snapshotSequence: 10,
})
const summary = { unreadCount: 2, totalCount: 3, snapshotSequence: 10 }
const snapshot = {
  snapshotId: '00000000-0000-0000-0000-000000000001',
  itemCount: 2000,
  expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
}
function create(handler: (path: string, init?: RequestInit) => Promise<unknown>) {
  const request = vi.fn<typeof handler>(handler)
  const inbox = createNotificationInbox(request as typeof apiRequest)
  inbox.reset(true)
  return { inbox, request }
}

describe('Notification inbox lifecycle', () => {
  it('rejects late summary and list responses after logout/downgrade', async () => {
    const resolvers: ((value: unknown) => void)[] = []
    const { inbox } = create(() => new Promise((resolve) => resolvers.push(resolve)))
    const reading = inbox.load()
    const counting = inbox.refreshSummary()
    inbox.reset(false)
    resolvers[0]!(page())
    resolvers[1]!(summary)
    await Promise.all([reading, counting])
    expect(inbox.state.items).toEqual([])
    expect(inbox.state.unreadCount).toBe(0)
  })
  it('prevents overlapping summary polls and cancels on shell reset', async () => {
    let signal: AbortSignal | undefined
    let finish: (value: unknown) => void = () => {}
    const { inbox, request } = create((_, init) => {
      signal = init?.signal as AbortSignal
      return new Promise((resolve) => {
        finish = resolve
      })
    })
    const first = inbox.refreshSummary()
    await inbox.refreshSummary()
    expect(request).toHaveBeenCalledTimes(1)
    inbox.reset(false)
    expect(signal!.aborted).toBe(true)
    finish(summary)
    await first
    expect(inbox.state.unreadCount).toBe(0)
  })
  it('uses one snapshot across read-all batches and leaves newer messages unread', async () => {
    const snapshots: string[] = []
    const { inbox } = create(async (path, init) => {
      if (path === 'notifications/read-all-snapshot') return snapshot
      if (path === 'notifications/read-all') {
        snapshots.push(JSON.parse(init!.body as string).snapshotId)
        return { changedCount: 1000, processedCount: 1000, hasMore: snapshots.length < 2 }
      }
      if (path === 'notifications/summary')
        return { ...summary, unreadCount: 1, snapshotSequence: 11 }
      return page([message('new', 11)])
    })
    inbox.state.snapshotSequence = 10
    await inbox.markAllRead()
    expect(snapshots).toEqual([snapshot.snapshotId, snapshot.snapshotId])
    expect(inbox.state.unreadCount).toBe(1)
    expect(inbox.state.items[0]!.id).toBe('new')
  })
  it('does not report a partial read-all as complete when a later batch fails', async () => {
    let batches = 0
    const { inbox } = create(async (path) => {
      if (path === 'notifications/read-all-snapshot') return snapshot
      if (++batches === 2) throw new ApiError('unavailable', 503, {})
      return { changedCount: 1000, processedCount: 1000, hasMore: true }
    })
    inbox.state.snapshotSequence = 10
    await inbox.markAllRead()
    expect(inbox.state.error).toContain('重试')
    expect(inbox.state.writing).toBe(false)
    expect(batches).toBe(2)
  })
  it('stops read-all on close even when its transport ignores abort', async () => {
    let finish: (value: unknown) => void = () => {}
    const { inbox, request } = create(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    inbox.state.snapshotSequence = 10
    const writing = inbox.markAllRead()
    inbox.cancelView()
    finish(snapshot)
    await writing
    expect(request).toHaveBeenCalledTimes(1)
    expect(inbox.state.writing).toBe(false)
  })
  it('stops after an already submitted batch when close ignores transport abort', async () => {
    let finish: (value: unknown) => void = () => {}
    const { inbox, request } = create(async (path) =>
      path === 'notifications/read-all-snapshot'
        ? snapshot
        : new Promise((resolve) => {
            finish = resolve
          }),
    )
    const writing = inbox.markAllRead()
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2))
    inbox.cancelView()
    finish({ changedCount: 1000, processedCount: 1000, hasMore: true })
    await writing
    expect(request).toHaveBeenCalledTimes(2)
    expect(inbox.state.writing).toBe(false)
  })
  it('continues when members were separately read but the snapshot batch progressed', async () => {
    let batches = 0
    const { inbox } = create(async (path) => {
      if (path === 'notifications/read-all-snapshot') return snapshot
      if (path === 'notifications/read-all')
        return ++batches === 1
          ? { changedCount: 0, processedCount: 1000, hasMore: true }
          : { changedCount: 5, processedCount: 5, hasMore: false }
      return path === 'notifications/summary' ? summary : page()
    })
    await inbox.markAllRead()
    expect(batches).toBe(2)
    expect(inbox.state.error).toBe('')
  })
  it.each([undefined, 'invalid-id'])(
    'does not write a batch with invalid snapshot id %s',
    async (id) => {
      const { inbox, request } = create(async () => ({ ...snapshot, snapshotId: id }))
      await inbox.markAllRead()
      expect(request).toHaveBeenCalledTimes(1)
      expect(inbox.state.error).toContain('重试')
    },
  )
  it('does not impose an arbitrary total 100k cap on a prepared snapshot', async () => {
    let batches = 0
    const { inbox } = create(async (path) => {
      if (path === 'notifications/read-all-snapshot') return { ...snapshot, itemCount: 100005 }
      if (path === 'notifications/read-all') {
        batches++
        return { changedCount: 1000, processedCount: 1000, hasMore: false }
      }
      return path === 'notifications/summary' ? summary : page()
    })
    await inbox.markAllRead()
    expect(batches).toBe(1)
    expect(inbox.state.error).toBe('')
  })
  it('explains expired snapshots without pretending partial changes were rolled back', async () => {
    const { inbox } = create(async (path) => {
      if (path === 'notifications/read-all-snapshot') return snapshot
      throw new ApiError('expired', 404, { code: 'NOTIFICATION_READ_SNAPSHOT_UNAVAILABLE' })
    })
    await inbox.markAllRead()
    expect(inbox.state.error).toContain('不会回退')
    expect(inbox.state.writing).toBe(false)
  })
  it.each([401, 403])('clears all private rows and counters on %s', async (status) => {
    const { inbox } = create(async () => {
      throw new ApiError('denied', status, {})
    })
    inbox.state.items = [message('private', 1)]
    inbox.state.unreadCount = 7
    await inbox.load()
    expect(inbox.state.items).toEqual([])
    expect(inbox.state.unreadCount).toBe(0)
    expect(inbox.state.error).toContain('重新登录')
  })
  it('does not let an old filter response replace the newest filter', async () => {
    let finish: (value: unknown) => void = () => {}
    const { inbox, request } = create(async () =>
      request.mock.calls.length === 1
        ? new Promise((resolve) => {
            finish = resolve
          })
        : page([message('filtered', 2)]),
    )
    const old = inbox.load()
    inbox.state.status = 'unread'
    await inbox.load()
    finish(page([message('old', 1)]))
    await old
    expect(inbox.state.items.map((item) => item.id)).toEqual(['filtered'])
  })
  it('deduplicates pagination boundaries without replacing existing messages', async () => {
    const { inbox } = create(async (path) =>
      path.includes('before=10')
        ? page([message('one', 10), message('two', 9)])
        : page([message('one', 10)], 10),
    )
    await inbox.load()
    await inbox.load(true)
    expect(inbox.state.items.map((item) => item.id)).toEqual(['one', 'two'])
  })
  it('does not expose a stale unread badge after summary failure', async () => {
    const { inbox } = create(async () => {
      throw new ApiError('unavailable', 503, {})
    })
    inbox.state.unreadCount = 8
    await inbox.refreshSummary()
    expect(inbox.state.unreadCount).toBe(0)
    expect(inbox.state.summaryUnavailable).toBe(true)
  })
})
