import type { FileRoot } from './fileManagement'

// Navigation hint only, never an authorization grant or a mutation request.
export const fileBrowserLocationKey = 'whaledeck-files-location-v1'
export interface FileBrowserLocation {
  rootId: string
  path: string
}
function validLocation(value: FileBrowserLocation) {
  const bytes = (text: string) => new TextEncoder().encode(text).length
  return (
    typeof value.rootId === 'string' &&
    /^[a-zA-Z0-9._-]{1,128}$/.test(value.rootId) &&
    typeof value.path === 'string' &&
    bytes(value.path) <= 4096 &&
    !value.path.includes('\\') &&
    ![...value.path].some((character) => {
      const code = character.codePointAt(0)!
      return code < 32 || (code >= 127 && code < 160) || (code >= 0xd800 && code <= 0xdfff)
    }) &&
    (value.path === '' ||
      value.path.split('/').every((part) => !['', '.', '..'].includes(part) && bytes(part) <= 255))
  )
}
export function clearFileBrowserLocation() {
  try {
    sessionStorage.removeItem(fileBrowserLocationKey)
  } catch {
    // Disabled/full browser storage must not prevent file management or logout.
  }
}
export function reconcileFileBrowserLocation(subject: string | undefined, administrator: boolean) {
  try {
    const raw = sessionStorage.getItem(fileBrowserLocationKey)
    if (!raw) return null
    if (raw.length > 24576) throw new Error('Invalid navigation hint')
    const value = JSON.parse(raw)
    if (
      !subject ||
      !administrator ||
      !value ||
      value.schema !== 1 ||
      value.subject !== subject ||
      !validLocation(value)
    ) {
      clearFileBrowserLocation()
      return null
    }
    return { rootId: value.rootId, path: value.path } as FileBrowserLocation
  } catch {
    clearFileBrowserLocation()
    return null
  }
}
export function restoreFileBrowserLocation(subject: string, roots: FileRoot[]) {
  const value = reconcileFileBrowserLocation(subject, true)
  if (!value) return null
  if (!roots.some((root) => root.id === value.rootId && root.actions.includes('files-list'))) {
    clearFileBrowserLocation()
    return null
  }
  return value
}
export function rememberFileBrowserLocation(
  subject: string | undefined,
  location: FileBrowserLocation,
) {
  if (!subject || !validLocation(location)) return
  try {
    sessionStorage.setItem(
      fileBrowserLocationKey,
      JSON.stringify({ schema: 1, subject, rootId: location.rootId, path: location.path }),
    )
  } catch {
    // Persistence is optional; the live authorized listing remains usable.
  }
}
