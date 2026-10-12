import { ApiError, apiRequest } from '@/services/apiClient'
import type { MetricSeriesSnapshot, OverviewResponse, PortalItem } from '@/services/contracts'
import { RollingTelemetryHistory, sumAlignedTelemetry } from '@/services/rollingTelemetry'

export type ResourceKey = 'agents' | 'websites' | 'databases' | 'containers'
export type ContainerState = 'available' | 'running' | 'stopped' | 'restarting'

export interface ResourceSummary {
  key: ResourceKey
  label: string
  value: number
  unit: string
  detail: string
}

export interface UsageMetric {
  key: 'cpu' | 'memory' | 'disk'
  label: string
  percentage: number
  value: string
  detail: string
}

export interface TelemetryStat {
  label: string
  value: string
  tone: 'primary' | 'success' | 'warning' | 'info'
}

export interface TelemetrySeries {
  name: string
  color: string
  values: Array<number | null>
}

export interface TelemetryFilter {
  value: string
  label: string
  stats: TelemetryStat[]
  series: TelemetrySeries[]
}

export interface TelemetryDataset {
  unit: string
  labels: string[]
  filters: TelemetryFilter[]
}

export interface SystemInformation {
  label: string
  value: string
  hint?: string
}

export interface RecommendedApplication {
  id: string
  name: string
  version: string
  description: string
  category: string
  icon: 'gateway' | 'database' | 'cache' | 'ai' | 'runtime' | 'developer'
  state: ContainerState
  installedVersion?: string
}

export interface WorkstationOverviewSnapshot {
  scope: 'Public' | 'Administrator'
  sampledAtUtc: string
  platformStatus: string
  catalogIsStale: boolean
  portals: PortalItem[]
  resources: ResourceSummary[]
  healthMessage: string
  healthDetail: string
  usage: UsageMetric[]
  network: TelemetryDataset
  diskIo: TelemetryDataset
  system: SystemInformation[]
  applications: RecommendedApplication[]
}

const emptyTelemetry = (): TelemetryDataset => ({
  unit: 'MB/s',
  labels: [],
  filters: [{ value: 'none', label: '暂无设备采样', stats: [], series: [] }],
})

export function createEmptyWorkstationOverview(): WorkstationOverviewSnapshot {
  return {
    scope: 'Public',
    sampledAtUtc: '',
    platformStatus: 'Connecting',
    catalogIsStale: false,
    portals: [],
    resources: [],
    healthMessage: '等待工作站数据',
    healthDetail: '正在连接 Agent 与指标服务',
    usage: [],
    network: emptyTelemetry(),
    diskIo: emptyTelemetry(),
    system: [],
    applications: [],
  }
}

