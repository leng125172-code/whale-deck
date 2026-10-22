<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Box, Delete, Plus, RefreshRight, VideoPause, VideoPlay } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest, apiUrl } from '@/services/apiClient'
import { enqueueOperation, planOperation, stageSecret } from '@/services/operations'
import ContainerUsageCell from '@/components/containers/ContainerUsageCell.vue'
import ContainerRelationships from '@/components/containers/ContainerRelationships.vue'
import ContainerFilePanel from '@/components/containers/ContainerFilePanel.vue'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import type {
  ContainerInspect,
  ContainerLogs,
  ContainerStats,
  ContainerUsageBatch,
  ContainerUpdateRun,
  DockerEvent,
} from '@/services/contracts'

interface Container {
  id: string
  name: string
  image: string
  state: string
  status: string
  isProtected: boolean
  protectionLevel: string
  labels: Record<string, string>
  ports?: string[]
  networks?: string[]
  addresses?: string[]
  createdAtUtc?: string | null
  imageId?: string
  volumeNames?: string[]
  networkIds?: string[]
}

const items = ref<Container[]>([])
const route = useRoute()
const router = useRouter()
function terminalAllowed(item: Container) {
  return !item.isProtected && item.state === 'running' && /^[a-f0-9]{64}$/.test(item.id)
}
function openTerminal(item: Container) {
  if (terminalAllowed(item))
    void router.push({ name: 'container-terminal', params: { containerId: item.id } })
}
const search = ref('')
const viewMode = ref('cards')
const wideTable = ref(window.innerWidth > 1045)
function syncTableWidth() {
  wideTable.value = window.innerWidth > 1045
}
onMounted(() => window.addEventListener('resize', syncTableWidth))
onUnmounted(() => window.removeEventListener('resize', syncTableWidth))
const stateFilter = ref('all')
const projectFilter = ref(typeof route.query.project === 'string' ? route.query.project : '')
watch(
  () => route.query.project,
  (value) => {
    projectFilter.value = typeof value === 'string' ? value : ''
  },
)
const relatedResource = computed(() =>
  typeof route.query.resourceId === 'string' ? route.query.resourceId : '',
)
const counts = computed(() => ({
  all: items.value.length,
  running: items.value.filter((item) => item.state === 'running').length,
  stopped: items.value.filter((item) => ['exited', 'created', 'dead'].includes(item.state)).length,
  abnormal: items.value.filter(
    (item) =>
      ['restarting', 'dead', 'paused'].includes(item.state) || item.status.includes('unhealthy'),
  ).length,
}))
const projects = computed(() =>
  [
    ...new Set(
      items.value
        .map((item) => item.labels['com.docker.compose.project'])
        .filter((value): value is string => Boolean(value)),
    ),
  ].sort(),
)
const visibleItems = computed(() =>
  items.value.filter((item) => {
    const keyword = search.value.trim().toLowerCase()
    const matchesText =
      !keyword ||
      [
        item.name,
        item.image,
        ...Object.entries(item.labels).map(([key, value]) => key + '=' + value),
      ]
        .join(' ')
        .toLowerCase()
        .includes(keyword)
    const matchesState =
      stateFilter.value === 'all' ||
      (stateFilter.value === 'running'
        ? item.state === 'running'
        : stateFilter.value === 'abnormal'
          ? ['restarting', 'dead', 'paused'].includes(item.state) ||
            item.status.includes('unhealthy')
          : ['exited', 'created', 'dead'].includes(item.state))
    return (
      matchesText &&
      matchesState &&
      (!projectFilter.value || item.labels['com.docker.compose.project'] === projectFilter.value) &&
      (!relatedResource.value ||
        (relatedResource.value.startsWith('image:') &&
          item.imageId === relatedResource.value.slice(6)) ||
        (relatedResource.value.startsWith('network:') &&
          item.networkIds?.includes(relatedResource.value.slice(8))) ||
        (relatedResource.value.startsWith('volume:') &&
          item.volumeNames?.includes(relatedResource.value.slice(7))))
    )
  }),
)
const selectedIds = ref<string[]>([])
watch([search, stateFilter, projectFilter, relatedResource], () => {
  selectedIds.value = []
})
const loading = ref(false)
const loadError = ref('')
const usageError = ref('')
const usage = ref<ContainerUsageBatch | null>(null)
const usageById = computed(
  () => new Map(usage.value?.items.map((item) => [item.containerId, item]) ?? []),
)
const now = ref(Date.now())
const usageStale = computed(
  () =>
    Boolean(usageError.value) ||
    Boolean(usage.value?.isStale) ||
    (usage.value?.sampledAtUtc ? now.value - Date.parse(usage.value.sampledAtUtc) > 18_000 : false),
)
const refreshInterval = ref(6000)
function sampleStale(id: string) {
  const sampledAt = usageById.value.get(id)?.sampledAtUtc
  return sampledAt ? now.value - Date.parse(sampledAt) > 18_000 : false
}
let refreshTimer: number | undefined
let staleTimer: number | undefined
let loadRequest: AbortController | undefined
let loadInFlight = false
let loadGeneration = 0
let detailGeneration = 0
onUnmounted(() => {
  loadGeneration++
  detailGeneration++
  loadRequest?.abort()
  window.clearInterval(refreshTimer)
  window.clearInterval(staleTimer)
  document.removeEventListener('visibilitychange', visibilityChanged)
})
const batchBusy = ref(false)
const createVisible = ref(false)
const submitting = ref(false)
const form = reactive({
  name: '',
  image: '',
  cpus: 1,
  memoryMb: 512,
  autoUpdate: false,
  versionPolicy: '*',
  maintenanceWindow: 'Sun@20:00-23:59',
  restartPolicy: 'unless-stopped',
  command: '',
  entrypoint: '',
  environment: '',
  ports: '',
  volumes: '',
  networks: 'bridge',
  labels: '{}',
  healthCommand: '',
  healthIntervalSeconds: 30,
  healthTimeoutSeconds: 5,
  healthRetries: 3,
})
const detailVisible = ref(false)
const filesVisible = ref(false)
const detailLoading = ref(false)
const selected = ref<Container | null>(null)
watch([detailVisible, () => selected.value?.id], () => {
  filesVisible.value = false
})
const logs = ref<ContainerLogs>({ lines: [], truncated: false })
const stats = ref<Record<string, string>>({})
const inspect = ref<ContainerInspect | null>(null)
const updateHistory = ref<ContainerUpdateRun[]>([])
const eventTimeline = ref<DockerEvent[]>([])

