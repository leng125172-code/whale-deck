import type { DirectoryCommand, DirectoryRegistration } from './containerDirectories'
import { validContainerUploadLocation } from './containerUploads'
import { validDirectoryId } from './containerDirectories'

// Navigation/registration hints only. No OIDC token, credentials or automatic execution.
const key = 'whaledeck-container-directory-hints-v1'
export interface DirectoryHint {
  subject: string
  command: DirectoryCommand
  registration: DirectoryRegistration
  id: string | null
}
export function clearDirectoryHints() {
  try {
    sessionStorage.removeItem(key)
  } catch {
    /* Storage is optional. */
  }
}
export function reconcileDirectoryHints(subject: string | undefined, administrator: boolean) {
  if (!administrator || !subject || read().some((value) => value.subject !== subject))
    clearDirectoryHints()
}
function valid(value: DirectoryHint) {
  return (
    value &&
    typeof value.subject === 'string' &&
    value.subject.length <= 256 &&
    value.command &&
    validContainerUploadLocation(value.command.location) &&
    /^0[0-7]{3}$/.test(value.command.mode) &&
    value.registration?.confirmed === true &&
    typeof value.registration.planHash === 'string' &&
    value.registration.planHash.length > 0 &&
    value.registration.planHash.length <= 65536 &&
    validDirectoryId(value.registration.idempotencyKey) &&
    (value.id === null || validDirectoryId(value.id))
  )
}
function read(): DirectoryHint[] {
  try {
    const raw = sessionStorage.getItem(key)
    if (!raw) return []
    if (raw.length > 270000) throw new Error('hint limit')
    const values = JSON.parse(raw)
    if (!Array.isArray(values) || values.length > 4 || !values.every(valid))
      throw new Error('invalid hints')
    return values
  } catch {
    clearDirectoryHints()
    return []
  }
}
export function directoryHint(subject: string, containerId: string) {
  const values = read()
  if (values.some((v) => v.subject !== subject)) {
    clearDirectoryHints()
    return null
  }
  return values.find((v) => v.command.location.containerId === containerId) ?? null
}
export function saveDirectoryHint(value: DirectoryHint) {
  if (!valid(value)) throw new Error('目录任务提示无效')
  const values = read().filter(
    (v) =>
      v.subject === value.subject &&
      v.command.location.containerId !== value.command.location.containerId,
  )
  try {
    sessionStorage.setItem(key, JSON.stringify([...values.slice(-3), value]))
  } catch {
    /* Existing task remains queryable on server. */
  }
}
export function removeDirectoryHint(subject: string, containerId: string) {
  try {
    sessionStorage.setItem(
      key,
      JSON.stringify(
        read().filter(
          (v) => v.subject === subject && v.command.location.containerId !== containerId,
        ),
      ),
    )
  } catch {
    /* Optional hint. */
  }
}
