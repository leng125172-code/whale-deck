<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { BellFilled, Edit, RefreshRight } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest } from '@/services/apiClient'
import type { AlertEvent, AlertEventHistory, AlertRule } from '@/services/contracts'
import { authSession } from '@/services/authSession'

const events = ref<AlertEvent[]>([])
const history = ref<AlertEventHistory[]>([])
const rules = ref<AlertRule[]>([])
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const traceId = ref('')
const activeTab = ref('events')
const dialogVisible = ref(false)
const editing = ref<AlertRule | null>(null)
const compact = ref(window.innerWidth <= 680)
function updateViewport() {
  compact.value = window.innerWidth <= 680
}
let generation = 0
const denied = ref(false)
const canManage = computed(() => authSession.user.value?.isAdministrator === true && !denied.value)
function clearPrivate() {
  generation++
  denied.value = true
  events.value = []
  history.value = []
  rules.value = []
  dialogVisible.value = false
  editing.value = null
  loading.value = false
}
function readableState(state: string) {
  return (
    (
      {
        Active: '未处置',
        Acknowledged: '已确认',
        Silenced: '已静默',
        Recovered: '已恢复',
        SeverityChanged: '级别变更',
        SilenceExpired: '静默到期',
      } as Record<string, string>
    )[state] || state
  )
}
function capacityText(item: AlertEvent) {
  const value = item.capacity
  if (!value) return ''
  if (!value.available || value.usedPercent == null) return '容量未知 · 不会因此解除风险'
  const free = value.freeBytes == null ? '—' : `${(value.freeBytes / 1024 ** 3).toFixed(1)} GiB`
  return `HDD 已用 ${value.usedPercent.toFixed(1)}% · 可用 ${free}`
}
const form = reactive({
  ruleType: 'CpuUtilization',
  resourceSelectorJson: '{}',
  thresholdJson: '{\n  "operator": ">=",\n  "value": 90\n}',
  evaluationWindowSeconds: 60,
  severity: 'Warning' as AlertRule['severity'],
  isEnabled: true,
})

const activeCount = computed(() => events.value.filter((item) => item.state !== 'Recovered').length)
const ruleNames: Record<string, string> = {
  CpuUtilization: 'CPU 使用率',
  MemoryUtilization: '内存使用率',
  DiskUtilization: '磁盘使用率',
  ContainerHealth: '容器健康',
  BackupFailure: '备份失败',
  AgentOffline: 'Agent 离线',
}

function severityType(value: AlertEvent['severity'] | AlertRule['severity']) {
  return value === 'Critical' ? 'danger' : value === 'Warning' ? 'warning' : 'info'
}

function formatDate(value: string | null) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—'
}
function reportWriteError(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) clearPrivate()
  error.value = reason instanceof Error ? reason.message : '告警操作失败'
  traceId.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
}

function ruleLabel(ruleId: string) {
  const rule = rules.value.find((item) => item.id === ruleId)
  return rule
    ? rule.ruleType === 'BackupStorageCapacity'
      ? 'HDD 备份容量 · 跟随备份策略'
      : ruleNames[rule.ruleType] || rule.ruleType
    : ruleId
}

async function load() {
  if (!canManage.value) return
  const version = ++generation
  loading.value = true
  error.value = ''
  try {
    const values = await Promise.all([
      apiRequest<AlertEvent[]>('alerts?includeRecovered=true'),
      apiRequest<AlertRule[]>('alerts/rules'),
      apiRequest<AlertEventHistory[]>('alerts/history?take=500'),
    ])
    if (version !== generation || !canManage.value) return
    ;[events.value, rules.value, history.value] = values
  } catch (reason) {
    if (version !== generation) return
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) clearPrivate()
    error.value = reason instanceof Error ? reason.message : '读取告警失败'
    traceId.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
  } finally {
    if (version === generation) loading.value = false
  }
}