function resetForm() {
  Object.assign(form, {
    name: '',
    image: '',
    cpus: 1,
    memoryMb: 512,
    autoUpdate: false,
    versionPolicy: '*',
    maintenanceWindow: 'Sun@20:00-23:59',
    restartPolicy: 'unless-stopped',
    command: '',
    entrypoint: '',
    environment: '',
    ports: '',
    volumes: '',
    networks: 'bridge',
    labels: '{}',
    healthCommand: '',
    healthIntervalSeconds: 30,
    healthTimeoutSeconds: 5,
    healthRetries: 3,
  })
}

function openCreate() {
  resetForm()
  createVisible.value = true
}

function clearPrivateData() {
  loadGeneration++
  loadRequest?.abort()
  loadInFlight = false
  loading.value = false
  detailGeneration++
  items.value = []
  usage.value = null
  selectedIds.value = []
  detailVisible.value = false
  selected.value = null
  inspect.value = null
  logs.value = { lines: [], truncated: false }
  eventTimeline.value = []
  stats.value = {}
  updateHistory.value = []
  form.environment = ''
  createVisible.value = false
}

async function load(manual = true) {
  if (loadInFlight) return
  loadInFlight = true
  const generation = ++loadGeneration
  loadRequest = new AbortController()
  const controller = loadRequest
  const timeout = window.setTimeout(() => controller.abort(), 30_000)
  loading.value = manual || items.value.length === 0
  loadError.value = ''
  usageError.value = ''
  try {
    const [result, latestUsage] = await Promise.all([
      apiRequest<Container[]>('containers?includeStopped=true', { signal: controller.signal }),
      apiRequest<ContainerUsageBatch>('containers/usage', { signal: controller.signal }).catch(
        (error) => {
          if (error instanceof ApiError && [401, 403].includes(error.status)) throw error
          if (controller.signal.aborted) throw error
          if (generation === loadGeneration)
            usageError.value = '容器资源采样暂不可用，旧采样不作为实时数据展示。'
          return null
        },
      ),
    ])
    if (generation !== loadGeneration) return
    now.value = Date.now()
    items.value = result
    usage.value = latestUsage
    selectedIds.value = selectedIds.value.filter((id) =>
      items.value.some((item) => item.id === id && !item.isProtected),
    )
  } catch (error) {
    if (generation !== loadGeneration) return
    loadError.value = error instanceof Error ? error.message : '无法加载容器'
    if (error instanceof ApiError && [401, 403].includes(error.status)) {
      refreshInterval.value = 0
      clearPrivateData()
    }
  } finally {
    window.clearTimeout(timeout)
    if (generation === loadGeneration) {
      loading.value = false
      loadInFlight = false
    }
  }
}

function startRefresh() {
  window.clearInterval(refreshTimer)
  if (refreshInterval.value > 0)
    refreshTimer = window.setInterval(() => {
      now.value = Date.now()
      if (!document.hidden) void load(false)
    }, refreshInterval.value)
}
function visibilityChanged() {
  if (!document.hidden && refreshInterval.value > 0) void load(false)
}
watch(refreshInterval, startRefresh)
watch(
  () => [authSession.user.value?.subject, authSession.user.value?.isAdministrator],
  () => {
    loadGeneration++
    loadRequest?.abort()
    loadInFlight = false
    clearPrivateData()
    if (authSession.user.value?.isAdministrator) void load()
    else refreshInterval.value = 0
  },
)

function toggleSelected(id: string, selected: boolean) {
  selectedIds.value = selected
    ? [...new Set([...selectedIds.value, id])]
    : selectedIds.value.filter((item) => item !== id)
}

async function batchLifecycle(action: 'start' | 'stop' | 'restart' | 'delete') {
  if (!selectedIds.value.length) return
  batchBusy.value = true
  try {
    const labels = { start: '启动', stop: '停止', restart: '重启', delete: '删除' } as const
    const parameters = {
      containerIds: JSON.stringify(selectedIds.value),
      timeoutSeconds: '10',
      preserveVolumes: 'true',
    }
    const operation = `batch-${action}`
    const plan = await planOperation('containers', operation, 'batch', parameters)
    await ElMessageBox.confirm(
      [...plan.changes, ...plan.warnings, action === 'delete' ? '数据卷与镜像默认保留。' : '']
        .filter(Boolean)
        .join('\n'),
      `批量${labels[action]}确认`,
      { type: action === 'delete' ? 'error' : 'warning', confirmButtonText: labels[action] },
    )
    const job = await enqueueOperation(
      'containers',
      operation,
      'batch',
      parameters,
      true,
      plan.planHash,
    )
    selectedIds.value = []
    ElMessage.success(`批量任务已提交：${job.id}`)
  } finally {
    batchBusy.value = false
  }
}

