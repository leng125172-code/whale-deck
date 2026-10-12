<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { enqueueOperation, planOperation } from '@/services/operations'
import type { BackupRecord, Job } from '@/services/contracts'

const props = defineProps<{ modelValue: boolean; resourceId: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()
const records = ref<BackupRecord[]>([])
const job = ref<Job | null>(null)
const consistency = ref<'quiesced' | 'live'>('quiesced')
const retentionDays = ref(14)
const retentionCount = ref(14)
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const denied = ref(false)
const terminal = new Set(['Succeeded', 'Failed', 'Canceled', 'RolledBack'])
const pending = computed(() => !!job.value && !terminal.has(job.value.state))
const slug = computed(() => props.resourceId.replace(/^application:/, ''))
let epoch = 0
let loadGeneration = 0
let timer: ReturnType<typeof setTimeout> | undefined
const authorized = () => authSession.user.value?.isAdministrator === true && !denied.value
const canManage = computed(authorized)
const stop = () => {
  clearTimeout(timer)
  timer = undefined
}
function reset() {
  epoch++
  loadGeneration++
  stop()
  records.value = []
  job.value = null
  loading.value = false
  busy.value = false
  error.value = ''
  denied.value = false
}
function fail(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    reset()
    denied.value = true
  }
  error.value = reason instanceof Error ? reason.message : '备份服务暂不可用，请稍后重试。'
}
function label(state: string) {
  return (
    (
      {
        Running: '执行中',
        Queued: '排队中',
        Succeeded: '已完成',
        Failed: '失败',
        Canceled: '已取消',
        Expired: '已清理',
      } as Record<string, string>
    )[state] || state
  )
}
function size(bytes: number | null) {
  if (bytes === null) return '—'
  return bytes >= 1024 ** 3
    ? `${(bytes / 1024 ** 3).toFixed(2)} GB`
    : `${(bytes / 1024 ** 2).toFixed(2)} MB`
}
const date = (value: string | null) =>
  value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—'
