<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElCard, ElCheckbox } from 'element-plus'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { workspaceError, type Workspace } from '@/services/developmentWorkspaces'
import { workspaceArchives as api, type WorkspaceArchive } from '@/services/workspaceArchives'

const props = defineProps<{ workspace: Workspace }>()
const emit = defineEmits<{ pending: [value: boolean] }>()
const operation = ref<WorkspaceArchive | null>(null),
  confirmed = ref(false),
  busy = ref(false),
  denied = ref(false),
  uncertain = ref(true),
  error = ref('')
let key: string | undefined, lifetime: AbortController
const pending = computed(
  () => uncertain.value || (!!operation.value && !operation.value.completedAtUtc),
)
const labels: Record<string, string> = {
  Plan: '正在核对归档计划',
  AwaitingConfirmation: '等待确认归档',
  Capture: '正在复制到 HDD',
  Verify: '正在独立校验归档',
  Archived: '归档已校验',
  Canceled: '归档已取消',
  Interrupted: '归档执行中断',
  Rejected: '归档条件不满足',
  Stopped: '执行已停止',
}
const plan = computed(() => {
  if (!operation.value?.planJson) return null
  try {
    const value = JSON.parse(operation.value.planJson) as {
      planSha256: string
      entryCount: number
      totalBytes: number
    }
    return /^[a-f0-9]{64}$/.test(value.planSha256) &&
      Number.isSafeInteger(value.entryCount) &&
      value.entryCount >= 0 &&
      Number.isSafeInteger(value.totalBytes) &&
      value.totalBytes >= 0
      ? value
      : null
  } catch {
    return null
  }
})
function verifiedReceipt(value: WorkspaceArchive | null) {
  if (value?.state !== 'Succeeded' || !value.captureJson || !value.verifyJson) return null
  try {
    const capture = JSON.parse(value.captureJson) as Record<string, unknown>
    const verify = JSON.parse(value.verifyJson) as Record<string, unknown>
    return ['receiptSha256', 'archiveSha256', 'archiveIndexSha256'].every(
      (name) =>
        typeof capture[name] === 'string' &&
        /^[a-f0-9]{64}$/.test(capture[name]) &&
        capture[name] === verify[name],
    ) &&
      capture.operationId === value.archiveId &&
      verify.operationId === value.archiveId &&
      Number.isSafeInteger(capture.archiveBytes) &&
      (capture.archiveBytes as number) > 0 &&
      capture.archiveBytes === verify.archiveBytes
      ? {
          receiptSha256: String(verify.receiptSha256),
          archiveSha256: String(verify.archiveSha256),
          archiveBytes: Number(verify.archiveBytes),
        }
      : null
  } catch {
    return null
  }
}
const receipt = computed(() => verifiedReceipt(operation.value))
function accept(value: WorkspaceArchive | null) {
  if (
    value &&
    (value.workspaceId !== props.workspace.id ||
      !value.retainsSource ||
      !value.reservesBudget ||
      (value.state === 'Succeeded' && (!value.completedAtUtc || !verifiedReceipt(value))))
  )
    throw new Error('WORKSPACE_ARCHIVE_BINDING_INVALID')
  if (value?.archiveId !== operation.value?.archiveId || value?.completedAtUtc)
    confirmed.value = false
  operation.value = value
  uncertain.value = false
  emit('pending', !!value && !value.completedAtUtc)
}
async function perform(action: (signal: AbortSignal) => Promise<void>) {
  if (busy.value) return
  const signal = lifetime.signal
  busy.value = true
  error.value = ''
  try {
    await action(signal)
  } catch (failure) {
    if (!signal.aborted) {
      if (failure instanceof ApiError && [401, 403].includes(failure.status)) {
        denied.value = true
        operation.value = null
        confirmed.value = false
        key = undefined
        uncertain.value = true
        emit('pending', true)
        error.value = '登录或权限已变化，已停止自动刷新。请重新登录或恢复权限后刷新归档状态。'
      } else error.value = workspaceError(failure)
    }
  } finally {
    if (!signal.aborted) busy.value = false
  }
}
async function observe() {
  await perform(async (signal) => {
    const value = await api.current(props.workspace.id, signal)
    if (!signal.aborted) {
      accept(value)
      denied.value = false
    }
  })
}
watch(
  () => props.workspace.id,
  (_, previous, cleanup) => {
    lifetime = new AbortController()
    busy.value = false
    denied.value = false
    uncertain.value = true
    emit('pending', true)
    if (previous) {
      operation.value = null
      key = undefined
      confirmed.value = false
      error.value = ''
    }
    const controller = lifetime
    const timer = window.setInterval(() => {
      if (document.visibilityState === 'visible' && pending.value && !denied.value && !busy.value)
        void observe()
    }, 3000)
    cleanup(() => {
      controller.abort()
      window.clearInterval(timer)
    })
    void observe()
  },
  { immediate: true },
)
function begin() {
  void perform(async (signal) => {
    if (operation.value?.completedAtUtc) key = undefined
    key ??= createIdempotencyKey()
    uncertain.value = true
    emit('pending', true)
    const value = await api.begin(
      props.workspace.id,
      props.workspace.lifecycleVersion ?? 0,
      key,
      signal,
    )
    if (!signal.aborted) {
      accept(value)
      key = undefined
    }
  })
}
function confirm() {
  const value = operation.value,
    selected = plan.value
  if (!value?.awaitingConfirmation || !selected || !confirmed.value) return
  void perform(async (signal) => {
    const result = await api.confirm(
      props.workspace.id,
      value.archiveId,
      selected.planSha256,
      signal,
    )
    if (!signal.aborted) {
      accept(result)
      confirmed.value = false
    }
  })
}
function change(action: 'cancel' | 'authorize') {
  const value = operation.value
  if (!value || value.completedAtUtc) return
  void perform(async (signal) => {
    const result = await api[action](props.workspace.id, value.archiveId, signal)
    if (!signal.aborted) accept(result)
  })
}
</script>

