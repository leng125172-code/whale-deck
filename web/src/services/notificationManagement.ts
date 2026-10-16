import { apiRequest, createIdempotencyKey } from '@/services/apiClient'

export type NotificationProvider = 'Email' | 'Webhook' | 'DingTalk' | 'WeCom' | 'Feishu'
export interface NotificationAttempt {
  attemptNumber: number
  startedAtUtc: string
  finishedAtUtc: string | null
  resultCode: string | null
}
export interface NotificationPage<T> {
  items: T[]
  hasMore: boolean
  page: number
  take: number
}
export interface NotificationChannel {
  id: string
  name: string
  provider: NotificationProvider
  destinationReference: string
  hasCredential: boolean
  isEnabled: boolean
  privateConfigurationBlocked?: boolean
  version: number
  createdAtUtc: string
}
export interface NotificationRoute {
  id: string
  channelId: string
  sourceKind: 'all' | 'Alert' | 'Job'
  minimumSeverity: 'Info' | 'Warning' | 'Error' | 'Critical'
  includeRecovery: boolean
  isEnabled: boolean
  version: number
  createdAtUtc: string
}
export interface NotificationDelivery {
  id: string
  notificationId: string
  channelId: string
  channelVersion: number
  provider: NotificationProvider
  state: string
  attemptCount: number
  createdAtUtc: string
  nextAttemptAtUtc: string
  lastAttemptAtUtc: string | null
  completedAtUtc: string | null
  lastResultCode: string | null
  version: number
  intentKind: 'Automatic' | 'Test' | 'ManualRetry'
  originDeliveryId: string | null
  hasManualRetry: boolean
}
export interface NotificationSubmission {
  deliveryId: string
  notificationId: string
  state: string
  intentKind: string
  originDeliveryId: string | null
}
export interface NotificationWriteIntent {
  path: string
  method: 'POST' | 'PUT' | 'DELETE'
  body: string
  key: string
}
export function notificationIntent(
  path: string,
  method: NotificationWriteIntent['method'],
  command: object,
): NotificationWriteIntent {
  return { path, method, body: JSON.stringify(command), key: createIdempotencyKey() }
}
export function executeNotificationIntent<T>(intent: NotificationWriteIntent, signal: AbortSignal) {
  return apiRequest<T>(`notifications/manage/${intent.path}`, {
    method: intent.method,
    body: intent.body,
    headers: { 'Idempotency-Key': intent.key },
    signal,
  })
}
export function notificationPage<T>(
  kind: 'channels' | 'routes' | 'deliveries',
  page: number,
  signal: AbortSignal,
  filters: { channelId?: string; state?: string } = {},
  take = 25,
) {
  const query = new URLSearchParams({ page: String(page), take: String(take) })
  if (filters.channelId) query.set('channelId', filters.channelId)
  if (filters.state) query.set('state', filters.state)
  return apiRequest<NotificationPage<T>>(`notifications/manage/${kind}?${query}`, { signal })
}
export const notificationStates: Record<string, string> = {
  Pending: '待处理',
  Leased: '已领取',
  Sending: '发送中',
  RetryWait: '等待重试',
  Succeeded: '已接受',
  Failed: '失败',
  Unknown: '结果未知',
  Canceled: '已取消',
}
