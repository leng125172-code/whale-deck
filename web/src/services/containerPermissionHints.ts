import { validDirectoryId } from './containerDirectories'

const storageKey = 'whaledeck-container-permission-hints-v1'
export interface ContainerPermissionHint {
  subject: string
  containerId: string
  key: string
  id: string | null
}
function valid(value: ContainerPermissionHint) {
  return (
    !!value &&
    Object.keys(value).length === 4 &&
    typeof value.subject === 'string' &&
    !!value.subject &&
    value.subject.length <= 256 &&
    typeof value.containerId === 'string' &&
    /^[a-f0-9]{64}$/.test(value.containerId) &&
    validDirectoryId(value.key) &&
    (value.id === null || validDirectoryId(value.id))
  )
}
export function clearPermissionHints() {
  try {
    sessionStorage.removeItem(storageKey)
  } catch {
    /* Optional navigation hint. */
  }
}
function read(): ContainerPermissionHint[] {
  try {
    const raw = sessionStorage.getItem(storageKey)
    if (!raw) return []
    if (raw.length > 8192) throw new Error('Permission hint budget')
    const values = JSON.parse(raw)
    if (
      !Array.isArray(values) ||
      values.length > 4 ||
      !values.every(valid) ||
      new Set(values.map((v) => v.containerId)).size !== values.length
    )
      throw new Error('Invalid permission hints')
    return values
  } catch {
    clearPermissionHints()
    return []
  }
}
export function reconcilePermissionHints(subject: string | undefined, admin: boolean) {
  if (!subject || !admin || read().some((value) => value.subject !== subject))
    clearPermissionHints()
}
export function permissionHint(subject: string, containerId: string) {
  const values = read()
  if (values.some((value) => value.subject !== subject)) {
    clearPermissionHints()
    return null
  }
  return values.find((value) => value.containerId === containerId) ?? null
}
export function savePermissionHint(value: ContainerPermissionHint) {
  if (!valid(value)) throw new Error('权限修改任务提示无效')
  const values = read().filter(
    (v) => v.subject === value.subject && v.containerId !== value.containerId,
  )
  if (values.length >= 4) throw new Error('已有4个未核实的权限修改引用，请先查询原任务')
  try {
    sessionStorage.setItem(storageKey, JSON.stringify([...values, value]))
  } catch {
    throw new Error('浏览器无法保存原任务引用，请检查会话存储设置')
  }
}
export function removePermissionHint(subject: string, containerId: string) {
  try {
    sessionStorage.setItem(
      storageKey,
      JSON.stringify(read().filter((v) => v.subject === subject && v.containerId !== containerId)),
    )
  } catch {
    /* Optional hint. */
  }
}