<template>
  <el-card shadow="never" class="workspace-archive" role="region" aria-label="开发空间 HDD 归档">
    <template #header><strong>HDD 归档</strong></template>
    <p>先核对计划，再确认复制和独立校验。源码、历史和磁盘配额继续保留，归档结束前不能恢复空间。</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div class="archive-actions">
      <el-button :disabled="busy || pending || denied" @click="begin">核对 HDD 归档计划</el-button>
      <el-button :disabled="busy" @click="observe">刷新归档状态</el-button>
    </div>
    <template v-if="operation">
      <p role="status">{{ labels[operation.phase] ?? operation.phase }}</p>
      <p>
        归档 <code>{{ operation.archiveId }}</code>
      </p>
      <p v-if="plan">{{ plan.entryCount }} 项，{{ plan.totalBytes }} 字节源码。</p>
      <template v-if="operation.awaitingConfirmation && !operation.cancelRequested && plan">
        <p>
          <code>{{ plan.planSha256 }}</code>
        </p>
        <el-checkbox v-model="confirmed" :disabled="busy"
          >我已核对计划，确认复制到 HDD，保留原源码与配额</el-checkbox
        >
        <div class="archive-actions">
          <el-button type="primary" :disabled="busy || !confirmed" @click="confirm"
            >确认 HDD 归档</el-button
          >
        </div>
      </template>
      <el-alert
        v-if="operation.code"
        type="warning"
        :closable="false"
        show-icon
        :title="
          operation.authorizationRequired
            ? '登录或权限已变化。尚未执行的任务可重新授权；执行过的任务将精确停止并保留原回执。'
            : '尚未确认归档结果。原任务和数据保留，请刷新查看。'
        "
      />
      <template v-if="receipt">
        <p>HDD 副本已独立校验；原源码与资源预算继续保留。</p>
        <dl>
          <dt>归档大小</dt>
          <dd>{{ receipt.archiveBytes }} 字节</dd>
          <dt>归档 SHA-256</dt>
          <dd>
            <code>{{ receipt.archiveSha256 }}</code>
          </dd>
          <dt>独立校验回执</dt>
          <dd>
            <code>{{ receipt.receiptSha256 }}</code>
          </dd>
        </dl>
      </template>
      <p v-if="['Failed', 'Canceled'].includes(operation.state)">
        原执行已结束。HDD 可能保留部分副本与日志，请勿把取消当作数据已清理。
      </p>
      <div v-if="pending" class="archive-actions">
        <el-button :disabled="busy || operation.cancelRequested" @click="change('cancel')">{{
          operation.cancelRequested ? '等待原任务停止确认' : '取消归档'
        }}</el-button>
        <el-button
          v-if="operation.authorizationRequired && !operation.cancelRequested"
          :disabled="busy"
          @click="change('authorize')"
          >重新授权归档</el-button
        >
      </div>
    </template>
  </el-card>
</template>

<style scoped>
.archive-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  margin-block: 0.75rem;
}
.archive-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}
.workspace-archive code {
  overflow-wrap: anywhere;
}
.workspace-archive dd {
  margin-inline-start: 0;
  margin-block-end: 0.5rem;
}
.workspace-archive :deep(.el-checkbox) {
  height: auto;
  white-space: normal;
}
.workspace-archive :deep(.el-checkbox__label) {
  white-space: normal;
}
</style>
