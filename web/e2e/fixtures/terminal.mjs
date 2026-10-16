import { randomUUID } from 'node:crypto'
import { Buffer } from 'node:buffer'
import { StringDecoder } from 'node:string_decoder'

// UI-only fixtures. Never claim these simulated commands prove host execution.
export async function installTerminalFixture(page, options = {}) {
  const state = {
    writes: [],
    frames: [],
    socketUrls: [],
    sockets: 0,
    closed: false,
    grantStatus: options.grantStatus || 200,
    administrator: true,
  }
  let active
  let currentId
  await page.addInitScript(() => sessionStorage.setItem('whaledeck-preview-authenticated', 'true'))
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname.replace('/api/v1/', '')
    const fulfill = (json, status = 200) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(json) })
    if (request.method() !== 'GET')
      state.writes.push({ path, method: request.method(), body: request.postDataJSON() })
    if (path === 'auth/csrf')
      return fulfill({ headerName: 'X-XSRF-TOKEN', token: 'ui-fixture-only' })
    if (path === 'auth/me')
      return fulfill({
        subject: 'preview-user',
        name: 'Preview User',
        groups: [],
        isAdministrator: state.administrator,
        permissionExpiresAtUtc: new Date(Date.now() + 7200000).toISOString(),
      })
    if (path === 'terminals/host/grants') {
      if (state.grantStatus !== 200)
        return fulfill(
          { title: 'Fixture unavailable', traceId: 'fixture-trace', status: state.grantStatus },
          state.grantStatus,
        )
      currentId = randomUUID()
      state.closed = false
      return fulfill({
        sessionId: currentId,
        connectToken: 'A'.repeat(64),
        targetId: 'whaledeck.host',
        grantExpiresAtUtc: new Date(Date.now() + 30000).toISOString(),
        expiresAtUtc: new Date(Date.now() + 600000).toISOString(),
      })
    }
    if (path.startsWith('terminals/') && path.endsWith('/close')) {
      state.closed = true
      active?.close({ code: 1000, reason: 'TERMINAL_CLOSED' })
      return route.fulfill({ status: 204 })
    }
    if (path.startsWith('terminals/'))
      return fulfill({ id: currentId, state: state.closed ? 'Closed' : 'Active' })
    if (path.startsWith('notifications'))
      return fulfill({ items: [], unreadCount: 0, nextCursor: null })
    if (path === 'overview') return fulfill({ title: 'Fixture overview not in scope' }, 503)
    return fulfill([])
  })
  function output(text) {
    active?.send(
      JSON.stringify({
        kind: 'output',
        data: Buffer.from(text).toString('base64'),
        code: '',
        exitCode: 0,
      }),
    )
  }
  await page.routeWebSocket('**/api/v1/terminals/*/connect', (socket) => {
    active = socket
    state.sockets++
    state.socketUrls.push(socket.url())
    let command = ''
    const decoder = new StringDecoder('utf8')
    socket.onClose(() => {
      state.closed = true
    })
    socket.onMessage((value) => {
      const frame = JSON.parse(String(value))
      state.frames.push(frame)
      if (frame.token) {
        if (frame.token !== 'A'.repeat(64)) throw new Error('Fixture token mismatch')
        if (options.noReady) return
        socket.send(JSON.stringify({ kind: 'ready', data: '', code: '', exitCode: 0 }))
        output('Whale Deck · UI fixture only\r\n$ ')
      } else if (frame.kind === 'input') {
        const text = decoder.write(Buffer.from(frame.data, 'base64'))
        for (const character of text) {
          if (character === '\r') {
            output('\r\n')
            if (command === 'exit 7') {
              socket.send(
                JSON.stringify({ kind: 'exit', data: '', code: 'TERMINAL_CLOSED', exitCode: 7 }),
              )
              state.closed = true
              socket.close({ code: 1000, reason: 'TERMINAL_CLOSED' })
              return
            }
            output('收到输入: ' + command + '\r\n$ ')
            command = ''
          } else {
            command += character
            output(character)
          }
        }
      }
    })
  })
  return {
    state,
    output,
    revoke() {
      state.administrator = false
      active?.close({ code: 1008, reason: 'TERMINAL_FORBIDDEN' })
    },
    interrupt() {
      active?.close({ code: 1011, reason: 'FIXTURE_INTERRUPTION' })
    },
  }
}
