<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ArrowRight,
  Box,
  CircleCheckFilled,
  Coin,
  CopyDocument,
  Cpu,
  DataAnalysis,
  MagicStick,
  Link,
  Monitor,
  Platform,
  Promotion,
  RefreshRight,
  VideoPlay,
} from '@element-plus/icons-vue'
import TelemetryChart from '@/components/overview/TelemetryChart.vue'
import {
  createEmptyWorkstationOverview,
  fetchWorkstationOverview,
  type ContainerState,
  type RecommendedApplication,
  type ResourceKey,
  type DiskMeasure,
  type TelemetryWindow,
  telemetryWindowLabels,
} from '@/services/workstationOverview'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { RollingTelemetryHistory } from '@/services/rollingTelemetry'
import { enqueueOperation } from '@/services/operations'
import type { Job } from '@/services/contracts'

type MonitorMode = 'network' | 'disk'

const router = useRouter()
const snapshot = ref(createEmptyWorkstationOverview())
const loading = ref(true)
const loadError = ref('')
const monitorMode = ref<MonitorMode>('network')
const networkFilter = ref('all')
const diskFilter = ref('all')
const monitorWindow = ref<TelemetryWindow>('live')
const diskMeasure = ref<DiskMeasure>('throughput')
const telemetryChanging = ref(false)
const telemetryHistory = new RollingTelemetryHistory()
let refreshTimer: number | undefined
let request: AbortController | undefined
let generation = 0
let inFlight = false

const resourceIcons = {
  agents: MagicStick,
  websites: Monitor,
  databases: Coin,
  containers: Box,
} satisfies Record<ResourceKey, object>

const applicationIcons = {
  gateway: Promotion,
  database: DataAnalysis,
  cache: Coin,
  ai: MagicStick,
  runtime: Platform,
  developer: Cpu,
} as const

const applicationStateIcons: Partial<Record<ContainerState, object>> = {
  running: VideoPlay,
  restarting: RefreshRight,
}

const applicationStateLabels = {
  available: '未安装',
  running: '运行中',
  stopped: '已停止',
  restarting: '重启中',
} satisfies Record<ContainerState, string>

const monitorOptions = [
  { label: '流量监控', value: 'network' },
  { label: '磁盘 IO', value: 'disk' },
]

const activeTelemetry = computed(() =>
  monitorMode.value === 'network' ? snapshot.value.network : snapshot.value.diskIo,
)

const selectedTelemetryFilter = computed({
  get: () => (monitorMode.value === 'network' ? networkFilter.value : diskFilter.value),
  set: (value: string) => {
    if (monitorMode.value === 'network') networkFilter.value = value
    else diskFilter.value = value
  },
})

const activeFilter = computed(
  () =>
    activeTelemetry.value.filters.find((item) => item.value === selectedTelemetryFilter.value) ??
    activeTelemetry.value.filters[0]!,
)

function gaugeColor(percentage: number) {
  if (percentage >= 85) return 'var(--el-color-danger)'
  if (percentage >= 70) return 'var(--el-color-warning)'
  return 'var(--el-color-primary)'
}

async function loadOverview() {
  if (inFlight) return
  inFlight = true
  const current = ++generation
  request = new AbortController()
  const controller = request
  const timeout = window.setTimeout(() => controller.abort(), 30_000)
  loading.value = snapshot.value.sampledAtUtc === ''
  loadError.value = ''
  try {
    const value = await fetchWorkstationOverview(telemetryHistory, controller.signal, {
      window: monitorWindow.value,
      diskMeasure: diskMeasure.value,
    })
    if (current === generation) snapshot.value = value
  } catch (reason) {
    if (current !== generation) return
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
      telemetryHistory.clear()
      snapshot.value = createEmptyWorkstationOverview()
    }
    loadError.value = reason instanceof Error ? reason.message : '读取工作站状态失败'
  } finally {
    window.clearTimeout(timeout)
    if (current === generation) {
      loading.value = false
      telemetryChanging.value = false
      inFlight = false
    }
  }
}