function openRule(item?: AlertRule) {
  if (!canManage.value || item?.ruleType === 'BackupStorageCapacity') return
  editing.value = item || null
  Object.assign(
    form,
    item
      ? {
          ruleType: item.ruleType,
          resourceSelectorJson: JSON.stringify(JSON.parse(item.resourceSelectorJson), null, 2),
          thresholdJson: JSON.stringify(JSON.parse(item.thresholdJson), null, 2),
          evaluationWindowSeconds: item.evaluationWindowSeconds,
          severity: item.severity,
          isEnabled: item.isEnabled,
        }
      : {
          ruleType: 'CpuUtilization',
          resourceSelectorJson: '{}',
          thresholdJson: '{\n  "operator": ">=",\n  "value": 90\n}',
          evaluationWindowSeconds: 60,
          severity: 'Warning',
          isEnabled: true,
        },
  )
  dialogVisible.value = true
}

async function saveRule() {
  if (!canManage.value) return
  const version = generation
  try {
    JSON.parse(form.resourceSelectorJson)
    JSON.parse(form.thresholdJson)
  } catch {
    ElMessage.error('规则 JSON 格式无效')
    return
  }
  saving.value = true
  try {
    await apiRequest<AlertRule>('alerts/rules', {
      method: 'POST',
      body: JSON.stringify({
        id: editing.value?.id || null,
        ...form,
        expectedVersion: editing.value?.version ?? null,
      }),
    })
    if (version !== generation || !canManage.value) return
    dialogVisible.value = false
    ElMessage.success('告警规则已保存')
    await load()
  } catch (reason) {
    if (version === generation) reportWriteError(reason)
  } finally {
    saving.value = false
  }
}

async function acknowledge(item: AlertEvent) {
  if (!canManage.value || item.state === 'Recovered') return
  const version = generation
  try {
    await apiRequest<AlertEvent>(`alerts/${item.id}/acknowledge`, {
      method: 'POST',
      body: '{}',
    })
    if (version !== generation || !canManage.value) return
    ElMessage.success('告警已确认')
    await load()
  } catch (reason) {
    reportWriteError(reason)
  }
}

async function silence(item: AlertEvent) {
  if (!canManage.value || item.state === 'Recovered') return
  const version = generation
  const result = await ElMessageBox.prompt('输入静默小时数（1–720）', '静默告警', {
    inputValue: '4',
    inputPattern: /^(?:[1-9]|[1-9]\d|[1-6]\d{2}|7[01]\d|720)$/,
    inputErrorMessage: '请输入 1 到 720 之间的整数',
    confirmButtonText: '开始静默',
    cancelButtonText: '取消',
  }).catch(() => null)
  if (!result) return
  if (version !== generation || !canManage.value) return
  const untilUtc = new Date(Date.now() + Number(result.value) * 60 * 60 * 1000).toISOString()
  try {
    await apiRequest<AlertEvent>(`alerts/${item.id}/silence`, {
      method: 'POST',
      body: JSON.stringify({ untilUtc }),
    })
    if (version !== generation || !canManage.value) return
    ElMessage.success(`已静默至 ${formatDate(untilUtc)}`)
    await load()
  } catch (reason) {
    reportWriteError(reason)
  }
}

onMounted(() => {
  window.addEventListener('resize', updateViewport)
  void load()
})
watch(
  () => [authSession.user.value?.subject, authSession.user.value?.isAdministrator],
  clearPrivate,
)
onUnmounted(() => {
  window.removeEventListener('resize', updateViewport)
  generation++
})
</script>

