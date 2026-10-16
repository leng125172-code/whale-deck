import { ApiError, apiRequest, apiUrl, createIdempotencyKey } from '@/services/apiClient'

export type TerminalPhase = 'idle' | 'preparing' | 'connecting' | 'open' | 'closed' | 'error'
export interface TerminalGrant {
  sessionId: string
  connectToken: string
  grantExpiresAtUtc: string
  expiresAtUtc: string
  targetId: string
  targetKind?: 'Host' | 'Container'
  instanceId?: string | null
  shell?: string | null
}
export interface ContainerTerminalTarget {
  containerId: string
  name: string
  instanceId: string
  imageId: string
  configuredUser: string
}
export type ContainerShell = 'sh' | 'bash' | 'ash'
export interface TerminalSink {
  state: (phase: TerminalPhase, message: string, exitCode?: number) => void
  write: (bytes: Uint8Array, consumed: () => void) => void
  clear: () => void
  ready: () => void
  notice: (message: string) => void
  permissionLost: () => void
}
type Dependencies = {
  request: typeof apiRequest
  socket: (url: string, protocol: string) => WebSocket
  location: string
  key: () => string
}
const identifier = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/

export function terminalSocketUrl(id: string, location: string) {
  if (!identifier.test(id)) throw new Error('终端会话标识无效。')
  const origin = new URL(location)
  const target = new URL(apiUrl(`terminals/${id}/connect`), origin)
  if (
    target.origin !== origin.origin ||
    target.search ||
    target.hash ||
    target.username ||
    target.password
  )
    throw new Error('终端只支持同源访问，请使用工作站面板入口。')
  if (!['http:', 'https:'].includes(target.protocol)) throw new Error('终端访问协议无效。')
  target.protocol = target.protocol === 'https:' ? 'wss:' : 'ws:'
  return target.toString()
}

