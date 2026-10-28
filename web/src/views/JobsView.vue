<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { apiRequest, apiUrl, createIdempotencyKey } from '@/services/apiClient'
import { stageSecret, subscribeToJob, type OperationPlan } from '@/services/operations'
import type { Job, JobEvent } from '@/services/contracts'

const jobs = ref<Job[]>([])
const loading = ref(false)
const selected = ref<Job | null>(null)
const events = ref<JobEvent[]>([])
const streamNotice = ref('')
const eventsTruncated = ref(false)
const drawerVisible = ref(false)
let timer: number | undefined
let unsubscribe: (() => void) | undefined
let openVersion = 0
const terminalStates = new Set(['Succeeded', 'Failed', 'Canceled', 'RolledBack'])
const canCancel = computed(() => selected.value && !terminalStates.has(selected.value.state))
const byteUpload = computed(
  () =>
    !!selected.value &&
    [
      'files.upload',
      'containers.files.upload',
      'containers.files.restore',
      'containers.files.mkdir',
    ].includes(selected.value.jobType),
)
const canRetry = computed(
  () =>
    selected.value &&
    !byteUpload.value &&
    ['Failed', 'Canceled', 'RolledBack'].includes(selected.value.state),
)
interface RetryContext {
  jobId: string
  jobType: string
  resourceId: string | null
  requiresInput: boolean
  inputKind: string | null
}
const retryContext = ref<RetryContext | null>(null)
const retryVisible = ref(false)
const retryBusy = ref(false)
const retryInput = ref('')
const retryInputTicket = ref<string | null>(null)
const retryPlan = ref<{ context: RetryContext; plan: OperationPlan | null } | null>(null)
const retryKey = ref('')
const retryError = ref('')
const diagnosticBundleId = computed(() => {
  if (selected.value?.jobType !== 'platform.diagnostic-bundle' || !selected.value.resultJson)
    return ''
  try {
    const value = JSON.parse(selected.value.resultJson) as { bundleId?: unknown }
    return typeof value.bundleId === 'string' && /^[a-f0-9]{32}$/.test(value.bundleId)
      ? value.bundleId
      : ''
  } catch {
    return ''
  }
})

function stateType(state: string) {
  if (state === 'Succeeded') return 'success'
  if (state === 'Failed') return 'danger'
  if (state === 'Canceled' || state === 'RolledBack') return 'warning'
  return 'primary'
}

async function load() {
  loading.value = true
  try {
    jobs.value = await apiRequest<Job[]>('jobs?take=100')
    if (selected.value)
      selected.value = jobs.value.find((item) => item.id === selected.value?.id) ?? selected.value
  } finally {
    loading.value = false
  }
}

async function openJob(job: Job) {
  const version = ++openVersion
  unsubscribe?.()
  events.value = []
  streamNotice.value = ''
  eventsTruncated.value = false
  const detail = await apiRequest<Job>(`jobs/${job.id}`)
  if (version !== openVersion) return
  selected.value = detail
  drawerVisible.value = true
  unsubscribe = subscribeToJob(job.id, {
    progress(event) {
      if (version !== openVersion || !drawerVisible.value) return
      streamNotice.value = ''
      events.value.push(event)
      if (events.value.length > 500) {
        eventsTruncated.value = true
        events.value = events.value.slice(-500)
      }
      if (selected.value) {
        selected.value.state = event.state
        selected.value.phase = event.phase
        selected.value.progressPercent = event.progressPercent
      }
      if (terminalStates.has(event.state)) void load().catch(() => {})
    },
    error: () => {
      if (version !== openVersion || !drawerVisible.value) return
      streamNotice.value = '事件连接中断，请刷新或重新打开详情核对状态。'
      void load().catch(() => {})
    },
  })
}

watch(drawerVisible, (visible) => {
  if (!visible) {
    openVersion++
    unsubscribe?.()
  }
})

async function cancelJob() {
  if (!selected.value) return
  await ElMessageBox.confirm(
    `请求取消任务 ${selected.value.id}？正在执行的安全阶段可能需要等待。`,
    '取消任务',
    { type: 'warning' },
  )
  await apiRequest<void>(`jobs/${selected.value.id}/cancel`, { method: 'POST' })
  ElMessage.success('已提交取消请求')
  await load()
}

async function openRetry() {
  if (!selected.value || !canRetry.value) return
  try {
    retryContext.value = await apiRequest<RetryContext>(`jobs/${selected.value.id}/retry-context`)
    retryInput.value = ''
    retryInputTicket.value = null
    retryPlan.value = null
    retryError.value = ''
    retryKey.value = createIdempotencyKey()
    retryVisible.value = true
  } catch {
    ElMessage.error('当前任务无法重试，请检查任务状态后刷新')
  }
}