<template>
  <div class="alerts-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">OBSERVABILITY</span>
        <h1>告警</h1>
        <p>当前有 {{ activeCount }} 条未恢复告警；确认和静默操作都会进入审计日志。</p>
      </div>
      <div class="page-heading__actions">
        <el-button :loading="loading" :disabled="!canManage" @click="load"
          ><el-icon><RefreshRight /></el-icon>刷新</el-button
        ><el-button type="primary" :disabled="!canManage" @click="openRule()">新增规则</el-button>
      </div>
    </header>
    <el-alert
      v-if="error"
      :title="error"
      type="error"
      :description="traceId ? `追踪号：${traceId}` : undefined"
      show-icon
      :closable="false"
    />
    <section class="panel alerts-panel" v-loading="loading">
      <el-tabs v-model="activeTab">
        <el-tab-pane name="events">
          <template #label
            ><el-icon><BellFilled /></el-icon>&nbsp;告警事件</template
          >
          <el-empty v-if="!events.length && !loading" description="暂无告警事件" />
          <div v-else-if="compact" class="alert-cards">
            <article v-for="item in events" :key="item.id" class="alert-card">
              <header>
                <strong>{{ item.capacity ? 'HDD 备份容量' : item.summaryCode }}</strong
                ><el-tag :type="severityType(item.severity)">{{
                  item.severity === 'Critical'
                    ? '严重'
                    : item.severity === 'Warning'
                      ? '预警'
                      : '信息'
                }}</el-tag>
              </header>
              <p v-if="item.capacity" class="capacity-detail">{{ capacityText(item) }}</p>
              <p v-if="item.capacity">
                预警 {{ item.capacity.warningPercent }}% / 临界 {{ item.capacity.criticalPercent }}%
              </p>
              <p>{{ readableState(item.state) }} · {{ item.occurrenceCount }} 次</p>
              <small>{{ formatDate(item.lastOccurredAtUtc) }}</small>
              <small v-if="item.silencedUntilUtc"
                >静默至 {{ formatDate(item.silencedUntilUtc) }}</small
              >
              <div class="alert-card__actions">
                <el-button
                  link
                  type="primary"
                  :disabled="
                    !canManage || item.state === 'Recovered' || Boolean(item.acknowledgedAtUtc)
                  "
                  @click="acknowledge(item)"
                  >确认</el-button
                >
                <el-button
                  link
                  type="warning"
                  :disabled="!canManage || item.state === 'Recovered'"
                  @click="silence(item)"
                  >静默</el-button
                >
              </div>
            </article>
          </div>
          <el-table v-else :data="events" row-key="id">
            <el-table-column label="级别" width="100">
              <template #default="scope">
                <el-tag :type="severityType(scope.row.severity)" effect="light">{{
                  scope.row.severity
                }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="告警" min-width="220">
              <template #default="scope">
                <div class="event-title">
                  <strong>{{ scope.row.summaryCode }}</strong
                  ><small>{{ ruleLabel(scope.row.ruleId) }}</small>
                  <small v-if="scope.row.capacity" class="capacity-detail">{{
                    capacityText(scope.row)
                  }}</small>
                  <small v-if="scope.row.capacity"
                    >预警 {{ scope.row.capacity.warningPercent }}% / 临界
                    {{ scope.row.capacity.criticalPercent }}%</small
                  >
                </div>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="110"
              ><template #default="scope">{{
                readableState(scope.row.state)
              }}</template></el-table-column
            >
            <el-table-column prop="occurrenceCount" label="次数" width="80" />
            <el-table-column label="最近发生" min-width="180"
              ><template #default="scope">{{
                formatDate(scope.row.lastOccurredAtUtc)
              }}</template></el-table-column
            >
            <el-table-column label="处置" min-width="190"
              ><template #default="scope"
                ><span v-if="scope.row.acknowledgedAtUtc"
                  >已确认 · {{ formatDate(scope.row.acknowledgedAtUtc) }}</span
                ><span v-else>未确认</span
                ><small v-if="scope.row.silencedUntilUtc"
                  >静默至 {{ formatDate(scope.row.silencedUntilUtc) }}</small
                ></template
              ></el-table-column
            >
            <el-table-column label="操作" width="150" fixed="right"
              ><template #default="scope"
                ><el-button
                  link
                  type="primary"
                  :disabled="
                    !canManage ||
                    scope.row.state === 'Recovered' ||
                    Boolean(scope.row.acknowledgedAtUtc)
                  "
                  @click="acknowledge(scope.row)"
                  >确认</el-button
                ><el-button
                  link
                  type="warning"
                  :disabled="!canManage || scope.row.state === 'Recovered'"
                  @click="silence(scope.row)"
                  >静默</el-button
                ></template
              ></el-table-column
            >
          </el-table>
        </el-tab-pane>
        <el-tab-pane name="history" label="状态历史">
          <el-empty v-if="!history.length && !loading" description="暂无状态变更记录" />
          <el-table v-else :data="history" row-key="id">
            <el-table-column label="告警" min-width="260" show-overflow-tooltip>
              <template #default="scope">{{
                events.find((item) => item.id === scope.row.alertEventId)?.summaryCode ||
                scope.row.alertEventId
              }}</template>
            </el-table-column>
            <el-table-column label="状态" width="130"
              ><template #default="scope">{{
                readableState(scope.row.state)
              }}</template></el-table-column
            >
            <el-table-column
              prop="actorSubject"
              label="操作者"
              min-width="220"
              show-overflow-tooltip
            />
            <el-table-column label="发生时间" min-width="190"
              ><template #default="scope">{{
                formatDate(scope.row.occurredAtUtc)
              }}</template></el-table-column
            >
          </el-table>
        </el-tab-pane>
        <el-tab-pane name="rules" label="告警规则">
          <el-empty v-if="!rules.length && !loading" description="暂无告警规则" />
          <el-table v-else :data="rules" row-key="id">
            <el-table-column label="类型" min-width="180"
              ><template #default="scope"
                ><strong>{{
                  scope.row.ruleType === 'BackupStorageCapacity'
                    ? 'HDD 备份容量'
                    : ruleNames[scope.row.ruleType] || scope.row.ruleType
                }}</strong></template
              ></el-table-column
            >
            <el-table-column label="级别" width="100"
              ><template #default="scope"
                ><el-tag :type="severityType(scope.row.severity)">{{
                  scope.row.severity
                }}</el-tag></template
              ></el-table-column
            >
            <el-table-column prop="evaluationWindowSeconds" label="判断窗口（秒）" width="150" />
            <el-table-column label="状态" width="100"
              ><template #default="scope"
                ><el-tag :type="scope.row.isEnabled ? 'success' : 'info'">{{
                  scope.row.isEnabled ? '启用' : '停用'
                }}</el-tag></template
              ></el-table-column
            >
            <el-table-column label="操作" width="100"
              ><template #default="scope"
                ><span v-if="scope.row.ruleType === 'BackupStorageCapacity'">跟随备份策略</span>
                <el-button
                  v-else
                  link
                  type="primary"
                  :disabled="!canManage"
                  @click="openRule(scope.row)"
                  ><el-icon><Edit /></el-icon>编辑</el-button
                ></template
              ></el-table-column
            >
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </section>
    <el-dialog
      v-model="dialogVisible"
      :title="editing ? '编辑告警规则' : '新增告警规则'"
      width="min(680px, calc(100vw - 32px))"
    >
      <el-form label-position="top">
        <div class="form-grid">
          <el-form-item label="指标类型"
            ><el-select v-model="form.ruleType"
              ><el-option
                v-for="(label, value) in ruleNames"
                :key="value"
                :label="label"
                :value="value" /></el-select
          ></el-form-item>
          <el-form-item label="严重程度"
            ><el-select v-model="form.severity"
              ><el-option label="信息" value="Info" /><el-option
                label="警告"
                value="Warning" /><el-option label="严重" value="Critical" /></el-select
          ></el-form-item>
          <el-form-item label="连续判断窗口（秒）"
            ><el-input-number v-model="form.evaluationWindowSeconds" :min="12" :max="86400"
          /></el-form-item>
          <el-form-item label="启用"><el-switch v-model="form.isEnabled" /></el-form-item>
        </div>
        <el-form-item label="资源筛选 JSON"
          ><el-input v-model="form.resourceSelectorJson" type="textarea" :rows="4"
        /></el-form-item>
        <el-form-item label="阈值 JSON"
          ><el-input v-model="form.thresholdJson" type="textarea" :rows="5"
        /></el-form-item>
      </el-form>
      <template #footer
        ><el-button @click="dialogVisible = false">取消</el-button
        ><el-button type="primary" :loading="saving" @click="saveRule">保存</el-button></template
      >
    </el-dialog>
  </div>
</template>

<style scoped>
.alerts-page {
  display: grid;
  gap: 18px;
}
.alerts-panel {
  min-height: 360px;
  padding: 12px 16px;
}
.event-title {
  display: grid;
  gap: 3px;
}
.event-title small,
td small {
  display: block;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0 14px;
}
.alert-cards {
  display: grid;
  gap: 12px;
}
.alert-card {
  display: grid;
  gap: 8px;
  min-width: 0;
  border: 1px solid var(--el-border-color-light);
  border-radius: 10px;
  padding: 12px;
  overflow-wrap: anywhere;
}
.alert-card header {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  justify-content: space-between;
}
.alert-card p {
  margin: 0;
  font-size: 13px;
}
.alert-card small {
  color: var(--el-text-color-secondary);
}
.alert-card__actions {
  display: flex;
  gap: 20px;
}
@media (max-width: 680px) {
  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>