export function createHostTerminal(sink: TerminalSink, provided: Partial<Dependencies> = {}) {
  return createTerminal(sink, provided)
}
export function createContainerTerminal(
  sink: TerminalSink,
  selection: () => { target: ContainerTerminalTarget; shell: ContainerShell },
  provided: Partial<Dependencies> = {},
) {
  return createTerminal(sink, provided, selection)
}
function createTerminal(
  sink: TerminalSink,
  provided: Partial<Dependencies>,
  selection?: () => { target: ContainerTerminalTarget; shell: ContainerShell },
) {
  const dependencies: Dependencies = {
    request: apiRequest,
    socket: (url, protocol) => new WebSocket(url, protocol),
    location: window.location.href,
    key: createIdempotencyKey,
    ...provided,
  }
  let phase: TerminalPhase = 'idle'
  let generation = 0
  let disposed = false
  let socket: WebSocket | null = null
  let sessionId: string | null = null
  let pendingKey: string | null = null
  let pendingScope = ''
  let controller: AbortController | null = null
  let readyTimer: number | undefined
  let pollTimer: number | undefined
  let pendingBytes = 0
  let dimensions = { columns: 80, rows: 24 }
  let sentDimensions = ''
  let exitCode: number | undefined

  function state(next: TerminalPhase, message: string) {
    phase = next
    if (!disposed) sink.state(next, message, exitCode)
  }
  function closeRecord(id: string) {
    if (identifier.test(id))
      void dependencies.request(`terminals/${id}/close`, { method: 'POST' }).catch(() => {
        // No retry of a write or false claim of server cleanup; the BFF/Agent
        // independently closes disconnected streams and expires leases.
      })
  }
  function release(close = false) {
    window.clearTimeout(readyTimer)
    window.clearTimeout(pollTimer)
    controller?.abort()
    controller = null
    const previous = socket
    socket = null
    if (previous) {
      previous.onopen = previous.onmessage = previous.onerror = previous.onclose = null
      try {
        previous.close(1000, 'CLIENT_DISCONNECT')
      } catch {
        /* Already closed. */
      }
    }
    if (close && sessionId) closeRecord(sessionId)
    sessionId = null
    pendingBytes = 0
  }
  function fail(message: string, forbidden = false) {
    generation++
    release(true)
    sink.clear()
    state('error', message)
    if (forbidden && !disposed) sink.permissionLost()
  }
  function send(frame: object) {
    if (!socket || socket.readyState !== WebSocket.OPEN) return false
    try {
      socket.send(JSON.stringify(frame))
      return true
    } catch {
      fail('连接已中断，请手动重新连接。')
      return false
    }
  }
  function resize(columns: number, rows: number) {
    if (!Number.isFinite(columns) || !Number.isFinite(rows)) return
    dimensions = {
      columns: Math.max(2, Math.min(400, Math.floor(columns))),
      rows: Math.max(2, Math.min(400, Math.floor(rows))),
    }
    const signature = `${dimensions.columns}:${dimensions.rows}`
    if (phase === 'open' && signature !== sentDimensions && send({ kind: 'resize', ...dimensions }))
      sentDimensions = signature
  }
  async function poll(current: number) {
    if (current !== generation || !sessionId || disposed) return
    try {
      const status = await dependencies.request<{ state: string }>(`terminals/${sessionId}`, {
        signal: controller?.signal,
      })
      if (current !== generation || disposed) return
      if (['Closed', 'Expired'].includes(status.state)) {
        fail('会话已结束，请手动建立新会话。')
        return
      }
    } catch (reason) {
      if (current !== generation || disposed) return
      if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
        fail('登录或管理权限已失效。', true)
        return
      }
      fail('无法确认会话状态，连接已关闭，请稍后重新连接。')
      return
    }
    pollTimer = window.setTimeout(() => void poll(current), 15_000)
  }

  async function connect() {
    if (disposed || ['preparing', 'connecting', 'open'].includes(phase)) return
    const current = ++generation
    release()
    exitCode = undefined
    sink.clear()
    state('preparing', '正在申请一次性连接许可…')
    controller = new AbortController()
    try {
      const selected = selection?.()
      if (
        selected &&
        (!/^[a-f0-9]{64}$/.test(selected.target.containerId) ||
          !selected.target.instanceId ||
          selected.target.instanceId.length > 80 ||
          !['sh', 'bash', 'ash'].includes(selected.shell))
      )
        throw new Error('容器目标无效。')
      const scope = JSON.stringify(
        selected
          ? {
              containerId: selected.target.containerId,
              instanceId: selected.target.instanceId,
              shell: selected.shell,
            }
          : null,
      )
      if (pendingScope !== scope) {
        pendingKey = null
        pendingScope = scope
      }
      pendingKey ??= dependencies.key()
      terminalSocketUrl('00000000-0000-4000-8000-000000000001', dependencies.location)
      const grant = await dependencies.request<TerminalGrant>(
        selected ? 'terminals/containers/grants' : 'terminals/host/grants',
        {
          method: 'POST',
          headers: { 'Idempotency-Key': pendingKey },
          body: JSON.stringify({
            confirmed: true,
            idempotencyKey: pendingKey,
            ...(selected
              ? {
                  containerId: selected.target.containerId,
                  instanceId: selected.target.instanceId,
                  shell: selected.shell,
                }
              : {}),
          }),
          signal: controller.signal,
        },
      )
      if (
        !identifier.test(grant.sessionId) ||
        !/^[A-F0-9]{64}$/.test(grant.connectToken) ||
        (selected
          ? grant.targetKind !== 'Container' ||
            grant.targetId !== selected.target.containerId ||
            grant.instanceId !== selected.target.instanceId ||
            grant.shell !== selected.shell
          : grant.targetId !== 'whaledeck.host' ||
            (grant.targetKind ?? 'Host') !== 'Host' ||
            grant.instanceId != null ||
            grant.shell != null)
      )
        throw new Error('终端服务返回了无效的连接许可。')
      if (current !== generation || disposed) {
        closeRecord(grant.sessionId)
        return
      }
      sessionId = grant.sessionId
      pendingKey = null
      let token: string | null = grant.connectToken
      const active = dependencies.socket(
        terminalSocketUrl(sessionId, dependencies.location),
        'whaledeck.terminal.v1',
      )
      socket = active
      state('connecting', selected ? '正在连接容器终端…' : '正在连接受限宿主终端…')
      readyTimer = window.setTimeout(() => {
        if (current === generation) fail('连接超时，请手动重新连接。')
      }, 15_000)
      active.onopen = () => {
        if (current !== generation || disposed) return
        if (token) send({ token })
        token = null
      }
      active.onmessage = (event) => {
        if (current !== generation || disposed) return
        try {
          if (typeof event.data !== 'string' || event.data.length > 8192) throw new Error()
          const frame = JSON.parse(event.data) as Record<string, unknown>
          if (
            !frame ||
            typeof frame !== 'object' ||
            Array.isArray(frame) ||
            Object.keys(frame).some((key) => !['kind', 'data', 'code', 'exitCode'].includes(key))
          )
            throw new Error()
          switch (frame.kind) {
            case 'ready':
              if (phase !== 'connecting') throw new Error()
              window.clearTimeout(readyTimer)
              state('open', selected ? '已连接 · 容器配置身份' : '已连接 · 受限宿主身份')
              sentDimensions = ''
              resize(dimensions.columns, dimensions.rows)
              sink.ready()
              pollTimer = window.setTimeout(() => void poll(current), 15_000)
              break
            case 'heartbeat':
              if (phase !== 'open') throw new Error()
              break
            case 'output': {
              if (phase !== 'open' || typeof frame.data !== 'string' || frame.data.length > 5464)
                throw new Error()
              const binary = atob(frame.data)
              if (binary.length > 4096 || pendingBytes + binary.length > 1024 * 1024)
                throw new Error()
              const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0))
              pendingBytes += bytes.length
              sink.write(bytes, () => {
                if (current === generation) pendingBytes = Math.max(0, pendingBytes - bytes.length)
              })
              break
            }
            case 'exit':
              if (
                phase !== 'open' ||
                !Number.isInteger(frame.exitCode) ||
                typeof frame.exitCode !== 'number' ||
                frame.exitCode < -2147483648 ||
                frame.exitCode > 2147483647
              )
                throw new Error()
              exitCode = frame.exitCode
              window.clearTimeout(pollTimer)
              state('closed', `进程已结束 · 退出码 ${exitCode}`)
              break
            case 'error':
              fail(
                selected
                  ? '容器终端未能完成操作，请检查容器状态和所选 Shell。'
                  : '宿主终端未能完成操作，请检查服务状态后重新连接。',
              )
              break
            default:
              throw new Error()
          }
        } catch {
          fail('终端数据无效或显示缓冲已满，连接已安全断开。')
        }
      }
      active.onerror = () => {
        if (current === generation) fail('终端连接失败，请检查登录和工作站服务。')
      }
      active.onclose = (event) => {
        if (current !== generation || disposed) return
        token = null
        generation++
        release()
        if (event.reason === 'TERMINAL_FORBIDDEN') {
          sink.clear()
          state('error', '会话已关闭或管理权限发生变化。')
          sink.permissionLost()
        } else if (exitCode !== undefined) state('closed', `进程已结束 · 退出码 ${exitCode}`)
        else if (event.code === 1000) state('closed', '连接已关闭。')
        else {
          sink.clear()
          state('error', '会话已中断或权限发生变化，请手动重新连接。')
        }
      }
    } catch (reason) {
      if (current !== generation || disposed) return
      if (reason instanceof ApiError && reason.status === 409) pendingKey = null
      fail(
        reason instanceof ApiError && reason.status === 429
          ? reason.message
          : '无法建立终端会话，请稍后重试。',
        reason instanceof ApiError && [401, 403].includes(reason.status),
      )
      const trace = reason instanceof ApiError ? reason.problem.traceId : undefined
      if (typeof trace === 'string' && /^[A-Za-z0-9._:-]{1,128}$/.test(trace))
        sink.notice(`请求追踪号：${trace}`)
    }
  }

  function input(value: string, binary = false) {
    if (disposed || phase !== 'open' || !socket || socket.readyState !== WebSocket.OPEN)
      return false
    if (
      value.length > 16384 ||
      (binary && [...value].some((character) => character.charCodeAt(0) > 255))
    ) {
      sink.notice('单次输入过大，请分段粘贴（最多 16 KiB）。')
      return false
    }
    const bytes = binary
      ? Uint8Array.from(value, (character) => character.charCodeAt(0))
      : new TextEncoder().encode(value)
    if (!bytes.length) return true
    if (bytes.length > 16384) {
      sink.notice('单次输入过大，请分段粘贴（最多 16 KiB）。')
      return false
    }
    if (socket.bufferedAmount + Math.ceil((bytes.length * 4) / 3) + 1024 > 65536) {
      sink.notice('输入缓冲较满，请稍后分段输入。')
      return false
    }
    for (let offset = 0; offset < bytes.length; offset += 4096) {
      const data = btoa(String.fromCharCode(...bytes.subarray(offset, offset + 4096)))
      if (!send({ kind: 'input', data })) return false
    }
    return true
  }
  function disconnect() {
    generation++
    release(true)
    pendingKey = null
    state('closed', '连接已断开。')
  }
  function dispose() {
    disposed = true
    generation++
    release(true)
    pendingKey = null
    sink.clear()
  }
  return { connect, disconnect, dispose, input, resize }
}