watch([monitorWindow, diskMeasure], () => {
  generation++
  request?.abort()
  inFlight = false
  const empty = createEmptyWorkstationOverview()
  snapshot.value = { ...snapshot.value, network: empty.network, diskIo: empty.diskIo }
  telemetryChanging.value = true
  void loadOverview()
})

watch(
  () => [authSession.user.value?.subject, authSession.user.value?.isAdministrator],
  () => {
    generation++
    request?.abort()
    inFlight = false
    telemetryHistory.clear()
    snapshot.value = createEmptyWorkstationOverview()
    networkFilter.value = 'all'
    diskFilter.value = 'all'
    if (authSession.authenticated.value) void loadOverview()
  },
)

function showDeferredAction(action: 'details' | 'install' | 'manage', app: RecommendedApplication) {
  void router.push({ name: 'applications', query: { app: app.id, action } })
}

async function copySystemValue(label: string, value: string) {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value)
    } else {
      const textarea = document.createElement('textarea')
      textarea.value = value
      textarea.style.position = 'fixed'
      textarea.style.opacity = '0'
      document.body.appendChild(textarea)
      textarea.select()
      document.execCommand('copy')
      textarea.remove()
    }
    ElMessage.success(`${label}已复制`)
  } catch {
    ElMessage.error(`${label}复制失败`)
  }
}

async function confirmContainerAction(
  app: RecommendedApplication,
  action: 'stop' | 'restart' | 'start',
) {
  const actionLabel = action === 'stop' ? '关闭' : action === 'start' ? '启动' : '重启'
  const confirmText = `确认${actionLabel}`

  try {
    await ElMessageBox.confirm(
      `将${actionLabel} ${app.name} ${app.installedVersion ?? ''}。操作会提交到工作站任务队列。`,
      `${actionLabel}容器确认`,
      {
        type: 'warning',
        confirmButtonText: confirmText,
        cancelButtonText: '取消',
        distinguishCancelAndClose: true,
      },
    )
  } catch {
    return
  }

  const job = await enqueueOperation(
    'applications',
    action,
    '',
    { slug: app.id, deleteVolumes: 'false' },
    true,
  )
  app.state = 'restarting'
  ElMessage.success(`${app.name} ${actionLabel}任务已提交`)
  await waitForJob(job.id)
  await loadOverview()
}

async function waitForJob(jobId: string) {
  const deadline = Date.now() + 120_000
  while (Date.now() < deadline) {
    const job = await apiRequest<Job>(`jobs/${encodeURIComponent(jobId)}`)
    if (job.state === 'Succeeded') return
    if (['Failed', 'Canceled', 'RolledBack'].includes(job.state)) {
      throw new Error(job.errorCode || `任务${job.state}`)
    }
    await new Promise((resolve) => window.setTimeout(resolve, 1500))
  }
  throw new Error('等待应用任务完成超时')
}

onMounted(() => {
  void loadOverview()
  refreshTimer = window.setInterval(() => {
    if (!document.hidden) void loadOverview()
  }, 6000)
})
onBeforeUnmount(() => {
  generation++
  request?.abort()
  telemetryHistory.clear()
  window.clearInterval(refreshTimer)
})
</script>

