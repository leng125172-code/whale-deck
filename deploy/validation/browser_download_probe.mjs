// Read-only QA observer. Never retains URLs, query strings, cookies or bodies.
// A snapshot/timeout/dispose is NOT a transport terminal event.
export function observeDownload(session, expectedPath) {
  if (!/^\/api\/v1\/(?:containers\/[a-f0-9]{64}\/)?files\/download$/.test(expectedPath))
    throw new Error('invalid-download-observer-path')
  let requestId
  let disposed = false
  const state = {
    seen: false,
    responseStatus: null,
    declaredBytes: null,
    receivedBodyBytes: 0,
    encodedBytes: null,
    terminal: null,
    networkError: null,
    canceled: false,
    invalid: false,
  }
  const own = (event) => !disposed && requestId !== undefined && event.requestId === requestId
  const handlers = {
    'Network.requestWillBeSent': (event) => {
      if (disposed || requestId !== undefined) return
      try {
        if (new URL(event.request.url).pathname !== expectedPath) return
      } catch { return }
      requestId = event.requestId
      state.seen = true
    },
    'Network.responseReceived': (event) => {
      if (!own(event) || state.terminal) return
      state.responseStatus = event.response.status
      const value = Object.entries(event.response.headers)
        .find(([key]) => key.toLowerCase() === 'content-length')?.[1]
      if (!/^(?:0|[1-9][0-9]{0,9})$/.test(String(value))) {
        state.invalid = true
        return
      }
      const length = Number(value)
      if (length > 1024 ** 3) { state.invalid = true; return }
      state.declaredBytes = length
    },
    'Network.dataReceived': (event) => {
      if (!own(event) || state.terminal) return
      if (!Number.isSafeInteger(event.dataLength) || event.dataLength < 0 ||
          state.receivedBodyBytes + event.dataLength > 1024 ** 3) {
        state.invalid = true
        return
      }
      state.receivedBodyBytes += event.dataLength
    },
    'Network.loadingFailed': (event) => {
      if (!own(event) || state.terminal) return
      state.terminal = 'failed'
      // Chromium network error names only; never retain arbitrary tool text.
      state.networkError = /^net::ERR_[A-Z_]{1,64}$/.test(event.errorText)
        ? event.errorText : 'unclassified-network-error'
      state.canceled = event.canceled === true
    },
    'Network.loadingFinished': (event) => {
      if (!own(event) || state.terminal) return
      state.terminal = 'finished'
      if (Number.isSafeInteger(event.encodedDataLength) && event.encodedDataLength >= 0)
        state.encodedBytes = event.encodedDataLength
      else state.invalid = true
    },
  }
  for (const [name, handler] of Object.entries(handlers)) session.on(name, handler)
  return {
    snapshot: () => ({ ...state }),
    dispose() {
      disposed = true
      for (const [name, handler] of Object.entries(handlers)) session.off(name, handler)
    },
  }
}