export async function fetchWorkstationOverview(
  rollingHistory = new RollingTelemetryHistory(),
  signal?: AbortSignal,
): Promise<WorkstationOverviewSnapshot> {
  const response = await apiRequest<OverviewResponse>('overview', { signal })
  if (response.scope === 'Public') {
    rollingHistory.clear()
    return {
      ...createEmptyWorkstationOverview(),
      scope: 'Public',
      sampledAtUtc: response.sampledAtUtc,
      platformStatus: response.platformStatus,
      portals: response.portals,
      healthMessage: '平台在线',
      healthDetail: '当前账号可访问个人门户与已发布入口',
    }
  }

  let history: MetricSeriesSnapshot[] = []
  try {
    history = await apiRequest<MetricSeriesSnapshot[]>(
      'metrics?kinds=network.receive&kinds=network.send&kinds=disk.read&kinds=disk.write&take=60',
      { signal },
    )
  } catch (reason) {
    if (signal?.aborted || (reason instanceof ApiError && [401, 403].includes(reason.status))) {
      rollingHistory.clear()
      throw reason
    }
    // A fresh Agent snapshot is still useful while historical metrics recover.
  }
  signal?.throwIfAborted()
  history = rollingHistory.update(history, response.metrics ?? [], response.sampledAtUtc)

  const resources = response.resources ?? []
  const containers = resources.filter((item) => item.type === 'Container')
  const running = containers.filter((item) => item.state.toLowerCase() === 'running').length
  const metricValues = response.metrics ?? []
  const metric = (kind: string, aggregate: 'first' | 'max' = 'first') => {
    const values = metricValues.filter((item) => item.kind === kind).map((item) => item.value)
    if (values.length === 0) return 0
    return aggregate === 'max' ? Math.max(...values) : values[0]!
  }
  const cpu = metric('cpu.utilization')
  const memory = metric('memory.utilization')
  const disk = metric('disk.utilization', 'max')
  const applications = (response.applications ?? []).slice(0, 6).map((app, index) => ({
    id: app.id,
    name: app.name,
    version: app.version,
    description: app.description,
    category: '容器应用',
    icon: (['gateway', 'database', 'cache', 'ai', 'runtime', 'developer'] as const)[index % 6]!,
    state: applicationState(app.installed, app.state),
    installedVersion: app.installed ? app.version : undefined,
  }))

  return {
    scope: 'Administrator',
    sampledAtUtc: response.sampledAtUtc,
    platformStatus: response.platformStatus,
    catalogIsStale: response.catalogIsStale,
    portals: response.portals,
    resources: [
      {
        key: 'agents',
        label: '智能体',
        value: response.agent?.available ? 1 : 0,
        unit: '个',
        detail: response.agent?.status || '不可用',
      },
      {
        key: 'websites',
        label: '网站',
        value: response.portals.length,
        unit: '个',
        detail: '个人与公共门户入口',
      },
      {
        key: 'databases',
        label: '数据库',
        value: containers.filter((item) => item.name.startsWith('database-platform-')).length,
        unit: '个',
        detail: '仅容器网络可访问',
      },
      {
        key: 'containers',
        label: '容器',
        value: containers.length,
        unit: '个',
        detail: `${running} 运行 · ${containers.length - running} 未运行`,
      },
    ],
    healthMessage:
      response.agent?.available && response.agent.dockerAvailable ? '主机资源正常' : '管理服务降级',
    healthDetail: `Agent ${response.agent?.status || '不可用'} · 采样 ${formatSampleTime(response.sampledAtUtc)}`,
    usage: [
      {
        key: 'cpu',
        label: 'CPU 使用率',
        percentage: cpu,
        value: `${cpu.toFixed(1)}%`,
        detail: '6 秒采样',
      },
      {
        key: 'memory',
        label: '内存使用率',
        percentage: memory,
        value: `${memory.toFixed(1)}%`,
        detail: response.host
          ? `${formatBytes(response.host.totalMemoryBytes)} 总内存`
          : '无主机数据',
      },
      {
        key: 'disk',
        label: '硬盘使用率',
        percentage: disk,
        value: `${disk.toFixed(1)}%`,
        detail: '当前最高文件系统占用',
      },
    ],
    network: makeTelemetry('network', history, metricValues),
    diskIo: makeTelemetry('disk', history, metricValues),
    system: response.host
      ? [
          { label: '主机名称', value: response.host.hostName },
          { label: '发行版本', value: response.host.distribution },
          { label: '内核版本', value: response.host.kernelVersion },
          { label: '系统类型', value: `Linux · ${response.host.architecture}` },
          ...response.host.addresses.map((item) => ({
            label: `主机地址 · ${item.interfaceName}`,
            value: item.address,
            hint: '当前活动网卡',
          })),
          { label: '启动时间', value: new Date(response.host.bootTimeUtc).toLocaleString() },
          { label: '运行时间', value: formatDuration(response.host.uptime) },
        ]
      : [],
    applications,
  }
}

function applicationState(installed: boolean, state: string | null): ContainerState {
  if (!installed) return 'available'
  const normalized = state?.toLowerCase()
  if (normalized === 'stopped' || normalized === 'exited') return 'stopped'
  if (normalized === 'restarting') return 'restarting'
  return 'running'
}

function makeTelemetry(
  mode: 'network' | 'disk',
  history: MetricSeriesSnapshot[],
  current: OverviewResponse['metrics'],
): TelemetryDataset {
  const firstKind = mode === 'network' ? 'network.receive' : 'disk.read'
  const secondKind = mode === 'network' ? 'network.send' : 'disk.write'
  const devices = [
    ...new Set(
      history
        .filter((series) => series.kind === firstKind || series.kind === secondKind)
        .map((series) => series.deviceId),
    ),
  ]
    .filter((device) => mode === 'disk' || isHostNetworkDevice(device))
    .sort()
  if (devices.length === 0) return emptyTelemetry()

  const source = history.find((series) => series.kind === firstKind) ?? history[0]
  const labels =
    source?.points.map((point) =>
      new Date(point.sampledAtUtc).toLocaleTimeString([], {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      }),
    ) ?? []
  const deviceFilters = devices.map((device) =>
    telemetryFilter(mode, device, history, current ?? [], firstKind, secondKind),
  )
  return {
    unit: 'MB/s',
    labels,
    filters: [aggregateTelemetry(mode, deviceFilters), ...deviceFilters],
  }
}