<template>
  <div class="overview-page">
    <section id="overview" class="page-heading overview-heading">
      <div>
        <div class="page-heading__meta">
          <el-tag size="small" type="primary" effect="light" round>WORKSTATION</el-tag>
          <span>Precision-7920-Tower</span>
          <el-tag
            v-if="snapshot.platformStatus"
            size="small"
            :type="snapshot.platformStatus === 'Healthy' ? 'success' : 'info'"
            effect="plain"
            round
            >{{ snapshot.platformStatus }}</el-tag
          >
        </div>
        <h1>工作站概览</h1>
        <p>
          {{
            snapshot.scope === 'Administrator'
              ? '集中查看资源、运行状态、实时监控、系统信息与容器应用。'
              : '访问公共入口和你自己的常用服务。'
          }}
        </p>
      </div>
    </section>

    <el-alert v-if="loadError" :title="loadError" type="error" show-icon :closable="false" />

    <section
      v-if="snapshot.portals.length > 0 || snapshot.scope === 'Public'"
      class="portal-overview"
      v-loading="loading"
      aria-label="门户入口"
    >
      <el-empty v-if="snapshot.portals.length === 0" description="还没有可用的门户入口" />
      <a
        v-for="portal in snapshot.portals"
        :key="portal.id"
        class="panel portal-overview__card"
        :href="portal.url"
        target="_blank"
        rel="noopener noreferrer"
      >
        <span
          ><el-icon><Link /></el-icon
        ></span>
        <strong>{{ portal.name }}</strong>
        <small>{{ portal.description || portal.url }}</small>
      </a>
    </section>

    <div v-if="snapshot.scope === 'Administrator'" class="overview-dashboard" v-loading="loading">
      <div class="overview-dashboard__main">
        <section id="resources" class="resource-grid content-anchor" aria-label="资源概览">
          <article
            v-for="resource in snapshot.resources"
            :key="resource.key"
            class="resource-card"
            :class="`resource-card--${resource.key}`"
          >
            <span class="resource-card__icon">
              <el-icon><component :is="resourceIcons[resource.key]" /></el-icon>
            </span>
            <div class="resource-card__copy">
              <span>{{ resource.label }}</span>
              <strong
                >{{ resource.value }}<small>{{ resource.unit }}</small></strong
              >
              <p>{{ resource.detail }}</p>
            </div>
            <span class="resource-card__arrow"
              ><el-icon><ArrowRight /></el-icon
            ></span>
          </article>
        </section>

        <section id="status" class="panel overview-panel content-anchor">
          <header class="overview-panel__header">
            <div>
              <span class="panel__eyebrow">SYSTEM STATUS</span>
              <h2>主机资源状态</h2>
            </div>
            <el-tag type="success" effect="light" round>
              <span class="live-dot" /> 实时采样
            </el-tag>
          </header>

          <div class="status-grid">
            <article class="health-card">
              <span class="health-card__halo">
                <el-icon><CircleCheckFilled /></el-icon>
              </span>
              <div>
                <span>资源健康度</span>
                <strong>{{ snapshot.healthMessage }}</strong>
                <p>{{ snapshot.healthDetail }}</p>
              </div>
            </article>

            <article v-for="metric in snapshot.usage" :key="metric.key" class="usage-card">
              <el-progress
                type="dashboard"
                :percentage="metric.percentage"
                :width="116"
                :stroke-width="8"
                :color="gaugeColor(metric.percentage)"
              >
                <template #default>
                  <strong>{{ metric.percentage.toFixed(1) }}%</strong>
                </template>
              </el-progress>
              <div class="usage-card__copy">
                <span>{{ metric.label }}</span>
                <strong>{{ metric.value }}</strong>
                <small>{{ metric.detail }}</small>
              </div>
            </article>
          </div>
        </section>

        <section id="monitoring" class="panel overview-panel monitoring-panel content-anchor">
          <header class="overview-panel__header monitoring-panel__header">
            <div>
              <span class="panel__eyebrow">TELEMETRY</span>
              <h2>{{ monitorWindow === 'live' ? '实时监控' : '历史监控' }}</h2>
            </div>
            <div class="monitoring-toolbar">
              <el-segmented v-model="monitorMode" :options="monitorOptions" />
              <el-select v-model="monitorWindow" class="monitoring-window" aria-label="监控时间窗口"
                ><el-option
                  v-for="(label, value) in telemetryWindowLabels"
                  :key="value"
                  :label="label"
                  :value="value"
              /></el-select>
              <el-select
                v-if="monitorMode === 'disk'"
                v-model="diskMeasure"
                class="monitoring-window"
                aria-label="磁盘指标"
                ><el-option label="读写速率" value="throughput" /><el-option
                  label="IOPS"
                  value="iops" /><el-option label="读写延迟" value="latency"
              /></el-select>
              <el-select
                v-model="selectedTelemetryFilter"
                class="monitoring-filter"
                aria-label="监控设备"
              >
                <el-option
                  v-for="option in activeTelemetry.filters"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </div>
          </header>

          <el-alert
            v-if="snapshot.telemetryError"
            :title="snapshot.telemetryError"
            type="warning"
            :closable="false"
            show-icon
          />
          <p class="telemetry-note">
            {{
              monitorWindow === 'live'
                ? '每 6 秒采集，缺失区间留空'
                : `数据库按 ${snapshot.telemetryResolutionSeconds} 秒聚合，当前时间桶可能尚未结束，缺失区间留空`
            }}<span v-if="monitorMode === 'disk' && diskMeasure === 'latency'">
              · 延迟按完成 I/O 数加权；无 I/O 不记为零延迟</span
            >
          </p>

          <div class="telemetry-summary">
            <div
              v-for="stat in activeFilter.stats"
              :key="stat.label"
              class="telemetry-stat"
              :class="`telemetry-stat--${stat.tone}`"
            >
              <span>{{ stat.label }}</span>
              <strong>{{ stat.value }}</strong>
            </div>
            <div class="telemetry-legend" aria-label="图例">
              <span v-for="item in activeFilter.series" :key="item.name">
                <i :style="{ backgroundColor: item.color }" />{{ item.name }}
              </span>
            </div>
          </div>

          <TelemetryChart
            v-loading="telemetryChanging"
            :key="`${monitorMode}-${selectedTelemetryFilter}-${monitorWindow}-${diskMeasure}`"
            :labels="activeTelemetry.labels"
            :series="activeFilter.series"
            :unit="activeTelemetry.unit"
            :window="monitorWindow"
          />
        </section>
      </div>

      <aside class="overview-dashboard__side">
        <section id="system" class="panel overview-panel content-anchor">
          <header class="overview-panel__header">
            <div>
              <span class="panel__eyebrow">HOST INFORMATION</span>
              <h2>系统信息</h2>
            </div>
            <el-tag effect="plain" round>Linux 工作站</el-tag>
          </header>

          <div class="system-grid">
            <article v-for="item in snapshot.system" :key="item.label" class="system-item">
              <span>{{ item.label }}</span>
              <div class="system-item__value">
                <strong>{{ item.value }}</strong>
                <el-button
                  v-if="item.label.startsWith('主机地址')"
                  class="system-item__copy"
                  link
                  type="primary"
                  :aria-label="`复制${item.label}`"
                  :title="`复制 ${item.value}`"
                  @click="copySystemValue(item.label, item.value)"
                >
                  <el-icon><CopyDocument /></el-icon>
                </el-button>
              </div>
              <small v-if="item.hint">{{ item.hint }}</small>
            </article>
          </div>
        </section>

        <section id="containers" class="panel overview-panel content-anchor">
          <header class="overview-panel__header application-header">
            <div>
              <span class="panel__eyebrow">XUANYUAN POPULAR</span>
              <h2>轩辕热门应用</h2>
              <p>
                后端定时同步轩辕热门目录；无法联网时保留最近一次有效快照。
                <el-tag v-if="snapshot.catalogIsStale" size="small" type="warning" effect="plain"
                  >快照已过期</el-tag
                >
              </p>
            </div>
          </header>

          <div class="application-grid">
            <el-empty
              v-if="!loading && snapshot.applications.length === 0"
              description="暂无可用的热门应用快照"
            />
            <article
              v-for="app in snapshot.applications"
              :key="app.id"
              class="application-card"
              :class="{ 'application-card--installed': app.installedVersion }"
            >
              <span class="application-card__icon" :class="`application-card__icon--${app.icon}`">
                <el-icon><component :is="applicationIcons[app.icon]" /></el-icon>
                <span
                  v-if="app.installedVersion"
                  class="application-card__state"
                  :class="`application-card__state--${app.state}`"
                  role="status"
                  :aria-label="applicationStateLabels[app.state]"
                  :title="applicationStateLabels[app.state]"
                >
                  <span
                    v-if="app.state === 'stopped'"
                    class="application-card__stop-icon"
                    aria-hidden="true"
                  />
                  <el-icon v-else>
                    <component :is="applicationStateIcons[app.state]" />
                  </el-icon>
                </span>
              </span>

              <div class="application-card__body">
                <div class="application-card__headline">
                  <strong>{{ app.name }}</strong>
                  <span>{{ app.description }}</span>
                </div>

                <div class="application-card__version">
                  <span>推荐 {{ app.version }}</span>
                  <span v-if="app.installedVersion">已装 {{ app.installedVersion }}</span>
                  <span
                    v-if="app.installedVersion"
                    class="application-card__state-label"
                    :class="`application-card__state-label--${app.state}`"
                  >
                    <i />{{ applicationStateLabels[app.state] }}
                  </span>
                </div>

                <footer class="application-card__actions">
                  <template v-if="app.installedVersion">
                    <el-button
                      link
                      type="danger"
                      :disabled="app.state === 'stopped' || app.state === 'restarting'"
                      @click="confirmContainerAction(app, 'stop')"
                    >
                      关闭
                    </el-button>
                    <el-button
                      link
                      type="primary"
                      :disabled="app.state === 'restarting'"
                      @click="
                        confirmContainerAction(app, app.state === 'stopped' ? 'start' : 'restart')
                      "
                    >
                      {{ app.state === 'stopped' ? '启动' : '重启' }}
                    </el-button>
                  </template>
                  <el-button link type="primary" @click="showDeferredAction('details', app)">
                    更多
                  </el-button>
                </footer>
              </div>

              <el-button
                v-if="!app.installedVersion"
                class="application-card__primary-action"
                type="primary"
                plain
                size="small"
                @click="showDeferredAction('install', app)"
              >
                安装
              </el-button>
              <el-button
                v-else
                class="application-card__primary-action"
                type="primary"
                plain
                size="small"
                @click="showDeferredAction('manage', app)"
              >
                管理
              </el-button>
            </article>
          </div>
        </section>
      </aside>
    </div>
  </div>
