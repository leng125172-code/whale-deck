import { validDirectoryId } from './containerDirectories'

const key = 'whaledeck-container-recycle-archive-retirement-hints-v1'
export interface RetirementHint {
  subject: string
  containerId: string
  archiveIntentId: string
  retainDays: number
  key: string
  id: string | null
}
function valid(row: RetirementHint) {
  return (
    !!row &&
    Object.keys(row).length === 6 &&
    typeof row.subject === 'string' &&
    row.subject.length > 0 &&
    row.subject.length <= 256 &&
    /^[a-f0-9]{64}$/.test(row.containerId) &&
    validDirectoryId(row.archiveIntentId) &&
    validDirectoryId(row.key) &&
    (row.id === null || validDirectoryId(row.id)) &&
    Number.isInteger(row.retainDays) &&
    row.retainDays >= 1 &&
    row.retainDays <= 3650
  )
}
export function clearRetirementHints() {
  try {
    sessionStorage.removeItem(key)
  } catch {
    /* Optional navigation hint. */
  }
}
function read(): RetirementHint[] {
  try {
    const raw = sessionStorage.getItem(key)
    if (!raw) return []
    if (raw.length > 8192) throw new Error('Retirement reference budget')
    const values = JSON.parse(raw)
    if (
      !Array.isArray(values) ||
      values.length > 4 ||
      !values.every(valid) ||
      new Set(values.map((r) => r.containerId)).size !== values.length
    )
      throw new Error('Invalid retirement references')
    return values
  } catch {
    clearRetirementHints()
    return []
  }
}
export function reconcileRetirementHints(subject: string | undefined, admin: boolean) {
  if (!subject || !admin || read().some((r) => r.subject !== subject)) clearRetirementHints()
}
export function retirementHint(subject: string, containerId: string) {
  reconcileRetirementHints(subject, true)
  return read().find((r) => r.containerId === containerId) ?? null
}
export function saveRetirementHint(row: RetirementHint) {
  if (!valid(row)) throw new Error('退役任务引用无效')
  const previous = read().filter(
    (r) => r.subject === row.subject && r.containerId !== row.containerId,
  )
  if (previous.length >= 4) throw new Error('已有四个待核实的退役引用')
  sessionStorage.setItem(key, JSON.stringify([...previous, row]))
}
export function removeRetirementHint(subject: string, containerId: string) {
  try {
    sessionStorage.setItem(
      key,
      JSON.stringify(read().filter((r) => r.subject !== subject || r.containerId !== containerId)),
    )
  } catch {
    /* Optional navigation hint. */
  }
}