async function lifecycle(item: Container, action: 'start' | 'stop' | 'restart') {
  if (item.isProtected) {
    ElMessage.warning('受保护容器只能通过平台维护流程操作')
    return
  }
  const label = action === 'start' ? '启动' : action === 'stop' ? '停止' : '重启'
  await ElMessageBox.confirm(`${label}容器“${item.name}”？`, `${label}确认`, {
    type: 'warning',
    confirmButtonText: label,
    cancelButtonText: '取消',
  })
  const job = await enqueueOperation(
    'containers',
    action,
    item.id,
    { timeoutSeconds: '10' },
    action !== 'start',
  )
  ElMessage.success(`任务已提交：${job.id}`)
  await load()
}

async function removeContainer(item: Container) {
  if (item.isProtected) {
    ElMessage.warning('受保护容器不能从普通容器页删除')
    return
  }
  const parameters = { preserveVolumes: 'true', timeoutSeconds: '10' }
  const plan = await planOperation('containers', 'delete', item.id, parameters)
  await ElMessageBox.confirm(
    [...plan.changes, ...plan.warnings, '容器会被删除；匿名卷、镜像和应用数据默认保留。'].join(
      '\n',
    ),
    `删除容器 ${item.name}`,
    { type: 'error', confirmButtonText: '删除容器', cancelButtonText: '取消' },
  )
  const job = await enqueueOperation(
    'containers',
    'delete',
    item.id,
    parameters,
    true,
    plan.planHash,
  )
  ElMessage.success(`删除任务已提交：${job.id}`)
}

function canUpdate(item: Container) {
  const enabled =
    item.labels['io.whaledeck.autoupdate'] === 'true' || item.labels.autoupdate === 'true'
  return enabled && !item.isProtected && !item.labels['com.docker.compose.project']
}

async function updateContainer(item: Container) {
  if (!canUpdate(item)) {
    ElMessage.warning('只有启用 autoupdate=true 的非受保护独立容器可以更新')
    return
  }
  const parameters: Record<string, string> = { automatic: 'false' }
  const plan = await planOperation('containers', 'update', item.id, parameters)
  await ElMessageBox.confirm(
    [...plan.changes, ...plan.warnings].join('\n'),
    `更新容器 ${item.name}`,
    { type: 'warning', confirmButtonText: '更新', cancelButtonText: '取消' },
  )
  const job = await enqueueOperation(
    'containers',
    'update',
    item.id,
    parameters,
    true,
    plan.planHash,
  )
  ElMessage.success(`更新任务已提交：${job.id}`)
}

async function rebuildContainer(item: Container) {
  if (item.isProtected || item.labels['com.docker.compose.project']) {
    ElMessage.warning('受保护或 Compose 容器必须通过对应维护流程重建')
    return
  }
  const plan = await planOperation('containers', 'rebuild', item.id, {})
  await ElMessageBox.confirm(
    [...plan.changes, ...plan.warnings].join('\n'),
    `重建容器 ${item.name}`,
    { type: 'warning', confirmButtonText: '重建', cancelButtonText: '取消' },
  )
  const job = await enqueueOperation('containers', 'rebuild', item.id, {}, true, plan.planHash)
  ElMessage.success(`重建任务已提交：${job.id}`)
}

function downloadDiagnostics(item: Container) {
  const anchor = document.createElement('a')
  anchor.href = apiUrl(`containers/${encodeURIComponent(item.id)}/diagnostics`)
  anchor.download = `whaledeck-container-${item.name}-diagnostics.zip`
  document.body.append(anchor)
  anchor.click()
  anchor.remove()
}

async function createContainer() {
  submitting.value = true
  try {
    const parameters: Record<string, string> = {
      name: form.name.trim(),
      image: form.image.trim(),
      cpus: String(form.cpus),
      memoryMb: String(form.memoryMb),
      autoUpdate: String(form.autoUpdate),
      versionPolicy: form.versionPolicy.trim() || '*',
      maintenanceWindow: form.maintenanceWindow.trim(),
      restartPolicy: form.restartPolicy,
    }
    if (form.command.trim()) parameters.command = form.command.trim()
    if (form.entrypoint.trim()) parameters.entrypoint = form.entrypoint.trim()
    if (form.ports.trim()) parameters.ports = form.ports.trim()
    if (form.volumes.trim()) parameters.volumes = form.volumes.trim()
    if (form.networks.trim()) parameters.networks = form.networks.trim()
    if (form.labels.trim() && form.labels.trim() !== '{}') parameters.labels = form.labels.trim()
    if (form.healthCommand.trim()) {
      parameters.healthCommand = form.healthCommand.trim()
      parameters.healthIntervalSeconds = String(form.healthIntervalSeconds)
      parameters.healthTimeoutSeconds = String(form.healthTimeoutSeconds)
      parameters.healthRetries = String(form.healthRetries)
    }
    if (form.environment.trim()) {
      const ticket = await stageSecret(form.environment)
      parameters.inputTicket = ticket.token
      parameters.inputKind = 'environment'
    }
    const plan = await planOperation('containers', 'create', 'new', parameters)
    const details =
      [...plan.changes, ...plan.warnings].join('\n') ||
      '将拉取镜像、创建并启动受 Whale Deck 管理的容器。'
    await ElMessageBox.confirm(details, '确认创建容器', {
      type: 'warning',
      confirmButtonText: '创建',
      cancelButtonText: '取消',
    })
    const job = await enqueueOperation(
      'containers',
      'create',
      'new',
      parameters,
      true,
      plan.planHash,
    )
    createVisible.value = false
    ElMessage.success(`创建任务已提交：${job.id}`)
  } finally {
    submitting.value = false
  }
}