</template>

<style scoped>
.overview-page {
  display: grid;
  gap: 20px;
}

.portal-overview {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 14px;
  min-height: 220px;
}
.portal-overview > .el-empty {
  grid-column: 1 / -1;
}
.portal-overview__card {
  display: grid;
  min-width: 0;
  padding: 20px;
  color: inherit;
  text-decoration: none;
}
.portal-overview__card > span {
  display: grid;
  width: 42px;
  height: 42px;
  place-items: center;
  border-radius: 12px;
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  font-size: 20px;
  font-weight: 800;
}
.portal-overview__card strong {
  margin-top: 14px;
  font-size: 14px;
}
.portal-overview__card small {
  overflow: hidden;
  margin-top: 4px;
  color: var(--el-text-color-secondary);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.overview-heading {
  margin-bottom: 2px;
}

.resource-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
}

.resource-card {
  --resource-color: var(--el-color-primary);
  position: relative;
  display: flex;
  overflow: hidden;
  align-items: center;
  min-height: 132px;
  padding: 16px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--whaledeck-panel-radius);
  background: var(--whaledeck-panel-bg);
  box-shadow: 0 8px 24px rgb(31 45 61 / 4%);
  transition:
    border-color 180ms ease,
    transform 180ms ease,
    box-shadow 180ms ease;
}

