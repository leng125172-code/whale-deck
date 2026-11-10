import { apiRequest } from './apiClient'

export interface WorkspaceRetirement {
  retirementId: string
  workspaceId: string
  archiveId: string
  state: string
  phase: string
  code: string | null
  awaitingConfirmation: boolean
  authorizationRequired: boolean
  cancelRequested: boolean
  attentionRequired: boolean
  planJson: string | null
  planExpiresAtUtc: string | null
  confirmedAtUtc: string | null
  executionJson: string | null
  completedAtUtc: string | null
  sourceRetired: boolean
  budgetReleased: boolean
  retainsHdd: boolean
}
const scope = (id: string) => 'development/workspaces/' + encodeURIComponent(id) + '/retirements'
const post = (signal: AbortSignal, body?: object, key?: string): RequestInit => ({
  method: 'POST',
  signal,
  body: body ? JSON.stringify(body) : undefined,
  headers: key ? { 'Idempotency-Key': key } : undefined,
})
export const workspaceRetirements = {
  current: (id: string, signal: AbortSignal) =>
    apiRequest<WorkspaceRetirement | null>(scope(id), { signal }),
  begin: (id: string, archiveId: string, expectedEpoch: number, key: string, signal: AbortSignal) =>
    apiRequest<WorkspaceRetirement>(scope(id), post(signal, { archiveId, expectedEpoch }, key)),
  confirm: (id: string, retirementId: string, planSha256: string, signal: AbortSignal) =>
    apiRequest<WorkspaceRetirement>(
      scope(id) + '/' + encodeURIComponent(retirementId) + '/confirm',
      post(signal, { planSha256, deleteSourceAndReleaseBudget: true }),
    ),
  cancel: (id: string, retirementId: string, signal: AbortSignal) =>
    apiRequest<WorkspaceRetirement>(
      scope(id) + '/' + encodeURIComponent(retirementId) + '/cancel',
      post(signal),
    ),
  authorize: (id: string, retirementId: string, signal: AbortSignal) =>
    apiRequest<WorkspaceRetirement>(
      scope(id) + '/' + encodeURIComponent(retirementId) + '/authorize',
      post(signal),
    ),
}
