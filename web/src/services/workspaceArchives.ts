import { apiRequest } from './apiClient'

export interface WorkspaceArchive {
  archiveId: string
  workspaceId: string
  state: string
  phase: string
  code: string | null
  awaitingConfirmation: boolean
  authorizationRequired: boolean
  cancelRequested: boolean
  planJson: string | null
  captureJson: string | null
  verifyJson: string | null
  completedAtUtc: string | null
  retainsSource: boolean
  reservesBudget: boolean
}
const scope = (id: string) => 'development/workspaces/' + encodeURIComponent(id) + '/archives'
const post = (signal: AbortSignal, body?: object, key?: string): RequestInit => ({
  method: 'POST',
  signal,
  body: body ? JSON.stringify(body) : undefined,
  headers: key ? { 'Idempotency-Key': key } : undefined,
})
export const workspaceArchives = {
  current: (id: string, signal: AbortSignal) =>
    apiRequest<WorkspaceArchive | null>(scope(id), { signal }),
  get: (id: string, archiveId: string, signal: AbortSignal) =>
    apiRequest<WorkspaceArchive>(scope(id) + '/' + encodeURIComponent(archiveId), { signal }),
  begin: (id: string, expectedEpoch: number, key: string, signal: AbortSignal) =>
    apiRequest<WorkspaceArchive>(scope(id), post(signal, { expectedEpoch }, key)),
  confirm: (id: string, archiveId: string, planSha256: string, signal: AbortSignal) =>
    apiRequest<WorkspaceArchive>(
      scope(id) + '/' + encodeURIComponent(archiveId) + '/confirm',
      post(signal, { planSha256 }),
    ),
  cancel: (id: string, archiveId: string, signal: AbortSignal) =>
    apiRequest<WorkspaceArchive>(
      scope(id) + '/' + encodeURIComponent(archiveId) + '/cancel',
      post(signal),
    ),
  authorize: (id: string, archiveId: string, signal: AbortSignal) =>
    apiRequest<WorkspaceArchive>(
      scope(id) + '/' + encodeURIComponent(archiveId) + '/authorize',
      post(signal),
    ),
}