async function previewRetry() {
  if (!retryContext.value) return
  retryBusy.value = true
  retryError.value = ''
  try {
    if (retryContext.value.requiresInput) {
      if (!retryInput.value) {
        ElMessage.warning('请重新输入本次操作所需的凭据或环境变量')
        return
      }
      retryInputTicket.value = (await stageSecret(retryInput.value)).token
      retryInput.value = ''
    }
    retryPlan.value = await apiRequest<{ context: RetryContext; plan: OperationPlan | null }>(
      `jobs/${retryContext.value.jobId}/retry/plan`,
      { method: 'POST', body: JSON.stringify({ inputTicket: retryInputTicket.value }) },
    )
  } catch {
    retryError.value = '预检未通过，请检查目标资源及操作条件后重新预检。'
    retryInputTicket.value = null
  } finally {
    retryBusy.value = false
  }
}

async function submitRetry() {
  if (!retryContext.value || !retryPlan.value) return
  retryBusy.value = true
  retryError.value = ''
  try {
    const job = await apiRequest<Job>(`jobs/${retryContext.value.jobId}/retry`, {
      method: 'POST',
      headers: { 'Idempotency-Key': retryKey.value },
      body: JSON.stringify({
        idempotencyKey: retryKey.value,
        inputTicket: retryInputTicket.value,
        planHash: retryPlan.value.plan?.planHash ?? null,
        confirmed: true,
      }),
    })
    retryVisible.value = false
    retryInputTicket.value = null
    ElMessage.success('已创建重试任务，原任务记录已保留')
    await load()
    await openJob(job)
  } catch {
    retryError.value = '提交失败，请检查资源状态。若计划或凭据已过期，请重新预检。'
    retryPlan.value = null
  } finally {
    retryBusy.value = false
  }
}

function clearRetryInput() {
  retryInput.value = ''
  retryInputTicket.value = null
}

function downloadDiagnosticBundle() {
  if (!diagnosticBundleId.value) return
  const anchor = document.createElement('a')
  anchor.href = apiUrl(`platform/diagnostics/${diagnosticBundleId.value}`)
  anchor.download = `whaledeck-diagnostics-${diagnosticBundleId.value}.tar.gz`
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
}

onMounted(() => {
  void load()
  timer = window.setInterval(load, 6000)
})
onBeforeUnmount(() => {
  openVersion++
  window.clearInterval(timer)
  unsubscribe?.()
})
</script>

