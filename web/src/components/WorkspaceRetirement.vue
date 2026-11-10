<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElCard, ElCheckbox } from 'element-plus'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { workspaceError, type Workspace } from '@/services/developmentWorkspaces'
import { workspaceArchives } from '@/services/workspaceArchives'
import {
  workspaceRetirements as api,
  type WorkspaceRetirement,
} from '@/services/workspaceRetirements'

const props = defineProps<{ workspace: Workspace; archivePending?: boolean }>()
const emit = defineEmits<{ pending: [value: boolean]; refresh: [] }>()
const operation = ref<WorkspaceRetirement | null>(null),
  archiveId = ref<string | null>(null)
const confirmed = ref(false),
  busy = ref(false),
  denied = ref(false),
  uncertain = ref(true),
  error = ref('')
const pendingBegin = ref<{ key: string; archiveId: string; epoch: number } | null>(null)
let lifetime: AbortController
const pending = computed(
  () => uncertain.value || (!!operation.value && !operation.value.completedAtUtc),
)
const labels: Record<string, string> = {
  Plan: '正在核对退役计划',
  AwaitingConfirmation: '等待确认删除源码',
  Execute: '正在退役源码和释放配额',
  Retired: '空间已退役',
  AttentionRequired: '退役结果需要处理',
  CanceledBeforeExecution: '执行前已取消',
  CanceledBeforeDispatch: '派发前已取消',
  Canceled: '退役已取消',
  Rejected: '退役条件不满足',
  Interrupted: '退役执行中断',
}
const plan = computed(() => {
  if (!operation.value?.planJson) return null
  try {
    const value = JSON.parse(operation.value.planJson) as Record<string, unknown>
    return typeof value.retirementPlanSha256 === 'string' &&
      /^[a-f0-9]{64}$/.test(value.retirementPlanSha256) &&
      value.archiveId === operation.value.archiveId &&
      value.expectedEpoch === props.workspace.lifecycleVersion &&
      value.retainsSource === true &&
      value.reservesBudget === true &&
      value.deletionAuthorized === false &&
      Number.isSafeInteger(value.entryCount) &&
      Number(value.entryCount) >= 0 &&
      Number.isSafeInteger(value.totalBytes) &&
      Number(value.totalBytes) >= 0
      ? {
          digest: value.retirementPlanSha256,
          entries: Number(value.entryCount),
          bytes: Number(value.totalBytes),
        }
      : null
  } catch {
    return null
  }
})
function validSuccess(value: WorkspaceRetirement) {
  if (
    value.state !== 'Succeeded' ||
    value.phase !== 'Retired' ||
    !value.completedAtUtc ||
    !value.planJson ||
    !value.executionJson
  )
    return false
  try {
    const result = JSON.parse(value.executionJson) as Record<string, unknown>
    const planned = JSON.parse(value.planJson) as Record<string, unknown>
    return (
      result.workspaceId === props.workspace.id &&
      result.retirementId === value.retirementId &&
      planned.archiveId === value.archiveId &&
      typeof planned.retirementPlanSha256 === 'string' &&
      /^[a-f0-9]{64}$/.test(planned.retirementPlanSha256) &&
      result.retirementPlanSha256 === planned.retirementPlanSha256 &&
      result.sourceRemoved === true &&
      result.quotaReleased === true &&
      result.claimReleased === true &&
      result.retainsHdd === true
    )
  } catch {
    return false
  }
}
function accept(value: WorkspaceRetirement | null) {
  if (
    value &&
    (value.workspaceId !== props.workspace.id ||
      !value.retainsHdd ||
      ((value.sourceRetired || value.budgetReleased || value.state === 'Succeeded') &&
        (!value.sourceRetired || !value.budgetReleased || !validSuccess(value))))
  )
    throw new Error('WORKSPACE_RETIREMENT_BINDING_INVALID')
  const changed =
    value?.retirementId !== operation.value?.retirementId ||
    value?.planJson !== operation.value?.planJson
  const completed =
    !!value?.completedAtUtc && value.completedAtUtc !== operation.value?.completedAtUtc
  if (changed || completed || value?.authorizationRequired || value?.cancelRequested)
    confirmed.value = false
  if (value && !value.completedAtUtc && value.archiveId === pendingBegin.value?.archiveId)
    pendingBegin.value = null
  operation.value = value
  uncertain.value = false
  emit('pending', !!value && !value.completedAtUtc)
  if (completed) emit('refresh')
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
        archiveId.value = null
        confirmed.value = false
        pendingBegin.value = null
        uncertain.value = true
        emit('pending', true)
        error.value = '登录或权限已变化，已停止自动刷新。请重新登录或恢复权限后刷新退役状态。'
      } else error.value = workspaceError(failure)
    }
  } finally {
    if (!signal.aborted) busy.value = false
  }
}
async function observe() {
  await perform(async (signal) => {
    const value = await api.current(props.workspace.id, signal)
    if (signal.aborted) return
    accept(value)
    denied.value = false
    if (props.workspace.lifecycleState === 'Stopped' && (!value || value.completedAtUtc)) {
      const archived = await workspaceArchives.current(props.workspace.id, signal)
      if (signal.aborted) return
      archiveId.value = null
      if (
        archived?.workspaceId === props.workspace.id &&
        archived.state === 'Succeeded' &&
        archived.completedAtUtc &&
        archived.verifyJson &&
        archived.planJson
      ) {
        const source = JSON.parse(archived.planJson) as Record<string, unknown>
        const verified = JSON.parse(archived.verifyJson) as Record<string, unknown>
        if (
          source.expectedEpoch === props.workspace.lifecycleVersion &&
          verified.operationId === archived.archiveId
        )
          archiveId.value = archived.archiveId
      }
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
      archiveId.value = null
      pendingBegin.value = null
      confirmed.value = false
      error.value = ''
    }
    const controller = lifetime
    const timer = window.setInterval(() => {
      if (
        document.visibilityState === 'visible' &&
        pending.value &&
        !denied.value &&
        !busy.value &&
        !operation.value?.attentionRequired
      )
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
watch(
  () => props.archivePending,
  (value, previous) => {
    if (previous && !value && !busy.value && !denied.value) void observe()
  },
)
function begin() {
  if (
    (!archiveId.value && !pendingBegin.value) ||
    props.workspace.lifecycleState !== 'Stopped' ||
    props.archivePending ||
    (pending.value && !pendingBegin.value) ||
    denied.value
  )
    return
  void perform(async (signal) => {
    pendingBegin.value ??= {
      key: createIdempotencyKey(),
      archiveId: archiveId.value!,
      epoch: props.workspace.lifecycleVersion ?? 0,
    }
    const request = pendingBegin.value
    uncertain.value = true
    emit('pending', true)
    const result = await api.begin(
      props.workspace.id,
      request.archiveId,
      request.epoch,
      request.key,
      signal,
    )
    if (!signal.aborted) {
      accept(result)
      pendingBegin.value = null
      emit('refresh')
    }
  })
}
function confirm() {
  const value = operation.value,
    selected = plan.value
  if (
    !value?.awaitingConfirmation ||
    value.cancelRequested ||
    value.authorizationRequired ||
    !selected ||
    !confirmed.value
  )
    return
  void perform(async (signal) => {
    const result = await api.confirm(
      props.workspace.id,
      value.retirementId,
      selected.digest,
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
  if (!value || value.completedAtUtc || value.attentionRequired) return
  void perform(async (signal) => {
    const result = await api[action](props.workspace.id, value.retirementId, signal)
    if (!signal.aborted) accept(result)
  })
}
</script>

<template>
  <el-card shadow="never" class="workspace-retirement" role="region" aria-label="开发空间退役">
    <template #header><strong>空间退役</strong></template>
    <p>
      退役会删除 NVMe 上的空间源码并释放磁盘配额和应用资源预算。HDD
      归档、历史任务、报告与审计继续保留。
    </p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div class="retirement-actions">
      <el-button
        v-if="workspace.lifecycleState === 'Stopped'"
        :disabled="
          busy ||
          (pending && !pendingBegin) ||
          denied ||
          archivePending ||
          (!archiveId && !pendingBegin)
        "
        @click="begin"
        >{{ pendingBegin ? '重连原退役请求' : '核对空间退役计划' }}</el-button
      >
      <el-button :disabled="busy" @click="observe">刷新退役状态</el-button>
    </div>
    <p v-if="workspace.lifecycleState === 'Stopped' && !archiveId">
      先完成当前停用版本的 HDD 归档及独立校验，再核对退役计划。
    </p>
    <template v-if="operation">
      <p role="status">{{ labels[operation.phase] ?? operation.phase }}</p>
      <p>
        退役 <code>{{ operation.retirementId }}</code>
      </p>
      <p>
        保留的 HDD 归档 <code>{{ operation.archiveId }}</code>
      </p>
      <template
        v-if="
          operation.awaitingConfirmation &&
          !operation.cancelRequested &&
          !operation.authorizationRequired &&
          plan
        "
      >
        <p>{{ plan.entries }} 项，{{ plan.bytes }} 字节源码。请核对 HDD 留存后确认删除。</p>
        <p>
          <code>{{ plan.digest }}</code>
        </p>
        <p v-if="operation.planExpiresAtUtc">计划有效期至 {{ operation.planExpiresAtUtc }}</p>
        <el-checkbox v-model="confirmed" :disabled="busy"
          >我确认删除此空间的原源码并释放配额与资源预算，保留 HDD 归档和历史</el-checkbox
        >
        <div class="retirement-actions">
          <el-button type="danger" :disabled="busy || !confirmed" @click="confirm"
            >确认删除源码并退役空间</el-button
          >
        </div>
      </template>
      <el-alert
        v-if="operation.attentionRequired"
        title="执行曾中断或停止，源码可能已被部分删除。空间继续隔离并占用预算；保留原任务与回执，等待显式恢复处理。"
        type="warning"
        :closable="false"
        show-icon
      />
      <el-alert
        v-else-if="operation.code"
        :title="
          operation.authorizationRequired
            ? '当前登录或权限已失效。未派发的任务可重新授权；已派发的任务只会停止原执行。'
            : '尚未确认原任务结果，请刷新查看；预算继续保留。'
        "
        type="warning"
        :closable="false"
        show-icon
      />
      <p v-if="operation.budgetReleased">
        源码和磁盘配额已退役，应用资源预算已释放。HDD 留存、历史记录和报告仍可读取。
      </p>
      <p v-else-if="operation.completedAtUtc">此退役请求已结束，源码和预算继续保留。</p>
      <div v-if="pending && !operation.attentionRequired" class="retirement-actions">
        <el-button :disabled="busy || operation.cancelRequested" @click="change('cancel')">{{
          operation.cancelRequested ? '等待原任务停止确认' : '取消退役'
        }}</el-button>
        <el-button
          v-if="operation.authorizationRequired && !operation.cancelRequested"
          :disabled="busy"
          @click="change('authorize')"
          >重新授权退役</el-button
        >
      </div>
    </template>
  </el-card>
</template>

<style scoped>
.retirement-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  margin-block: 0.75rem;
}
.retirement-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}
.workspace-retirement code {
  overflow-wrap: anywhere;
}
.workspace-retirement :deep(.el-checkbox) {
  height: auto;
  white-space: normal;
}
.workspace-retirement :deep(.el-checkbox__label) {
  white-space: normal;
}
</style>