async function load() {
  if (!props.modelValue || !authorized()) return
  const current = ++loadGeneration
  loading.value = true
  error.value = ''
  try {
    const result = await apiRequest<BackupRecord[]>(
      `backups/records?take=100&instanceResourceId=${encodeURIComponent(props.resourceId)}`,
    )
    if (current !== loadGeneration || !props.modelValue) return
    if (!Array.isArray(result)) throw new Error('备份历史响应格式无效。')
    records.value = result
    const running = result.find((item) => ['Running', 'Queued'].includes(item.status))
    if (running && !pending.value) {
      const active = await apiRequest<Job>(`jobs/${running.jobId}`)
      if (current === loadGeneration && props.modelValue) job.value = active
    }
    schedule()
  } catch (reason) {
    if (current === loadGeneration) fail(reason)
  } finally {
    if (current === loadGeneration) loading.value = false
  }
}
function schedule() {
  stop()
  if (!props.modelValue || !pending.value || !authorized() || document.hidden) return
  timer = setTimeout(() => void poll(), 6000)
}
async function poll() {
  if (!job.value || !props.modelValue || !authorized()) return
  const current = epoch
  const id = job.value.id
  try {
    const result = await apiRequest<Job>(`jobs/${id}`)
    if (current !== epoch || job.value?.id !== id) return
    job.value = result
    error.value = ''
    if (terminal.has(result.state)) await load()
    else schedule()
  } catch (reason) {
    if (current !== epoch) return
    fail(reason)
    if (!(reason instanceof ApiError && [401, 403].includes(reason.status))) schedule()
  }
}
async function submit(
  action: 'backup' | 'verify-backup' | 'cleanup-backups',
  record?: BackupRecord,
) {
  if (busy.value || pending.value || !authorized()) return
  const current = epoch
  const resource = props.resourceId
  busy.value = true
  error.value = ''
  const parameters: Record<string, string> =
    action === 'backup'
      ? { consistency: consistency.value }
      : action === 'verify-backup'
        ? { backupId: record!.jobId.replace(/-/g, '') }
        : {
            retentionDays: String(retentionDays.value),
            retentionCount: String(retentionCount.value),
          }
  try {
    const plan = await planOperation('applications', action, resource, parameters)
    if (current !== epoch) return
    await ElMessageBox.confirm(
      [...plan.changes, ...plan.warnings].join('\n'),
      action === 'backup'
        ? '确认应用备份'
        : action === 'verify-backup'
          ? '确认校验备份'
          : '确认清理旧备份',
      {
        type: action === 'cleanup-backups' ? 'error' : 'warning',
        confirmButtonText: '确认提交',
        cancelButtonText: '取消',
      },
    )
    if (current !== epoch || !authorized()) return
    const result = await enqueueOperation(
      'applications',
      action,
      resource,
      parameters,
      true,
      plan.planHash,
    )
    if (current !== epoch) return
    job.value = result
    ElMessage.success('任务已提交，关闭窗口不会中止任务。')
    await load()
  } catch (reason) {
    if (current === epoch && reason !== 'cancel' && reason !== 'close') fail(reason)
  } finally {
    if (current === epoch) busy.value = false
  }
}
async function cancel() {
  if (!pending.value || busy.value || !authorized()) return
  const current = epoch
  const id = job.value!.id
  try {
    await ElMessageBox.confirm('请求取消后，需要等待宿主服务完成收尾与容器恢复。', '确认取消任务', {
      type: 'warning',
      confirmButtonText: '请求取消',
      cancelButtonText: '继续执行',
    })
    if (current !== epoch || !authorized()) return
    busy.value = true
    await apiRequest(`jobs/${id}/cancel`, { method: 'POST', body: '{}' })
    if (current === epoch) {
      ElMessage.info('已请求取消，正在等待任务收尾。')
      await poll()
    }
  } catch (reason) {
    if (current === epoch && reason !== 'cancel' && reason !== 'close') fail(reason)
  } finally {
    if (current === epoch) busy.value = false
  }
}
function visibility() {
  if (document.hidden) stop()
  else if (props.modelValue) void poll()
}
document.addEventListener('visibilitychange', visibility)
watch(
  () => [
    props.modelValue,
    props.resourceId,
    authSession.user.value?.subject,
    authSession.user.value?.isAdministrator,
  ],
  () => {
    reset()
    if (props.modelValue && authorized()) void load()
  },
  { immediate: true },
)
onUnmounted(() => {
  reset()
  document.removeEventListener('visibilitychange', visibility)
})
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="应用备份"
    top="6vh"
    width="min(850px, calc(100vw - 24px))"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="application-backups">
      <p v-if="canManage" class="backup-resource">{{ slug }}</p>
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <el-alert
        title="范围：配置、应用 data 目录、独占本地卷。排除可写层、tmpfs 和外部服务；不提供恢复或下载。"
        type="info"
        :closable="false"
      />
      <p class="backup-notice">
        暂停归档会临时暂停应用，完成或取消后恢复。它不保证数据库事务一致性；数据库请使用原生备份。
      </p>
      <div class="backup-toolbar">
        <el-select
          v-model="consistency"
          aria-label="归档一致性"
          :disabled="busy || pending || !canManage"
        >
          <el-option label="暂停归档 · 临时暂停应用" value="quiesced" /><el-option
            label="实时归档 · 文件可能变化"
            value="live"
          />
        </el-select>
        <el-button
          type="primary"
          :loading="busy"
          :disabled="pending || !canManage"
          @click="submit('backup')"
          >立即备份</el-button
        >
        <el-button :loading="loading" :disabled="!canManage" @click="load">刷新历史</el-button>
      </div>
      <section v-if="job" class="backup-job" aria-live="polite">
        <strong>{{ label(job.state) }} · {{ job.id.slice(0, 8) }}</strong>
        <span>{{ job.phase }}</span
        ><small v-if="job.errorCode">{{ job.errorCode }}</small>
        <el-progress v-if="job.progressPercent !== null" :percentage="job.progressPercent" />
        <el-button v-if="pending" link type="danger" :disabled="busy" @click="cancel"
          >请求取消</el-button
        >
      </section>
      <div class="backup-retention">
        <span>旧备份清理</span>
        <el-input-number v-model="retentionDays" aria-label="保留天数" :min="1" :max="3650" />
        <span>天 /</span
        ><el-input-number v-model="retentionCount" aria-label="保留份数" :min="1" :max="365" /><span
          >份</span
        >
        <el-button :disabled="busy || pending || !canManage" @click="submit('cleanup-backups')"
          >预检并清理</el-button
        >
      </div>
      <small class="backup-notice"
        >清理超出天数或份数的旧归档，但至少保留最新一份；应用运行数据不受影响。</small
      >
      <h3>最近 100 条备份记录</h3>
      <el-empty v-if="!loading && !records.length" description="暂无备份记录" />
      <div v-loading="loading" class="archive-list">
        <article v-for="record in records" :key="record.id" class="archive-row">
          <div>
            <strong>{{ date(record.startedAtUtc) }}</strong
            ><el-tag
              size="small"
              :type="
                record.status === 'Failed'
                  ? 'danger'
                  : record.status === 'Succeeded'
                    ? 'success'
                    : 'info'
              "
              >{{ label(record.status) }}</el-tag
            >
          </div>
          <small
            >{{ size(record.sizeBytes) }} · {{ record.policyId ? '自动策略' : '手动备份' }} ·
            校验：{{ date(record.verifiedAtUtc) }}</small
          >
          <small v-if="record.checksum" class="archive-checksum"
            >SHA256：{{ record.checksum }}</small
          >
          <small v-if="record.errorCode">{{ record.errorCode }}</small>
          <el-button
            link
            type="primary"
            :disabled="record.status !== 'Succeeded' || busy || pending"
            @click="submit('verify-backup', record)"
            >校验此备份</el-button
          >
        </article>
      </div>
    </div>
    <template #footer
      ><el-button @click="emit('update:modelValue', false)">关闭</el-button></template
    >
  </el-dialog>
</template>

<style scoped>
.application-backups {
  display: grid;
  gap: 14px;
  min-width: 0;
  max-height: min(680px, calc(100dvh - 220px));
  overflow: auto;
  padding-right: 4px;
}
.application-backups > * {
  min-width: 0;
}
.backup-resource {
  margin: 0;
  color: var(--el-color-primary);
  overflow-wrap: anywhere;
}
.backup-notice {
  margin: 0;
  font-size: 12px;
  line-height: 1.8;
  color: var(--el-text-color-secondary);
}
.backup-toolbar,
.backup-retention,
.archive-row > div {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}
.backup-toolbar .el-select {
  width: 270px;
  max-width: 100%;
}
.backup-toolbar .el-button,
.backup-retention .el-button {
  margin-left: 0;
}
.backup-retention .el-input-number {
  width: 118px;
}
.backup-job,
.archive-row {
  display: grid;
  gap: 8px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 12px;
}
.archive-list {
  display: grid;
  gap: 10px;
  min-height: 80px;
}
.archive-row small,
.backup-job span,
.backup-job small {
  font-size: 12px;
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
}
.archive-checksum {
  word-break: break-all;
}
.archive-row > .el-button {
  justify-self: start;
}
.application-backups h3 {
  margin: 4px 0 0;
  font-size: 14px;
}
@media (max-width: 600px) {
  .backup-toolbar .el-select {
    width: 100%;
  }
  .application-backups {
    max-height: calc(100dvh - 190px);
  }
}
</style>