async function openDetails(item: Container) {
  const generation = ++detailGeneration
  selected.value = item
  detailVisible.value = true
  detailLoading.value = true
  logs.value = { lines: [], truncated: false }
  stats.value = {}
  inspect.value = null
  updateHistory.value = []
  eventTimeline.value = []
  try {
    const id = encodeURIComponent(item.id)
    const [logResult, statsResult, inspectResult, historyResult, eventResult] = await Promise.all([
      apiRequest<ContainerLogs>(`containers/${id}/logs?tail=500&sinceMinutes=60`),
      item.state === 'running'
        ? apiRequest<ContainerStats>(`containers/${id}/stats`)
        : Promise.resolve({ values: {} }),
      apiRequest<ContainerInspect>(`containers/${id}/inspect`),
      apiRequest<ContainerUpdateRun[]>(
        `containers/update-history?containerId=${encodeURIComponent(item.name)}&take=20`,
      ),
      apiRequest<DockerEvent[]>(`containers/events?containerId=${id}&take=50`),
    ])
    if (generation !== detailGeneration || !detailVisible.value || selected.value?.id !== item.id)
      return
    logs.value = logResult
    stats.value = statsResult.values
    inspect.value = inspectResult
    updateHistory.value = historyResult
    eventTimeline.value = eventResult
  } catch (error) {
    if (generation !== detailGeneration) return
    if (error instanceof ApiError && [401, 403].includes(error.status)) {
      refreshInterval.value = 0
      clearPrivateData()
    }
    ElMessage.error(error instanceof Error ? error.message : '读取容器详情失败')
  } finally {
    if (generation === detailGeneration) detailLoading.value = false
  }
}

function copyContainer() {
  if (!inspect.value) return
  const safeLabels = Object.fromEntries(
    Object.entries(inspect.value.labels).filter(
      ([key]) => !key.startsWith('io.whaledeck.') && !key.startsWith('com.docker.compose.'),
    ),
  )
  Object.assign(form, {
    name: `${inspect.value.name}-copy`.slice(0, 64),
    image: inspect.value.image,
    cpus: inspect.value.cpus || 1,
    memoryMb: inspect.value.memoryMb || 512,
    autoUpdate: inspect.value.labels['io.whaledeck.autoupdate'] === 'true',
    versionPolicy: inspect.value.labels['io.whaledeck.update.version-policy'] || '*',
    maintenanceWindow:
      inspect.value.labels['io.whaledeck.update.maintenance-window'] || 'Sun@20:00-23:59',
    restartPolicy:
      inspect.value.restartPolicy === 'always' ? 'unless-stopped' : inspect.value.restartPolicy,
    command: inspect.value.command.length ? JSON.stringify(inspect.value.command) : '',
    entrypoint: inspect.value.entrypoint.length ? JSON.stringify(inspect.value.entrypoint) : '',
    environment: '',
    ports: inspect.value.ports.join(','),
    volumes: inspect.value.volumes.filter((item) => !item.startsWith('/')).join(','),
    networks: inspect.value.networks
      .filter((item) => !item.startsWith('database-platform') && !item.startsWith('whaledeck'))
      .join(','),
    labels: JSON.stringify(safeLabels, null, 2),
    healthCommand:
      inspect.value.healthCommand[0] === 'CMD-SHELL'
        ? inspect.value.healthCommand.slice(1).join(' ')
        : '',
    healthIntervalSeconds: inspect.value.healthIntervalSeconds || 30,
    healthTimeoutSeconds: inspect.value.healthTimeoutSeconds || 5,
    healthRetries: inspect.value.healthRetries || 3,
  })
  detailVisible.value = false
  createVisible.value = true
  if (inspect.value.environmentNames.length)
    ElMessage.info('环境变量值不会复制，请在创建前重新填写。')
}

onMounted(() => {
  void load()
  startRefresh()
  staleTimer = window.setInterval(() => {
    now.value = Date.now()
  }, 1000)
  document.addEventListener('visibilitychange', visibilityChanged)
})
</script>