.resource-card::after {
  position: absolute;
  right: -34px;
  bottom: -50px;
  width: 130px;
  height: 130px;
  border-radius: 50%;
  background: color-mix(in srgb, var(--resource-color) 9%, transparent);
  content: '';
}

.resource-card:hover {
  border-color: color-mix(in srgb, var(--resource-color) 42%, var(--el-border-color));
  box-shadow: 0 14px 30px color-mix(in srgb, var(--resource-color) 10%, transparent);
  transform: translateY(-2px);
}

.resource-card--agents {
  --resource-color: #7c6df2;
}

.resource-card--websites {
  --resource-color: #409eff;
}

.resource-card--databases {
  --resource-color: #e6a23c;
}

.resource-card--containers {
  --resource-color: #36b5a0;
}

.resource-card__icon {
  display: grid;
  width: 48px;
  height: 48px;
  flex: 0 0 48px;
  place-items: center;
  border-radius: 14px;
  color: var(--resource-color);
  background: color-mix(in srgb, var(--resource-color) 12%, transparent);
  font-size: 22px;
}

.resource-card__copy {
  display: flex;
  min-width: 0;
  flex-direction: column;
  margin-left: 14px;
}

.resource-card__copy > span {
  color: var(--el-text-color-regular);
  font-size: 14px;
}

