import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, type apiRequest } from '@/services/apiClient'
import {
  createHostTerminal,
  createContainerTerminal,
  terminalSocketUrl,
  type TerminalSink,
  type TerminalGrant,
} from '@/services/hostTerminal'

const grant = {
  sessionId: '4871e73a-84ac-4267-a3f1-a243221ddf16',
  connectToken: 'A'.repeat(64),
  targetId: 'whaledeck.host',
  grantExpiresAtUtc: '2026-10-16T00:00:30Z',
  expiresAtUtc: '2026-10-16T00:10:00Z',
}
class FakeSocket {
  readyState: number = WebSocket.CONNECTING
  bufferedAmount = 0
  sent: Record<string, unknown>[] = []
  onopen: ((event: Event) => void) | null = null
  onmessage: ((event: MessageEvent) => void) | null = null
  onclose: ((event: CloseEvent) => void) | null = null
  onerror: ((event: Event) => void) | null = null
  send(data: string) {
    this.sent.push(JSON.parse(data) as Record<string, unknown>)
  }
  close() {
    this.readyState = WebSocket.CLOSED
  }
  open() {
    this.readyState = WebSocket.OPEN
    this.onopen?.(new Event('open'))
  }
  row(value: object) {
    this.onmessage?.(new MessageEvent('message', { data: JSON.stringify(value) }))
  }
  closed(code = 1000, reason = '') {
    this.readyState = WebSocket.CLOSED
    this.onclose?.(new CloseEvent('close', { code, reason }))
  }
}
const clients: ReturnType<typeof createHostTerminal>[] = []
function containerSetup(overrides: Partial<TerminalGrant> = {}) {
  const base = setup()
  const target = {
    containerId: 'a'.repeat(64),
    instanceId: '10:2026-10-16T00:00:00Z',
    name: 'ui-only',
    imageId: 'image',
    configuredUser: '1000',
  }
  base.request.mockImplementation(async (path) =>
    path.endsWith('containers/grants')
      ? {
          ...grant,
          targetKind: 'Container',
          targetId: target.containerId,
          instanceId: target.instanceId,
          shell: 'sh',
          ...overrides,
        }
      : { state: 'Active' },
  )
  const client = createContainerTerminal(base.sink, () => ({ target, shell: 'sh' }), {
    request: base.request as typeof apiRequest,
    socket: base.factory,
    location: 'http://127.0.0.1:5174/containers/terminal',
    key: () => '1a122a65-5b5b-4203-8985-f6c0198cd046',
  })
  clients.push(client)
  return { ...base, client, target }
}
function setup(key = () => '1a122a65-5b5b-4203-8985-f6c0198cd046') {
  const socket = new FakeSocket()
  const request = vi.fn<(path: string, init?: RequestInit) => Promise<unknown>>(async (path) =>
    path.endsWith('host/grants')
      ? { ...grant }
      : path.endsWith('/close')
        ? undefined
        : { state: 'Active' },
  )
  const factory = vi.fn<(url: string, protocol: string) => WebSocket>(
    () => socket as unknown as WebSocket,
  )
  const sink: TerminalSink = {
    state: vi.fn<TerminalSink['state']>(),
    write: vi.fn<TerminalSink['write']>((_bytes, consumed) => consumed()),
    clear: vi.fn<TerminalSink['clear']>(),
    ready: vi.fn<TerminalSink['ready']>(),
    notice: vi.fn<TerminalSink['notice']>(),
    permissionLost: vi.fn<TerminalSink['permissionLost']>(),
  }
  const client = createHostTerminal(sink, {
    request: request as typeof apiRequest,
    socket: factory,
    location: 'http://127.0.0.1:5174/host/terminal',
    key,
  })
  clients.push(client)
  async function open() {
    await client.connect()
    socket.open()
    socket.row({ kind: 'ready' })
  }
  return { client, socket, sink, request, factory, open }
}
beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  clients.splice(0).forEach((client) => client.dispose())
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('host terminal lifecycle', () => {
  it('container grant includes exact target and startup instance but WS accepts only the one-time token', async () => {
    const { client, target, request, socket, factory } = containerSetup()
    await client.connect()
    const issue = request.mock.calls.find(([path]) => path === 'terminals/containers/grants')
    expect(JSON.parse(issue?.[1]?.body as string)).toEqual({
      confirmed: true,
      idempotencyKey: '1a122a65-5b5b-4203-8985-f6c0198cd046',
      containerId: target.containerId,
      instanceId: target.instanceId,
      shell: 'sh',
    })
    socket.open()
    expect(socket.sent).toEqual([{ token: grant.connectToken }])
    expect(JSON.stringify(factory.mock.calls)).not.toContain(target.containerId)
    socket.row({ kind: 'ready' })
    expect(client.input('中文')).toBe(true)
    client.disconnect()
    expect(socket.readyState).toBe(WebSocket.CLOSED)
  })
  it.each([
    { targetId: 'b'.repeat(64) },
    { targetKind: 'Host' as const },
    { instanceId: 'restarted' },
    { shell: 'bash' },
  ])('rejects a container grant for a different selection: %j', async (overrides) => {
    const { client, factory, sink } = containerSetup(overrides)
    await client.connect()
    expect(factory).not.toHaveBeenCalled()
    expect(sink.state).toHaveBeenLastCalledWith('error', expect.any(String), undefined)
  })
  it('leaves preparation state when secure request-key generation fails and allows manual retry', async () => {
    const key = vi
      .fn<() => string>()
      .mockImplementationOnce(() => {
        throw new Error('Random generator unavailable')
      })
      .mockReturnValue('1a122a65-5b5b-4203-8985-f6c0198cd046')
    const { client, sink, request } = setup(key)
    await client.connect()
    expect(sink.state).toHaveBeenLastCalledWith(
      'error',
      '无法建立终端会话，请稍后重试。',
      undefined,
    )
    expect(request).not.toHaveBeenCalled()
    await client.connect()
    expect(sink.state).toHaveBeenLastCalledWith('connecting', expect.any(String), undefined)
  })
  it('does not connect until explicitly requested and puts token only in the first WS frame', async () => {
    const { client, socket, sink, request, factory } = setup()
    expect(request).not.toHaveBeenCalled()
    await client.connect()
    expect(request.mock.calls[0]?.[1]?.headers).toEqual({
      'Idempotency-Key': '1a122a65-5b5b-4203-8985-f6c0198cd046',
    })
    expect(factory.mock.calls[0]).toEqual([
      'ws://127.0.0.1:5174/api/v1/terminals/' + grant.sessionId + '/connect',
      'whaledeck.terminal.v1',
    ])
    expect(JSON.stringify(factory.mock.calls)).not.toContain(grant.connectToken)
    expect(socket.sent).toEqual([])
    socket.open()
    expect(socket.sent).toEqual([{ token: grant.connectToken }])
    socket.row({ kind: 'ready' })
    expect(sink.ready).toHaveBeenCalledOnce()
    expect(socket.sent[1]).toEqual({ kind: 'resize', columns: 80, rows: 24 })
  })
  it('relays UTF-8 and controls as bytes, bounds resize and displays exit code without reopening', async () => {
    const { client, socket, sink, factory, open } = setup()
    await open()
    expect(client.input('中文🙂\r\x03')).toBe(true)
    const data = socket.sent[socket.sent.length - 1]?.data as string
    expect(new TextDecoder().decode(Uint8Array.from(atob(data), (c) => c.charCodeAt(0)))).toBe(
      '中文🙂\r\x03',
    )
    client.resize(1000, 1)
    expect(socket.sent[socket.sent.length - 1]).toEqual({ kind: 'resize', columns: 400, rows: 2 })
    socket.row({ kind: 'output', data: btoa('hello') })
    expect(Array.from(vi.mocked(sink.write).mock.calls[0]![0])).toEqual([104, 101, 108, 108, 111])
    expect(typeof vi.mocked(sink.write).mock.calls[0]![1]).toBe('function')
    socket.row({ kind: 'exit', exitCode: 7 })
    socket.closed()
    expect(sink.state).toHaveBeenLastCalledWith('closed', '进程已结束 · 退出码 7', 7)
    expect(client.input('ignored')).toBe(false)
    await vi.advanceTimersByTimeAsync(60_000)
    expect(factory).toHaveBeenCalledOnce()
  })
  it('rejects oversized paste before sending any bytes and refuses a full outgoing buffer', async () => {
    const { client, socket, sink, open } = setup()
    await open()
    const before = socket.sent.length
    expect(client.input('a'.repeat(16385))).toBe(false)
    expect(socket.sent).toHaveLength(before)
    socket.bufferedAmount = 65536
    expect(client.input('a')).toBe(false)
    expect(sink.notice).toHaveBeenCalledTimes(2)
  })
  it('splits bounded large input into at most 4 KiB binary frames', async () => {
    const { client, socket, open } = setup()
    await open()
    expect(client.input('b'.repeat(10000))).toBe(true)
    const chunks = socket.sent
      .filter((row) => row.kind === 'input')
      .map((row) => atob(row.data as string))
    expect(chunks.map((chunk) => chunk.length)).toEqual([4096, 4096, 1808])
  })
  it('clears on abnormal closure, refreshes authority on forbidden and never automatically reconnects', async () => {
    const { socket, sink, factory, open } = setup()
    await open()
    socket.row({ kind: 'output', data: btoa('private output') })
    const cleared = vi.mocked(sink.clear).mock.calls.length
    socket.closed(1008, 'TERMINAL_FORBIDDEN')
    expect(vi.mocked(sink.clear).mock.calls.length).toBeGreaterThan(cleared)
    expect(sink.permissionLost).toHaveBeenCalledOnce()
    await vi.advanceTimersByTimeAsync(60_000)
    expect(factory).toHaveBeenCalledOnce()
  })
  it('fails closed on a malformed or target-changing server frame', async () => {
    const { socket, sink, open } = setup()
    await open()
    socket.row({ kind: 'output', data: btoa('text'), token: 'private' })
    expect(sink.state).toHaveBeenLastCalledWith('error', expect.any(String), undefined)
    expect(socket.readyState).toBe(WebSocket.CLOSED)
  })
  it('bounds the rendering queue independently of total server output', async () => {
    const { socket, sink, open } = setup()
    await open()
    vi.mocked(sink.write).mockImplementation(() => {})
    for (let index = 0; index < 257; index++)
      socket.row({ kind: 'output', data: btoa('a'.repeat(4096)) })
    expect(socket.readyState).toBe(WebSocket.CLOSED)
    expect(sink.state).toHaveBeenLastCalledWith('error', expect.any(String), undefined)
  })
  it('ignores late grant responses after leaving and closes their exact record without opening a socket', async () => {
    const { client, request, factory } = setup()
    let complete!: (value: unknown) => void
    request.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          complete = resolve
        }),
    )
    const connecting = client.connect()
    client.dispose()
    complete(grant)
    await connecting
    expect(factory).not.toHaveBeenCalled()
    expect(request).toHaveBeenLastCalledWith(`terminals/${grant.sessionId}/close`, {
      method: 'POST',
    })
  })
  it('expires a connection attempt without a ready frame', async () => {
    const { client, socket, sink } = setup()
    await client.connect()
    socket.open()
    await vi.advanceTimersByTimeAsync(15_000)
    expect(sink.state).toHaveBeenLastCalledWith('error', '连接超时，请手动重新连接。', undefined)
    expect(socket.readyState).toBe(WebSocket.CLOSED)
  })
  it('stops and clears when a live authorization check returns 403', async () => {
    const { request, sink, open } = setup()
    await open()
    request.mockRejectedValueOnce(new ApiError('forbidden', 403, {}))
    await vi.advanceTimersByTimeAsync(15_000)
    expect(sink.permissionLost).toHaveBeenCalledOnce()
    expect(sink.state).toHaveBeenLastCalledWith('error', '登录或管理权限已失效。', undefined)
  })
  it('reuses the same unacknowledged HTTP request key, not the consumed socket grant', async () => {
    const { client, request } = setup()
    request.mockRejectedValueOnce(new Error('network'))
    await client.connect()
    await client.connect()
    const attempts = request.mock.calls.filter(([path]) => path === 'terminals/host/grants')
    expect(attempts).toHaveLength(2)
    expect(attempts[0]?.[1]?.body).toBe(attempts[1]?.[1]?.body)
  })
  it('shows only a bounded public trace identifier, never a raw server exception', async () => {
    const { client, request, sink } = setup()
    request.mockRejectedValueOnce(
      new ApiError('private upstream exception', 503, { traceId: 'public-trace-123' }),
    )
    await client.connect()
    expect(sink.notice).toHaveBeenCalledWith('请求追踪号：public-trace-123')
    expect(sink.state).toHaveBeenLastCalledWith(
      'error',
      '无法建立终端会话，请稍后重试。',
      undefined,
    )
    request.mockRejectedValueOnce(
      new ApiError('private upstream exception', 503, { traceId: 'x'.repeat(129) }),
    )
    await client.connect()
    expect(sink.notice).toHaveBeenCalledTimes(1)
  })
  it('cancels timers and detaches listeners on disposal', async () => {
    const { client, socket, request, open } = setup()
    await open()
    client.dispose()
    const count = request.mock.calls.length
    expect(socket.onmessage).toBeNull()
    expect(socket.onopen).toBeNull()
    await vi.advanceTimersByTimeAsync(60_000)
    expect(request).toHaveBeenCalledTimes(count)
  })
  it('uses wss with HTTPS and rejects a path/identifier injection', () => {
    expect(terminalSocketUrl(grant.sessionId, 'https://example.invalid/')).toMatch(/^wss:/)
    expect(() => terminalSocketUrl('../other', 'http://127.0.0.1/')).toThrow('终端会话标识无效。')
  })
})
