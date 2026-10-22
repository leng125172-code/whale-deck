// UI invalidation only. No token, identity, role, or authority is stored here.
export const sessionEndedStorageKey = 'whaledeck-session-ended'
const listeners = new Set<() => void>()

export function onSessionExpired(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function notifySessionExpired() {
  for (const listener of listeners) listener()
}

export function validSessionEndedSignal(value: string | null) {
  if (!value || value.length > 160) return false
  try {
    const signal = JSON.parse(value)
    return (
      signal !== null &&
      typeof signal === 'object' &&
      Object.keys(signal).length === 3 &&
      signal.version === 1 &&
      signal.kind === 'signed-out' &&
      typeof signal.id === 'string' &&
      /^[a-f0-9]{32}$/.test(signal.id)
    )
  } catch {
    return false
  }
}

export function publishSessionEnded() {
  try {
    const random = crypto.getRandomValues(new Uint8Array(16))
    localStorage.setItem(
      sessionEndedStorageKey,
      JSON.stringify({
        version: 1,
        kind: 'signed-out',
        id: Array.from(random, (byte) => byte.toString(16).padStart(2, '0')).join(''),
      }),
    )
  } catch {
    // Storage can be denied. A subsequent real BFF 401 still invalidates UI.
  }
}