.resource-card__copy > strong {
  margin: 3px 0 2px;
  color: var(--el-text-color-primary);
  font-size: 30px;
  line-height: 1.15;
}

.resource-card__copy > strong small {
  margin-left: 5px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  font-weight: 500;
}

.resource-card__copy p {
  overflow: hidden;
  margin: 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.resource-card__arrow {
  position: absolute;
  z-index: 1;
  right: 16px;
  bottom: 14px;
  color: color-mix(in srgb, var(--resource-color) 70%, transparent);
}

.overview-dashboard {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  align-items: stretch;
  gap: 20px;
}

.overview-dashboard__main,
.overview-dashboard__side {
  display: grid;
  min-width: 0;
  gap: 20px;
}

.overview-dashboard__main {
  grid-template-rows: auto;
}

.overview-dashboard__side {
  grid-template-rows: auto;
}

.overview-panel {
  overflow: hidden;
}

.overview-panel__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 20px;
  padding: 20px 22px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.overview-panel__header h2 {
  margin: 4px 0 0;
  font-size: 16px;
  font-weight: 600;
}

.overview-panel__header p {
  margin: 7px 0 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.live-dot {
  display: inline-block;
  width: 6px;
  height: 6px;
  margin-right: 4px;
  border-radius: 50%;
  background: currentColor;
  box-shadow: 0 0 0 4px color-mix(in srgb, currentColor 14%, transparent);
}

.status-grid {
  display: grid;
  grid-template-columns: 1.16fr repeat(3, 1fr);
  gap: 10px;
  padding: 12px;
}

.health-card,
.usage-card {
  display: flex;
  align-items: center;
  min-height: 158px;
  padding: 20px;
  border-radius: 12px;
  background: color-mix(in srgb, var(--whaledeck-soft-bg) 68%, var(--whaledeck-panel-bg));
}

.usage-card {
  justify-content: center;
  gap: 14px;
}

.health-card__halo {
  display: grid;
  width: 74px;
  height: 74px;
  flex: 0 0 74px;
  place-items: center;
  border-radius: 50%;
  color: var(--el-color-success);
  background: color-mix(in srgb, var(--el-color-success) 12%, transparent);
  font-size: 34px;
  box-shadow: 0 0 0 10px color-mix(in srgb, var(--el-color-success) 5%, transparent);
}

.health-card > div {
  display: flex;
  min-width: 0;
  flex-direction: column;
  margin-left: 22px;
}

.health-card > div > span,
.usage-card__copy span {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.health-card strong {
  margin: 4px 0;
  color: var(--el-color-success);
  font-size: 24px;
}

.health-card p {
  margin: 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.6;
}

.usage-card :deep(.el-progress__text) {
  color: var(--el-text-color-primary);
}

.usage-card :deep(.el-progress__text strong) {
  font-size: 17px;
}

.usage-card__copy {
  display: flex;
  min-width: 0;
  flex-direction: column;
}

.usage-card__copy strong {
  margin: 5px 0;
  font-size: 13px;
}

.usage-card__copy small {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.monitoring-panel__header {
  align-items: center;
}

.monitoring-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
}
.monitoring-panel__header {
  flex-wrap: wrap;
}
.monitoring-toolbar {
  flex-wrap: wrap;
  max-width: 100%;
}
.monitoring-window {
  width: 150px;
  min-width: 0;
}
.telemetry-note {
  margin: 12px 18px 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow-wrap: anywhere;
}
@media (max-width: 760px) {
  .monitoring-window {
    width: 100%;
  }
}

.monitoring-filter {
  width: 224px;
}

.telemetry-summary {
  display: flex;
  align-items: stretch;
  gap: 10px;
  padding: 18px 22px 4px;
}

.telemetry-stat {
  --stat-color: var(--el-color-info);
  min-width: 124px;
  padding: 9px 11px;
  border: 1px solid color-mix(in srgb, var(--stat-color) 20%, var(--el-border-color-lighter));
  border-radius: 9px;
  background: color-mix(in srgb, var(--stat-color) 6%, var(--whaledeck-panel-bg));
}

.telemetry-stat--primary {
  --stat-color: var(--el-color-primary);
}

.telemetry-stat--success {
  --stat-color: var(--el-color-success);
}

.telemetry-stat--warning {
  --stat-color: var(--el-color-warning);
}

.telemetry-stat span,
.telemetry-stat strong {
  display: block;
}

.telemetry-stat span {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.telemetry-stat strong {
  margin-top: 3px;
  color: var(--stat-color);
  font-size: 13px;
}

.telemetry-legend {
  display: flex;
  flex: 1;
  align-items: center;
  justify-content: flex-end;
  gap: 14px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.telemetry-legend span {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.telemetry-legend i {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.system-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  padding: 8px 14px 14px;
}

.system-item {
  display: flex;
  min-height: 64px;
  flex-direction: column;
  justify-content: center;
  padding: 9px 10px;
  border-right: 1px solid var(--el-border-color-lighter);
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.system-item:nth-child(4n) {
  border-right: 0;
}

.system-item:nth-last-child(-n + 4) {
  border-bottom: 0;
}

.system-item span {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.system-item__value {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 6px;
  margin-top: 4px;
}

.system-item__value strong {
  overflow: hidden;
  min-width: 0;
  font-size: 13px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.system-item__copy {
  width: 26px;
  height: 26px;
  flex: 0 0 26px;
  padding: 0;
  font-size: 14px;
}

.system-item small {
  margin-top: 3px;
  color: var(--el-color-primary);
  font-size: 12px;
}

.application-header {
  align-items: flex-start;
}

.application-grid {
  display: grid;
  grid-template-columns: 1fr;
  align-content: start;
  gap: 10px;
  padding: 14px;
}

.application-card {
  position: relative;
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  min-width: 0;
  min-height: 78px;
  align-items: center;
  gap: 14px;
  padding: 12px 14px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 12px;
  background: color-mix(in srgb, var(--whaledeck-soft-bg) 56%, var(--whaledeck-panel-bg));
  transition:
    border-color 180ms ease,
    box-shadow 180ms ease,
    transform 180ms ease;
}

.application-card:hover {
  border-color: color-mix(in srgb, var(--el-color-primary) 32%, var(--el-border-color));
  box-shadow: 0 10px 26px rgb(31 45 61 / 7%);
  transform: translateY(-2px);
}

.application-card--installed {
  border-color: color-mix(in srgb, var(--el-color-success) 32%, var(--el-border-color-lighter));
}

.application-card__headline {
  display: flex;
  overflow: hidden;
  min-width: 0;
  align-items: baseline;
  gap: 6px;
}

.application-card__body {
  min-width: 0;
}

.application-card__headline strong {
  flex: 0 0 auto;
  color: var(--el-text-color-primary);
  font-size: 14px;
  font-weight: 800;
  white-space: nowrap;
}

.application-card__headline span {
  overflow: hidden;
  min-width: 0;
  color: var(--el-text-color-secondary);
  font-size: 13px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.application-card__icon {
  --app-color: var(--el-color-primary);
  position: relative;
  display: grid;
  width: 42px;
  height: 42px;
  flex: 0 0 42px;
  place-items: center;
  border: 1px solid color-mix(in srgb, var(--app-color) 24%, transparent);
  border-radius: 9px;
  color: var(--app-color);
  background: color-mix(in srgb, var(--app-color) 10%, var(--whaledeck-panel-bg));
  font-size: 20px;
  box-shadow: 0 5px 14px color-mix(in srgb, var(--app-color) 10%, transparent);
}

.application-card__state {
  --state-color: var(--el-color-info);
  position: absolute;
  right: -6px;
  bottom: -6px;
  display: grid;
  width: 17px;
  height: 17px;
  place-items: center;
  border: 2px solid var(--whaledeck-panel-bg);
  border-radius: 50%;
  color: white;
  background: var(--state-color);
  font-size: 9px;
  box-shadow: 0 2px 7px color-mix(in srgb, var(--state-color) 38%, transparent);
}

.application-card__state--running {
  --state-color: var(--el-color-success);
}

.application-card__state--stopped {
  --state-color: var(--el-color-info);
}

.application-card__state--restarting {
  --state-color: var(--el-color-primary);
}

.application-card__state--restarting :deep(svg) {
  animation: application-state-spin 900ms linear infinite;
}

.application-card__stop-icon {
  width: 6px;
  height: 6px;
  border-radius: 1px;
  background: currentColor;
}

.application-card__icon--gateway {
  --app-color: #38a169;
}

.application-card__icon--database {
  --app-color: #3b82b5;
}

.application-card__icon--cache {
  --app-color: #dc3c32;
}

.application-card__icon--ai {
  --app-color: #4361ee;
}

.application-card__icon--runtime {
  --app-color: #7c6df2;
}

.application-card__icon--developer {
  --app-color: #1f2937;
}

.dark .application-card__icon--developer {
  --app-color: #d8dee9;
}

.application-card__version {
  display: flex;
  flex-wrap: wrap;
  gap: 3px;
  min-width: 0;
  margin-top: 3px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.application-card__version span {
  padding: 2px 5px;
  border-radius: 5px;
  background: var(--whaledeck-hover-bg);
}

.application-card__version .application-card__state-label {
  --state-label-color: var(--el-color-info);
  display: inline-flex;
  align-items: center;
  gap: 5px;
  color: var(--state-label-color);
  background: color-mix(in srgb, var(--state-label-color) 10%, transparent);
}

.application-card__state-label i {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
}

.application-card__state-label--running {
  --state-label-color: var(--el-color-success);
}

.application-card__state-label--stopped {
  --state-label-color: var(--el-color-danger);
}

.application-card__state-label--restarting {
  --state-label-color: var(--el-color-primary);
}

.application-card__actions {
  display: flex;
  align-items: center;
  flex: 0 0 auto;
  gap: 7px;
  min-height: 18px;
  margin-top: 2px;
}

.application-card__actions :deep(.el-button) {
  height: auto;
  padding: 3px 0;
  margin-left: 0;
  font-size: 12px;
}

.application-card__primary-action {
  min-width: 68px;
}

@keyframes application-state-spin {
  to {
    transform: rotate(1turn);
  }
}

@media (max-width: 1260px) {
  .resource-grid,
  .status-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .system-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .system-item:nth-child(n) {
    border-right: 1px solid var(--el-border-color-lighter);
    border-bottom: 1px solid var(--el-border-color-lighter);
  }

  .system-item:nth-child(2n) {
    border-right: 0;
  }

  .system-item:nth-last-child(-n + 2) {
    border-bottom: 0;
  }
}

@media (max-width: 1040px) {
  .telemetry-summary {
    flex-wrap: wrap;
  }

  .telemetry-legend {
    min-width: 100%;
    justify-content: flex-start;
    padding: 4px 2px;
  }
}

@media (max-width: 760px) {
  .portal-overview {
    grid-template-columns: 1fr;
  }
  .overview-panel__header,
  .monitoring-panel__header,
  .application-header {
    align-items: flex-start;
    flex-direction: column;
  }

  .monitoring-toolbar {
    width: 100%;
    align-items: stretch;
    flex-direction: column;
  }

  .monitoring-filter {
    width: 100%;
  }

  .telemetry-stat {
    min-width: calc(50% - 5px);
    flex: 1;
  }

  .application-grid {
    grid-template-columns: 1fr;
  }

  .system-grid {
    grid-template-columns: 1fr;
  }

  .system-item:nth-child(n) {
    border-right: 0;
    border-bottom: 1px solid var(--el-border-color-lighter);
  }

  .system-item:last-child {
    border-bottom: 0;
  }
}

@media (max-width: 560px) {
  .resource-grid,
  .status-grid {
    grid-template-columns: 1fr;
  }

  .resource-card {
    min-height: 126px;
  }

  .telemetry-stat {
    min-width: 100%;
  }
}

@media (prefers-reduced-motion: reduce) {
  .resource-card,
  .application-card {
    transition: none;
  }
}
</style>