function telemetryFilter(
  mode: 'network' | 'disk',
  device: string,
  history: MetricSeriesSnapshot[],
  current: NonNullable<OverviewResponse['metrics']>,
  firstKind: string,
  secondKind: string,
): TelemetryFilter {
  const first =
    history.find((item) => item.kind === firstKind && item.deviceId === device)?.points ?? []
  const second =
    history.find((item) => item.kind === secondKind && item.deviceId === device)?.points ?? []
  const firstValue = first[first.length - 1]?.value ?? null
  const secondValue = second[second.length - 1]?.value ?? null
  const total = (kind: string) =>
    current.find((item) => item.kind === kind && item.deviceId === device)?.value ?? 0
  const stats: TelemetryStat[] =
    mode === 'network'
      ? [
          { label: '当前下行', value: formatRate(firstValue), tone: 'success' },
          { label: '当前上行', value: formatRate(secondValue), tone: 'primary' },
          { label: '累计接收', value: formatBytes(total('network.receive.total')), tone: 'info' },
          { label: '累计发送', value: formatBytes(total('network.send.total')), tone: 'warning' },
        ]
      : [
          { label: '读取', value: formatRate(firstValue), tone: 'success' },
          { label: '写入', value: formatRate(secondValue), tone: 'warning' },
          { label: '时间窗口', value: '6 分钟', tone: 'info' },
        ]
  return {
    value: device,
    label: device,
    stats,
    series: [
      {
        name: mode === 'network' ? '下行' : '读取',
        color: '#409eff',
        values: first.map((point) => (point.value === null ? null : point.value / 1024 / 1024)),
      },
      {
        name: mode === 'network' ? '上行' : '写入',
        color: mode === 'network' ? '#67c23a' : '#e6a23c',
        values: second.map((point) => (point.value === null ? null : point.value / 1024 / 1024)),
      },
    ],
  }
}

function aggregateTelemetry(mode: 'network' | 'disk', filters: TelemetryFilter[]): TelemetryFilter {
  const sumSeries = (seriesIndex: number) =>
    sumAlignedTelemetry(filters.map((item) => item.series[seriesIndex]?.values ?? []))
  const first = sumSeries(0)
  const second = sumSeries(1)
  return {
    value: 'all',
    label: mode === 'network' ? '全部网卡' : '全部磁盘',
    stats: [
      {
        label: mode === 'network' ? '当前下行' : '读取',
        value: formatRate(
          first[first.length - 1] === null || first.length === 0
            ? null
            : first[first.length - 1]! * 1024 * 1024,
        ),
        tone: 'success',
      },
      {
        label: mode === 'network' ? '当前上行' : '写入',
        value: formatRate(
          second[second.length - 1] === null || second.length === 0
            ? null
            : second[second.length - 1]! * 1024 * 1024,
        ),
        tone: mode === 'network' ? 'primary' : 'warning',
      },
      { label: '设备', value: String(filters.length), tone: 'info' },
    ],
    series: [
      { name: mode === 'network' ? '下行' : '读取', color: '#409eff', values: first },
      {
        name: mode === 'network' ? '上行' : '写入',
        color: mode === 'network' ? '#67c23a' : '#e6a23c',
        values: second,
      },
    ],
  }
}

function formatSampleTime(value: string) {
  return value ? new Date(value).toLocaleTimeString() : '未知'
}

function formatRate(bytesPerSecond: number | null) {
  return bytesPerSecond === null ? '暂无采样' : `${(bytesPerSecond / 1024 / 1024).toFixed(2)} MB/s`
}

function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  return `${(value / 1024 ** index).toFixed(index >= 3 ? 1 : 0)} ${units[index]}`
}

function formatDuration(value: string) {
  const match = /(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/.exec(value)
  if (!match) return value || '未知'
  const days = Number(match[1] ?? 0)
  const hours = Number(match[2])
  const minutes = Number(match[3])
  return `${days ? `${days} 天 ` : ''}${hours} 小时 ${minutes} 分`
}

function isHostNetworkDevice(name: string) {
  return !/^(lo|veth|docker|br-|virbr|cni|flannel|tun|tap)/.test(name)
}