<template>
  <div class="containers-page">
    <el-alert v-if="relatedResource" type="info" :closable="false" title="仅显示关联容器">
      <code class="related-resource-id">{{ relatedResource }}</code>
      <router-link to="/containers">显示全部</router-link>
      <p>筛选使用真实资源标识；空列表可能表示资源已移除、没有引用或 Agent 尚未提供关联摘要。</p>
    </el-alert>
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">DOCKER</span>
        <h1>容器</h1>
        <p>受保护的基础容器不会出现在普通生命周期操作中。</p>
      </div>
      <div class="page-heading__actions">
        <el-select v-model="refreshInterval" class="container-refresh" aria-label="容器自动刷新">
          <el-option label="不自动刷新" :value="0" /><el-option label="每 6 秒刷新" :value="6000" />
          <el-option label="每 30 秒刷新" :value="30000" /><el-option
            label="每 60 秒刷新"
            :value="60000"
          />
        </el-select>
        <el-button :loading="loading" @click="load()"
          ><el-icon><RefreshRight /></el-icon>刷新</el-button
        ><el-button type="primary" @click="openCreate"
          ><el-icon><Plus /></el-icon>创建容器</el-button
        >
      </div>
    </header>
    <el-alert v-if="loadError" :title="loadError" type="error" :closable="false" show-icon />
    <el-alert v-if="usageError" :title="usageError" type="warning" :closable="false" show-icon />
    <p
      v-else-if="usage?.isTruncated || usage?.skippedCount || usageStale"
      class="container-sampling-note"
    >
      {{ usageStale ? '采样已过期，等待 Agent 恢复。' : '' }}
      {{ usage?.isTruncated ? `采集上限 256 个，实际 ${usage.totalCount} 个。` : '' }}
      {{ usage?.skippedCount ? `${usage.skippedCount} 个采样未取得，未伪造资源使用率。` : '' }}
    </p>
    <section class="panel container-filters">
      <el-radio-group v-model="stateFilter" aria-label="筛选容器状态">
        <el-radio-button value="all">全部 {{ counts.all }}</el-radio-button>
        <el-radio-button value="running">运行中 {{ counts.running }}</el-radio-button>
        <el-radio-button value="stopped">已停止 {{ counts.stopped }}</el-radio-button>
        <el-radio-button value="abnormal">异常 {{ counts.abnormal }}</el-radio-button>
      </el-radio-group>
      <el-radio-group v-model="viewMode" aria-label="容器显示方式"
        ><el-radio-button value="cards">卡片</el-radio-button
        ><el-radio-button value="table">列表</el-radio-button></el-radio-group
      >
      <div class="container-filters__search">
        <el-select
          v-model="projectFilter"
          placeholder="全部 Compose 项目"
          clearable
          aria-label="筛选 Compose 项目"
        >
          <el-option v-for="project in projects" :key="project" :value="project" :label="project" />
        </el-select>
        <el-input
          v-model="search"
          clearable
          placeholder="搜索名称、镜像或标签"
          aria-label="搜索容器"
        />
      </div>
    </section>
    <section v-if="selectedIds.length" class="panel batch-toolbar">
      <strong>已选择 {{ selectedIds.length }} 个容器</strong>
      <div>
        <el-button :loading="batchBusy" @click="batchLifecycle('start')">批量启动</el-button>
        <el-button :loading="batchBusy" @click="batchLifecycle('stop')">批量停止</el-button>
        <el-button :loading="batchBusy" @click="batchLifecycle('restart')">批量重启</el-button>
        <el-button type="danger" :loading="batchBusy" @click="batchLifecycle('delete')"
          >批量删除</el-button
        >
        <el-button link @click="selectedIds = []">清除选择</el-button>
      </div>
    </section>
    <section v-if="viewMode === 'table'" class="panel container-table" v-loading="loading">
      <el-table :data="visibleItems" empty-text="没有符合筛选条件的容器" row-key="id">
        <el-table-column width="48"
          ><template #default="{ row }"
            ><el-checkbox
              :model-value="selectedIds.includes(row.id)"
              :disabled="row.isProtected"
              :aria-label="`选择容器 ${row.name}`"
              @change="toggleSelected(row.id, Boolean($event))" /></template
        ></el-table-column>
        <el-table-column prop="name" label="名称" min-width="220" sortable show-overflow-tooltip
          ><template #default="{ row }"
            ><el-button link type="primary" @click="openDetails(row)">{{
              row.name
            }}</el-button></template
          ></el-table-column
        >
        <el-table-column prop="image" label="镜像" min-width="220" show-overflow-tooltip />
        <el-table-column prop="state" label="状态" width="130" sortable
          ><template #default="{ row }"
            ><el-tag size="small" :type="row.state === 'running' ? 'success' : 'info'">{{
              row.state
            }}</el-tag></template
          ></el-table-column
        >
        <el-table-column label="资源使用率" min-width="210">
          <template #default="{ row }"
            ><ContainerUsageCell
              :sample="usageById.get(row.id)"
              :state="row.state"
              :stale="usageStale || sampleStale(row.id)"
          /></template>
        </el-table-column>
        <el-table-column label="项目 / 保护" min-width="180" show-overflow-tooltip
          ><template #default="{ row }"
            ><div>{{ row.labels['com.docker.compose.project'] || '独立容器' }}</div>
            <el-tag v-if="row.isProtected" size="small" type="warning" effect="plain">{{
              row.protectionLevel
            }}</el-tag></template
          ></el-table-column
        >
        <el-table-column label="IP 地址" min-width="160" show-overflow-tooltip
          ><template #default="{ row }">{{
            row.addresses?.join(', ') || '—'
          }}</template></el-table-column
        >
        <el-table-column label="端口" min-width="220" show-overflow-tooltip
          ><template #default="{ row }">{{
            row.ports?.join(', ') || '—'
          }}</template></el-table-column
        >
        <el-table-column label="操作" width="210" :fixed="wideTable ? 'right' : undefined"
          ><template #default="{ row }"
            ><div class="container-table__actions">
              <el-button link type="primary" @click="openDetails(row)">详情 / 日志</el-button
              ><el-button
                v-if="!row.isProtected"
                link
                type="primary"
                :disabled="!terminalAllowed(row)"
                @click="openTerminal(row)"
                >终端</el-button
              ><el-dropdown
                v-if="!row.isProtected"
                trigger="click"
                @command="
                  (action: string) =>
                    action === 'delete'
                      ? removeContainer(row)
                      : lifecycle(row, action as 'start' | 'stop' | 'restart')
                "
                ><el-button link type="primary">更多</el-button
                ><template #dropdown
                  ><el-dropdown-menu
                    ><el-dropdown-item :command="row.state === 'running' ? 'stop' : 'start'">{{
                      row.state === 'running' ? '停止' : '启动'
                    }}</el-dropdown-item
                    ><el-dropdown-item command="restart" :disabled="row.state !== 'running'"
                      >重启</el-dropdown-item
                    ><el-dropdown-item command="delete" divided
                      >删除</el-dropdown-item
                    ></el-dropdown-menu
                  ></template
                ></el-dropdown
              >
            </div></template
          ></el-table-column
        >
      </el-table>
    </section>
    <section v-else class="container-grid" v-loading="loading">
      <el-empty v-if="!loading && !visibleItems.length" description="没有符合筛选条件的容器" />
      <article v-for="item in visibleItems" :key="item.id" class="panel container-card">
        <el-checkbox
          :model-value="selectedIds.includes(item.id)"
          :disabled="item.isProtected"
          :aria-label="`选择容器 ${item.name}`"
          @change="toggleSelected(item.id, Boolean($event))"
        />
        <span class="container-card__icon"
          ><el-icon><Box /></el-icon
        ></span>
        <div>
          <strong>{{ item.name }}</strong
          ><small>{{ item.image }}</small
          ><span>{{ item.status }}</span>
          <div class="container-card__metadata">
            <span v-if="item.labels['com.docker.compose.project']"
              >项目：{{ item.labels['com.docker.compose.project'] }}</span
            >
            <span v-if="item.addresses?.length">IP：{{ item.addresses.join(', ') }}</span>
            <span v-if="item.ports?.length" :title="item.ports.join(', ')"
              >端口：{{ item.ports.join(', ') }}</span
            >
          </div>
        </div>
        <div class="container-card__states">
          <el-tag :type="item.state === 'running' ? 'success' : 'info'" effect="light">{{
            item.state
          }}</el-tag>
          <el-tag v-if="item.isProtected" type="warning" effect="plain">{{
            item.protectionLevel
          }}</el-tag>
        </div>
        <ContainerUsageCell
          class="container-card__usage"
          :sample="usageById.get(item.id)"
          :state="item.state"
          :stale="usageStale || sampleStale(item.id)"
        />
        <div class="container-card__actions">
          <el-button link type="primary" @click="openDetails(item)">详情</el-button>
          <el-button
            v-if="!item.isProtected"
            link
            type="primary"
            :disabled="!terminalAllowed(item)"
            @click="openTerminal(item)"
            >终端</el-button
          >
          <el-button v-if="canUpdate(item)" link type="primary" @click="updateContainer(item)"
            ><el-icon><RefreshRight /></el-icon>更新</el-button
          >
          <el-button
            v-if="!item.isProtected && item.state !== 'running'"
            link
            type="primary"
            :disabled="item.isProtected"
            @click="lifecycle(item, 'start')"
            ><el-icon><VideoPlay /></el-icon>启动</el-button
          >
          <template v-else-if="!item.isProtected"
            ><el-button
              link
              type="danger"
              :disabled="item.isProtected"
              @click="lifecycle(item, 'stop')"
              ><el-icon><VideoPause /></el-icon>停止</el-button
            ><el-button
              link
              type="primary"
              :disabled="item.isProtected"
              @click="lifecycle(item, 'restart')"
              ><el-icon><RefreshRight /></el-icon>重启</el-button
            ></template
          >
          <el-button v-if="!item.isProtected" link type="danger" @click="removeContainer(item)"
            ><el-icon><Delete /></el-icon>删除</el-button
          >
        </div>
      </article>
    </section>
    <el-dialog v-model="createVisible" title="创建受管容器" width="min(760px, calc(100vw - 32px))">
      <el-alert
        title="端口、命名卷和普通网络经过服务端预检；宿主目录和平台内部网络不会从此页面开放。"
        type="info"
        :closable="false"
        show-icon
      />
      <el-form label-position="top" class="container-form">
        <el-form-item label="容器名称"
          ><el-input v-model="form.name" placeholder="my-application"
        /></el-form-item>
        <el-form-item label="OCI 镜像"
          ><el-input v-model="form.image" placeholder="nginx:1.29-alpine"
        /></el-form-item>
        <div class="container-form__resources">
          <el-form-item label="CPU"
            ><el-input-number v-model="form.cpus" :min="0.1" :max="32" :step="0.5" /></el-form-item
          ><el-form-item label="内存 (MiB)"
            ><el-input-number v-model="form.memoryMb" :min="32" :max="32768" :step="128"
          /></el-form-item>
        </div>
        <el-form-item label="重启策略">
          <el-select v-model="form.restartPolicy">
            <el-option label="除非手动停止" value="unless-stopped" />
            <el-option label="失败时重启" value="on-failure" />
            <el-option label="不自动重启" value="no" />
          </el-select>
        </el-form-item>
        <el-form-item label="命令（JSON 字符串数组）">
          <el-input v-model="form.command" placeholder='["nginx", "-g", "daemon off;"]' />
        </el-form-item>
        <el-form-item label="Entrypoint（JSON 字符串数组）">
          <el-input v-model="form.entrypoint" placeholder='["/docker-entrypoint.sh"]' />
        </el-form-item>
        <el-form-item label="环境变量">
          <el-input v-model="form.environment" type="textarea" :rows="4" placeholder="KEY=value" />
          <span class="form-hint">值通过一次性 Secret 传输，任务记录只保留字段名。</span>
        </el-form-item>
        <el-form-item label="端口映射">
          <el-input v-model="form.ports" placeholder="0.0.0.0:8088:80/tcp,127.0.0.1:9090:90/tcp" />
          <span class="form-hint">多个映射用逗号分隔；只允许全网卡或本机回环地址。</span>
        </el-form-item>
        <el-form-item label="命名卷">
          <el-input
            v-model="form.volumes"
            placeholder="my-data:/var/lib/app:rw,my-config:/etc/app:ro"
          />
          <span class="form-hint">不接受任意宿主机路径，平台和数据库卷不可使用。</span>
        </el-form-item>
        <el-form-item label="网络">
          <el-input v-model="form.networks" placeholder="bridge,team-apps" />
          <span class="form-hint">平台与 database-platform 内部网络不可从通用容器流程加入。</span>
        </el-form-item>
        <el-form-item label="标签（JSON）">
          <el-input
            v-model="form.labels"
            type="textarea"
            :rows="3"
            placeholder='{"team":"platform"}'
          />
        </el-form-item>
        <el-form-item label="健康检查命令">
          <el-input
            v-model="form.healthCommand"
            placeholder="wget -qO- http://127.0.0.1/health || exit 1"
          />
        </el-form-item>
        <div v-if="form.healthCommand" class="container-form__health">
          <el-form-item label="间隔（秒）"
            ><el-input-number v-model="form.healthIntervalSeconds" :min="5" :max="3600"
          /></el-form-item>
          <el-form-item label="超时（秒）"
            ><el-input-number v-model="form.healthTimeoutSeconds" :min="1" :max="300"
          /></el-form-item>
          <el-form-item label="重试"
            ><el-input-number v-model="form.healthRetries" :min="1" :max="20"
          /></el-form-item>
        </div>
        <el-form-item label="自动更新"
          ><el-switch v-model="form.autoUpdate" /><span class="form-hint"
            >只有启用此标签的普通容器才进入自动更新。</span
          ></el-form-item
        >
        <div v-if="form.autoUpdate" class="container-form__resources">
          <el-form-item label="版本策略">
            <el-input v-model="form.versionPolicy" placeholder="*、1.*、1.2.*、>=1.2.3" />
          </el-form-item>
          <el-form-item label="维护窗口">
            <el-input v-model="form.maintenanceWindow" placeholder="Sun@20:00-23:59" />
          </el-form-item>
        </div>
      </el-form>
      <template #footer
        ><el-button @click="createVisible = false">取消</el-button
        ><el-button
          type="primary"
          :loading="submitting"
          :disabled="!form.name || !form.image"
          @click="createContainer"
          >预检并创建</el-button
        ></template
      >
    </el-dialog>
    <el-drawer
      v-model="detailVisible"
      :title="selected ? `容器详情 · ${selected.name}` : '容器详情'"
      size="min(760px, 96vw)"
    >
      <div v-loading="detailLoading" class="container-detail">
        <div class="container-detail__actions">
          <el-button
            v-if="selected && !selected.isProtected"
            :disabled="!terminalAllowed(selected)"
            :aria-expanded="filesVisible"
            @click="filesVisible = !filesVisible"
            >{{ filesVisible ? '收起文件' : '容器文件' }}</el-button
          >
          <el-button
            v-if="selected && !selected.isProtected"
            :disabled="!terminalAllowed(selected)"
            @click="openTerminal(selected)"
            >容器终端</el-button
          >
          <el-button :disabled="!selected" @click="selected && downloadDiagnostics(selected)"
            >导出诊断</el-button
          >
          <el-button
            :disabled="
              !selected ||
              selected.isProtected ||
              Boolean(selected.labels['com.docker.compose.project'])
            "
            @click="selected && rebuildContainer(selected)"
            >重建容器</el-button
          >
          <el-button :disabled="!inspect || selected?.isProtected" @click="copyContainer"
            >复制配置创建</el-button
          >
        </div>
        <ContainerFilePanel
          v-if="detailVisible && filesVisible && selected && terminalAllowed(selected)"
          :key="selected.id"
          :container-id="selected.id"
        />
        <el-descriptions v-if="selected" :column="1" border>
          <el-descriptions-item label="镜像">{{ selected.image }}</el-descriptions-item>
        </el-descriptions>
        <el-descriptions
          v-if="selected"
          class="container-detail__summary"
          :class="{ 'container-detail__summary--paired': wideTable }"
          :column="wideTable ? 2 : 1"
          border
        >
          <el-descriptions-item label="状态">{{ selected.status }}</el-descriptions-item>
          <el-descriptions-item label="保护级别">{{
            selected.protectionLevel
          }}</el-descriptions-item>
          <el-descriptions-item label="CPU">{{ stats.cpu || '—' }}</el-descriptions-item>
          <el-descriptions-item label="内存"
            >{{ stats.memory || '—' }} · {{ stats.memoryPercent || '—' }}</el-descriptions-item
          >
          <el-descriptions-item label="网络 I/O">{{ stats.network || '—' }}</el-descriptions-item>
          <el-descriptions-item label="磁盘 I/O">{{ stats.block || '—' }}</el-descriptions-item>
          <el-descriptions-item label="进程数">{{ stats.pids || '—' }}</el-descriptions-item>
          <template v-if="inspect">
            <el-descriptions-item label="重启策略">{{
              inspect.restartPolicy
            }}</el-descriptions-item>
          </template>
        </el-descriptions>
        <el-descriptions v-if="inspect" :column="1" border>
          <el-descriptions-item label="资源限制"
            >{{ inspect.cpus || '默认' }} CPU ·
            {{
              inspect.memoryMb > 0 ? formatBytes(inspect.memoryMb * 1024 ** 2) : '默认'
            }}</el-descriptions-item
          >
          <el-descriptions-item v-if="!inspect.relationships" label="端口">{{
            inspect.ports.join(', ') || '无'
          }}</el-descriptions-item>
          <el-descriptions-item v-if="!inspect.relationships" label="网络">{{
            inspect.networks.join(', ') || '无'
          }}</el-descriptions-item>
          <el-descriptions-item v-if="!inspect.relationships" label="挂载">{{
            inspect.volumes.join(', ') || '无'
          }}</el-descriptions-item>
          <el-descriptions-item label="环境变量名">{{
            inspect.environmentNames.join(', ') || '无'
          }}</el-descriptions-item>
          <el-descriptions-item label="命令">{{
            inspect.command.join(' ') || '继承镜像'
          }}</el-descriptions-item>
          <el-descriptions-item label="Entrypoint">{{
            inspect.entrypoint.join(' ') || '继承镜像'
          }}</el-descriptions-item>
          <el-descriptions-item label="健康检查">{{
            inspect.healthCommand.join(' ') || '未配置'
          }}</el-descriptions-item>
        </el-descriptions>
        <ContainerRelationships v-if="inspect" :inspect="inspect" />
        <div class="log-heading">
          <h3>更新历史</h3>
        </div>
        <el-table v-if="updateHistory.length" :data="updateHistory" size="small">
          <el-table-column prop="startedAtUtc" label="时间" min-width="180" />
          <el-table-column prop="image" label="镜像" min-width="180" />
          <el-table-column prop="result" label="结果" width="100" />
          <el-table-column label="回滚" width="80">
            <template #default="scope">{{ scope.row.wasRolledBack ? '是' : '否' }}</template>
          </el-table-column>
        </el-table>
        <el-empty v-else description="暂无更新记录" :image-size="64" />
        <div class="log-heading">
          <h3>Docker 事件</h3>
          <span>最近 {{ eventTimeline.length }} 条</span>
        </div>
        <el-table
          v-if="eventTimeline.length"
          :data="eventTimeline"
          size="small"
          row-key="fingerprint"
        >
          <el-table-column label="时间" min-width="180">
            <template #default="scope">{{
              new Date(scope.row.occurredAtUtc).toLocaleString()
            }}</template>
          </el-table-column>
          <el-table-column prop="eventType" label="类型" width="100" />
          <el-table-column label="动作" min-width="180">
            <template #default="scope"
              ><el-popover trigger="click" placement="top" :width="300">
                <template #reference
                  ><button
                    type="button"
                    class="docker-event-action"
                    :title="scope.row.action"
                    :aria-label="'查看完整动作：' + scope.row.action"
                  >
                    {{ scope.row.action }}
                  </button></template
                >
                <div class="docker-event-action-full">{{ scope.row.action }}</div>
              </el-popover></template
            >
          </el-table-column>
          <el-table-column prop="image" label="镜像" min-width="200" show-overflow-tooltip />
        </el-table>
        <el-empty v-else description="Agent 启动后暂无相关事件" :image-size="64" />
        <div class="log-heading">
          <h3>最近一小时日志</h3>
          <el-tag v-if="logs.truncated" type="warning" effect="plain">已按 64 KiB 截断</el-tag>
        </div>
        <pre class="container-logs">{{ logs.lines.join('\n') || '当前时间范围内没有日志。' }}</pre>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.containers-page {
  display: grid;
  gap: 18px;
}
.related-resource-id {
  display: block;
  overflow-wrap: anywhere;
  white-space: normal;
}
.container-grid {
  display: grid;
  gap: 10px;
  min-height: 240px;
}
.container-refresh {
  width: 152px;
}
.page-heading__actions {
  flex-wrap: wrap;
}
.container-sampling-note {
  margin: 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.container-card__usage {
  width: 210px;
  max-width: 100%;
}
.container-card__states {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.container-filters {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 14px;
  padding: 14px 16px;
}
.container-filters :deep(.el-radio-group) {
  flex-wrap: wrap;
  row-gap: 8px;
  max-width: 100%;
}
.container-table {
  padding: 16px;
  min-width: 0;
}
.container-table__actions {
  display: flex;
  align-items: center;
  gap: 12px;
}
.container-filters__search {
  display: flex;
  flex-wrap: wrap;
  flex: 1 1 420px;
  gap: 12px;
  min-width: 0;
  max-width: 600px;
}
.container-filters__search > * {
  flex: 1 1 180px;
  min-width: 0;
}
.container-card__metadata {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  min-width: 0;
}
.container-card__metadata span {
  overflow-wrap: anywhere;
}
.docker-event-action {
  display: block;
  max-width: 100%;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  padding: 3px 8px;
  border: 1px solid var(--el-color-primary-light-5);
  border-radius: 4px;
  color: var(--el-color-primary);
  background: transparent;
  font: inherit;
  cursor: pointer;
}
.docker-event-action-full {
  overflow-wrap: anywhere;
  white-space: pre-wrap;
  font-size: 12px;
}
.batch-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 12px 16px;
}
.batch-toolbar > div {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.container-card {
  display: grid;
  grid-template-columns: auto auto minmax(0, 1fr) auto auto auto;
  align-items: center;
  gap: 14px;
  padding: 14px 16px;
}
.container-card__icon {
  display: grid;
  width: 42px;
  height: 42px;
  place-items: center;
  border-radius: 10px;
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
}
.container-card > div:nth-child(3) {
  display: grid;
  min-width: 0;
  overflow-wrap: anywhere;
}
.container-card small {
  overflow: hidden;
  color: var(--el-text-color-secondary);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.container-card span {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.container-card__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.container-card__actions .el-button + .el-button {
  margin-left: 0;
}
.container-form {
  margin-top: 18px;
}
.container-form__resources {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
.container-form__health {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}
.form-hint {
  display: block;
  width: 100%;
  margin-top: 5px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.container-detail__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: flex-end;
}
.container-detail__actions .el-button + .el-button {
  margin-left: 0;
}
.container-detail {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  min-width: 0;
  gap: 18px;
  min-height: 320px;
}
.container-detail :deep(.el-descriptions__label) {
  width: 96px;
  word-break: keep-all;
}
.container-detail :deep(.el-descriptions__table) {
  table-layout: fixed;
}
.container-detail__summary--paired :deep(.el-descriptions__content) {
  width: calc(50% - 96px);
}
.container-detail :deep(.el-descriptions__content) {
  overflow-wrap: anywhere;
  word-break: break-word;
}
.log-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.log-heading h3 {
  margin: 0;
  font-size: 14px;
}
.log-heading > span {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.container-logs {
  max-height: 52vh;
  overflow: auto;
  margin: 0;
  padding: 14px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  color: var(--el-text-color-regular);
  background: var(--el-fill-color-light);
  font: inherit;
  font-size: 12px;
  line-height: 1.65;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
@media (max-width: 760px) {
  .container-refresh {
    width: 100%;
  }
  .page-heading__actions {
    width: 100%;
  }
  .container-card__usage {
    grid-column: 3 / -1;
    width: 100%;
  }
  .container-card {
    grid-template-columns: auto auto minmax(0, 1fr);
  }
  .container-card__states {
    grid-column: 3 / -1;
  }
  .container-card__actions {
    grid-column: 3 / -1;
  }
  .batch-toolbar {
    align-items: flex-start;
    flex-direction: column;
  }
  .container-form__resources {
    grid-template-columns: 1fr;
  }
  .container-form__health {
    grid-template-columns: 1fr;
  }
}
</style>
