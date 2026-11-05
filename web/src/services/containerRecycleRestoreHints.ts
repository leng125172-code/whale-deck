import { validDirectoryId } from './containerDirectories'

const storageKey = 'whaledeck-container-recycle-restore-hints-v1'
export interface ContainerRecycleRestoreHint {
  subject: string
  containerId: string
  key: string
  sourceIntentId: string
  id: string | null
}
function valid(value: ContainerRecycleRestoreHint) {
  return (
    !!value &&
    Object.keys(value).length === 5 &&
    typeof value.subject === 'string' &&
    !!value.subject &&
    value.subject.length <= 256 &&
    typeof value.containerId === 'string' &&
    /^[a-f0-9]{64}$/.test(value.containerId) &&
    validDirectoryId(value.key) &&
    validDirectoryId(value.sourceIntentId) &&
    (value.id === null || validDirectoryId(value.id))
  )
}
export function clearRecycleRestoreHints() {
  try {
    sessionStorage.removeItem(storageKey)
  } catch {
    /* Optional navigation hint. */
  }
}
function read(): ContainerRecycleRestoreHint[] {
  try {
    const raw = sessionStorage.getItem(storageKey)
    if (!raw) return []
    if (raw.length > 8192) throw new Error('Recycle hint budget')
    const values = JSON.parse(raw)
    if (
      !Array.isArray(values) ||
      values.length > 4 ||
      !values.every(valid) ||
      new Set(values.map((v) => v.containerId)).size !== values.length
    )
      throw new Error('Invalid recycle hints')
    return values
  } catch {
    clearRecycleRestoreHints()
    return []
  }
}
export function reconcileRecycleRestoreHints(subject: string | undefined, admin: boolean) {
  if (!subject || !admin || read().some((value) => value.subject !== subject))
    clearRecycleRestoreHints()
}
export function recycleRestoreHint(subject: string, containerId: string) {
  const values = read()
  if (values.some((value) => value.subject !== subject)) {
    clearRecycleRestoreHints()
    return null
  }
  return values.find((value) => value.containerId === containerId) ?? null
}
export function saveRecycleRestoreHint(value: ContainerRecycleRestoreHint) {
  if (!valid(value)) throw new Error('文件恢复任务提示无效')
  const values = read().filter(
    (v) => v.subject === value.subject && v.containerId !== value.containerId,
  )
  if (values.length >= 4) throw new Error('已有4个未核实的文件恢复引用，请先查询原任务')
  try {
    sessionStorage.setItem(storageKey, JSON.stringify([...values, value]))
  } catch {
    throw new Error('浏览器无法保存原任务引用，请检查会话存储设置')
  }
}
export function removeRecycleRestoreHint(subject: string, containerId: string) {
  try {
    sessionStorage.setItem(
      storageKey,
      JSON.stringify(read().filter((v) => v.subject === subject && v.containerId !== containerId)),
    )
  } catch {
    /* Optional hint. */
  }
}