<template>
  <div class="jobs-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">OPERATIONS</span>
        <h1>任务中心</h1>
        <p>查看任务进度与结果，取消进行中的任务，或重新预检并重试失败任务。</p>
      </div>
      <el-button :loading="loading" @click="load">刷新</el-button>
    </header>
    <section class="panel" v-loading="loading">
      <el-table :data="jobs" row-key="id" empty-text="暂无任务" @row-click="openJob">
        <el-table-column prop="jobType" label="任务" min-width="210" />
        <el-table-column label="状态" width="130"
          ><template #default="scope"
            ><el-tag :type="stateType(scope.row.state)" effect="light">{{
              scope.row.state
            }}</el-tag></template
          ></el-table-column
        >
        <el-table-column prop="phase" label="阶段" min-width="180" />
        <el-table-column label="进度" width="180"
          ><template #default="scope"
            ><el-progress
              :percentage="scope.row.progressPercent ?? 0"
              :status="scope.row.state === 'Failed' ? 'exception' : undefined" /></template
        ></el-table-column>
        <el-table-column prop="errorCode" label="错误码" min-width="180" />
        <el-table-column prop="createdAtUtc" label="创建时间" min-width="210" />
        <el-table-column label="操作" width="90" fixed="right"
          ><template #default="scope"
            ><el-button link type="primary" @click.stop="openJob(scope.row)"
              >详情</el-button
            ></template
          ></el-table-column
        >
      </el-table>
    </section>

    <el-drawer v-model="drawerVisible" title="任务详情" size="min(560px, 94vw)" destroy-on-close>
      <template v-if="selected">
        <el-descriptions :column="1" border>
          <el-descriptions-item label="任务 ID">{{ selected.id }}</el-descriptions-item>
          <el-descriptions-item label="类型">{{ selected.jobType }}</el-descriptions-item>
          <el-descriptions-item label="状态"
            ><el-tag :type="stateType(selected.state)">{{
              selected.state
            }}</el-tag></el-descriptions-item
          >
          <el-descriptions-item label="阶段">{{ selected.phase }}</el-descriptions-item>
          <el-descriptions-item label="错误码">{{
            selected.errorCode || '—'
          }}</el-descriptions-item>
          <el-descriptions-item label="结果">
            <pre class="job-result">{{ selected.resultJson || '任务成功后显示安全结果。' }}</pre>
            <el-button
              v-if="diagnosticBundleId"
              class="diagnostic-download"
              type="primary"
              @click="downloadDiagnosticBundle"
              >下载诊断包</el-button
            >
          </el-descriptions-item>
        </el-descriptions>
        <el-alert
          v-if="byteUpload"
          title="重新上传或恢复请回到文件页面，重新选择来源并确认基于当前版本的新计划。原任务不会自动重传或重复发布。"
          type="info"
          :closable="false"
        />
        <h3 class="job-events-title">实时事件</h3>
        <el-alert
          v-if="eventsTruncated"
          title="当前显示最近500条事件，完整历史保留在服务端。"
          type="info"
          :closable="false"
        />
        <el-alert v-if="streamNotice" :title="streamNotice" type="warning" :closable="false" />
        <el-timeline>
          <el-timeline-item
            v-for="event in events"
            :key="event.sequence"
            :timestamp="event.occurredAtUtc"
            placement="top"
            ><strong>{{ event.phase }}</strong>
            <div>{{ event.messageCode }} · {{ event.progressPercent ?? 0 }}%</div></el-timeline-item
          >
        </el-timeline>
      </template>
      <template #footer
        ><el-button v-if="canRetry" type="primary" @click="openRetry">重新预检并重试</el-button
        ><el-button v-if="canCancel" type="danger" plain @click="cancelJob"
          >取消任务</el-button
        ></template
      >
    </el-drawer>
    <el-dialog
      v-model="retryVisible"
      title="重试任务"
      width="min(560px, 94vw)"
      :close-on-click-modal="false"
      @closed="clearRetryInput"
    >
      <template v-if="retryContext">
        <el-alert v-if="retryError" :title="retryError" type="error" :closable="false" />
        <el-descriptions :column="1" border>
          <el-descriptions-item label="原任务">{{ retryContext.jobId }}</el-descriptions-item>
          <el-descriptions-item label="操作">{{ retryContext.jobType }}</el-descriptions-item>
          <el-descriptions-item label="资源">{{
            retryContext.resourceId || '新资源'
          }}</el-descriptions-item>
        </el-descriptions>
        <p>重试会新建任务，并按当前资源状态重新校验。请确认之前的操作结果和本次影响。</p>
        <el-form v-if="retryContext.requiresInput && !retryPlan" label-position="top">
          <el-form-item
            :label="
              retryContext.jobType === 'applications.configure'
                ? '重新输入应用配置补丁（JSON）'
                : retryContext.inputKind === 'environment'
                  ? '重新输入环境变量（JSON）'
                  : '重新输入密码'
            "
          >
            <el-input
              v-model="retryInput"
              :type="retryContext.inputKind === 'environment' ? 'textarea' : 'password'"
              :rows="5"
              :show-password="retryContext.inputKind !== 'environment'"
              autocomplete="new-password"
            />
          </el-form-item>
        </el-form>
        <template v-if="retryPlan">
          <el-alert title="预检完成，请确认后提交新的任务" type="info" :closable="false" />
          <ul v-if="retryPlan.plan?.changes.length">
            <li v-for="change in retryPlan.plan.changes" :key="change">{{ change }}</li>
          </ul>
          <el-alert
            v-for="warning in retryPlan.plan?.warnings"
            :key="warning"
            :title="warning"
            type="warning"
            :closable="false"
          />
        </template>
      </template>
      <template #footer>
        <el-button :disabled="retryBusy" @click="retryVisible = false">取消</el-button>
        <el-button v-if="!retryPlan" type="primary" :loading="retryBusy" @click="previewRetry"
          >重新预检</el-button
        >
        <el-button v-else type="primary" :loading="retryBusy" @click="submitRetry"
          >确认重试</el-button
        >
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.jobs-page {
  display: grid;
  gap: 18px;
}
.job-result {
  overflow-wrap: anywhere;
  margin: 0;
  white-space: pre-wrap;
}
.job-events-title {
  margin: 24px 0 16px;
  font-size: 16px;
}
.diagnostic-download {
  margin-top: 12px;
}
</style>
