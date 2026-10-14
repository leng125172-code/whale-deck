import { reactive } from 'vue'
import { ApiError, apiRequest } from '@/services/apiClient'

export interface InboxMessage {
  id: string
  sequence: number
  sourceKind: 'Alert' | 'Job'
  sourceId: string
  messageCode: string
  severity: string
  title: string
  summary: string
  occurredAtUtc: string
  publishedAtUtc: string
  readAtUtc: string | null
  targetPath: string | null
}
interface InboxPage {
  items: InboxMessage[]
  nextBefore: number | null
  snapshotSequence: number
}
interface InboxSummary {
  unreadCount: number
  totalCount: number
  snapshotSequence: number
}
type Request = typeof apiRequest

/** One inbox per shell. Reset invalidates even a transport that ignores cancellation. */
export function createNotificationInbox(request: Request = apiRequest) {
  const state = reactive({
    items: [] as InboxMessage[],
    unreadCount: 0,
    totalCount: 0,
    snapshotSequence: 0,
    nextBefore: null as number | null,
    status: 'all' as 'all' | 'unread',
    kind: 'all' as 'all' | 'Alert' | 'Job',
    loading: false,
    writing: false,
    error: '',
    summaryUnavailable: false,
  })
  let active = false
  let epoch = 0
  let viewGeneration = 0
  let summaryGeneration = 0
  let viewController: AbortController | null = null
  let summaryController: AbortController | null = null

  function cancelView() {
    viewGeneration++
    viewController?.abort()
    viewController = null
    state.loading = false
    state.writing = false
  }

  function reset(enabled: boolean) {
    active = enabled
    epoch++
    summaryGeneration++
    summaryController?.abort()
    summaryController = null
    cancelView()
    Object.assign(state, {
      items: [],
      unreadCount: 0,
      totalCount: 0,
      snapshotSequence: 0,
      nextBefore: null,
      error: '',
      summaryUnavailable: false,
      status: 'all',
      kind: 'all',
    })
  }

  function failure(error: unknown) {
    if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
      reset(false)
      state.error = '会话或权限已变化，请重新登录后查看消息。'
    } else if (
      error instanceof ApiError &&
      error.problem.code === 'NOTIFICATION_READ_SNAPSHOT_UNAVAILABLE'
    ) {
      state.error = '已读快照已失效，已处理的消息不会回退；请重新点击“全部已读”。'
    } else {
      state.error = '消息服务暂时不可用，请重试。'
    }
  }

  async function refreshSummary(force = false) {
    if (!active || (summaryController && !force)) return
    summaryController?.abort()
    const controller = new AbortController()
    summaryController = controller
    const generation = ++summaryGeneration
    const session = epoch
    try {
      const result = await request<InboxSummary>('notifications/summary', {
        signal: controller.signal,
      })
      if (session !== epoch || generation !== summaryGeneration) return
      if (
        ![result.unreadCount, result.totalCount, result.snapshotSequence].every(
          (value) => Number.isSafeInteger(value) && value >= 0,
        ) ||
        result.unreadCount > result.totalCount
      )
        throw new Error('Invalid notification summary')
      state.unreadCount = result.unreadCount
      state.totalCount = result.totalCount
      state.snapshotSequence = result.snapshotSequence
      state.summaryUnavailable = false
    } catch (error) {
      if (session !== epoch || generation !== summaryGeneration || controller.signal.aborted) return
      state.summaryUnavailable = true
      // Stale numbers are not presented as a current unread count.
      state.unreadCount = 0
      if (error instanceof ApiError && [401, 403].includes(error.status)) failure(error)
    } finally {
      if (generation === summaryGeneration) summaryController = null
    }
  }

  async function load(more = false) {
    if (!active || state.writing || (more && (state.loading || state.nextBefore === null))) return
    cancelView()
    const generation = viewGeneration
    const session = epoch
    const controller = new AbortController()
    viewController = controller
    const query = new URLSearchParams({ take: '30', status: state.status, kind: state.kind })
    if (more) query.set('before', String(state.nextBefore))
    else {
      state.items = []
      state.nextBefore = null
    }
    state.loading = true
    state.error = ''
    try {
      const page = await request<InboxPage>(`notifications?${query}`, { signal: controller.signal })
      if (session !== epoch || generation !== viewGeneration) return
      if (
        !Array.isArray(page.items) ||
        !Number.isSafeInteger(page.snapshotSequence) ||
        page.snapshotSequence < 0 ||
        (page.nextBefore !== null &&
          (!Number.isSafeInteger(page.nextBefore) || page.nextBefore <= 0))
      )
        throw new Error('Invalid notification page')
      const ids = new Set(state.items.map((item) => item.id))
      state.items = more
        ? [...state.items, ...page.items.filter((item) => !ids.has(item.id))]
        : page.items
      state.nextBefore = page.nextBefore
      state.snapshotSequence = page.snapshotSequence
    } catch (error) {
      if (session === epoch && generation === viewGeneration && !controller.signal.aborted)
        failure(error)
    } finally {
      if (generation === viewGeneration) {
        state.loading = false
        viewController = null
      }
    }
  }

  async function write(operation: (signal: AbortSignal) => Promise<void>) {
    if (!active || state.loading || state.writing) return
    cancelView()
    const controller = new AbortController()
    viewController = controller
    const generation = viewGeneration
    const session = epoch
    state.writing = true
    state.error = ''
    try {
      await operation(controller.signal)
      if (generation !== viewGeneration || session !== epoch) return
      state.writing = false
      await Promise.all([load(), refreshSummary(true)])
    } catch (error) {
      if (session === epoch && generation === viewGeneration && !controller.signal.aborted)
        failure(error)
    } finally {
      if (generation === viewGeneration) {
        state.writing = false
        viewController = null
      }
    }
  }

  function setRead(item: InboxMessage, isRead: boolean) {
    return write(async (signal) => {
      await request(`notifications/${encodeURIComponent(item.id)}/read-state`, {
        method: 'POST',
        body: JSON.stringify({ isRead }),
        signal,
      })
    })
  }

  function markAllRead() {
    return write(async (signal) => {
      const snapshot = await request<{
        snapshotId: string
        itemCount: number
        expiresAtUtc: string
      }>('notifications/read-all-snapshot', { method: 'POST', signal })
      if (signal.aborted) return
      if (
        !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(
          snapshot.snapshotId,
        ) ||
        !Number.isSafeInteger(snapshot.itemCount) ||
        snapshot.itemCount < 0 ||
        !Number.isFinite(Date.parse(snapshot.expiresAtUtc))
      )
        throw new Error('Invalid read-all snapshot')
      let remaining = true
      while (remaining && !signal.aborted) {
        const result = await request<{
          changedCount: number
          hasMore: boolean
          processedCount: number
        }>('notifications/read-all', {
          method: 'POST',
          body: JSON.stringify({ snapshotId: snapshot.snapshotId }),
          signal,
        })
        if (
          !Number.isSafeInteger(result.changedCount) ||
          !Number.isSafeInteger(result.processedCount) ||
          result.changedCount < 0 ||
          result.processedCount < result.changedCount ||
          result.processedCount > 1000 ||
          typeof result.hasMore !== 'boolean'
        )
          throw new Error('Invalid read-all batch')
        remaining = result.hasMore
        if (remaining && result.processedCount === 0) throw new Error('Read batch made no progress')
      }
    })
  }

  function setFilters(status: typeof state.status, kind: typeof state.kind) {
    if (state.writing) return
    state.status = status
    state.kind = kind
  }

  return { state, reset, cancelView, refreshSummary, load, setRead, markAllRead, setFilters }
}

export type NotificationInbox = ReturnType<typeof createNotificationInbox>
