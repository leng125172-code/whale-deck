<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElCard, ElCheckbox } from 'element-plus'
import { createIdempotencyKey } from '@/services/apiClient'
import {
  developmentWorkspaces as api,
  workspaceError,
  type Workspace,
  type LifecyclePlan,
  type LifecycleOperation,
} from '@/services/developmentWorkspaces'

const props = defineProps<{ workspace: Workspace }>()
const emit = defineEmits<{ refresh: [] }>()
const plan = ref<LifecyclePlan | null>(null)
const operation = ref<LifecycleOperation | null>(null)
const confirmed = ref(false)
const busy = ref(false)
const error = ref('')
let key: string | undefined
let lifetime: AbortController
const state = computed(() => props.workspace.lifecycleState ?? 'Active')
const labels: Record<string, string> = {
  Active: '可用',
  Stopping: '停用确认中',
  Stopped: '已停用',
  Resuming: '恢复确认中',
}
const waiting = computed(
  () =>
    ['Stopping', 'Resuming'].includes(state.value) ||
    (!!operation.value && !operation.value.completedAtUtc),
)

watch(
  () => props.workspace.id,
  (_, previous, cleanup) => {
    const controller = new AbortController()
    lifetime = controller
    if (previous) {
      plan.value = null
      operation.value = null
      key = undefined
      confirmed.value = false
      error.value = ''
    }
    const timer = window.setInterval(() => {
      if (document.visibilityState === 'visible' && waiting.value && !busy.value) void observe()
    }, 3000)
    cleanup(() => {
      controller.abort()
      window.clearInterval(timer)
    })
    if (waiting.value) void observe()
  },
  { immediate: true },
)

async function perform(action: (signal: AbortSignal) => Promise<void>) {
  if (busy.value) return
  const signal = lifetime.signal
  busy.value = true
  error.value = ''
  try {
    await action(signal)
  } catch (failure) {
    if (!signal.aborted) error.value = workspaceError(failure)
  } finally {
    if (!signal.aborted) busy.value = false
  }
}
async function observe() {
  await perform(async (signal) => {
    const result = operation.value
      ? await api.lifecycleGet(props.workspace.id, operation.value.operationId, signal)
      : await api.lifecycleCurrent(props.workspace.id, signal)
    if (signal.aborted) return
    operation.value = result
    if (result?.completedAtUtc) emit('refresh')
  })
}
function loadPlan(action: 'stop' | 'resume') {
  void perform(async (signal) => {
    const result = await api.lifecyclePlan(props.workspace.id, action, signal)
    if (signal.aborted) return
    if (
      result.workspaceId !== props.workspace.id ||
      result.action !== action ||
      result.expectedVersion !== (props.workspace.lifecycleVersion ?? 1) ||
      !result.retainsData ||
      !result.reservesBudget ||
      !result.requiresIdle
    )
      throw new Error('WORKSPACE_LIFECYCLE_PLAN_CHANGED')
    plan.value = result
    confirmed.value = false
    key = undefined
  })
}
function apply() {
  const selected = plan.value
  if (!selected || !confirmed.value) return
  void perform(async (signal) => {
    key ??= createIdempotencyKey()
    const result = await api.lifecycleApply(props.workspace.id, selected, key, signal)
    if (signal.aborted) return
    operation.value = result
    plan.value = null
    confirmed.value = false
    key = undefined
    emit('refresh')
  })
}
function authorize() {
  const selected = operation.value
  if (!selected || selected.completedAtUtc) return
  void perform(async (signal) => {
    const result = await api.lifecycleAuthorize(props.workspace.id, selected.operationId, signal)
    if (!signal.aborted) operation.value = result
  })
}
function closePlan() {
  plan.value = null
  key = undefined
  confirmed.value = false
}
</script>

<template>
  <el-card shadow="never" class="workspace-lifecycle">
    <template #header><strong>空间启停</strong></template>
    <p role="status">{{ labels[state] ?? state }}</p>
    <p>停用只阻止新任务和源码写入，源码、历史与磁盘配额继续保留。请先完成或取消正在处理的任务。</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div class="lifecycle-actions">
      <el-button v-if="state === 'Active'" :disabled="busy || waiting" @click="loadPlan('stop')"
        >核对停用计划</el-button
      >
      <el-button v-if="state === 'Stopped'" :disabled="busy || waiting" @click="loadPlan('resume')"
        >核对恢复计划</el-button
      >
      <el-button :disabled="busy" @click="emit('refresh')">刷新启停状态</el-button>
    </div>
    <template v-if="plan">
      <p>
        {{
          plan.action === 'stop'
            ? '停用闲置空间，保留原工作树和配额。'
            : '核对原存储和配额后恢复任务提交。'
        }}
      </p>
      <p class="plan-digest">
        <code>{{ plan.planSha256 }}</code>
      </p>
      <el-checkbox v-model="confirmed" :disabled="busy"
        >我已核对计划，确认保留源码与配额</el-checkbox
      >
      <div class="lifecycle-actions">
        <el-button type="primary" :disabled="busy || !confirmed" @click="apply">{{
          plan.action === 'stop' ? '确认停用空间' : '确认恢复空间'
        }}</el-button>
        <el-button :disabled="busy" @click="closePlan">关闭启停计划</el-button>
      </div>
    </template>
    <template v-if="operation">
      <p>
        任务 <code>{{ operation.operationId }}</code>
      </p>
      <p v-if="!operation.completedAtUtc">等待确认，源码与配额继续保留。</p>
      <el-alert
        v-if="operation.code"
        :title="
          operation.code === 'WORKSPACE_LIFECYCLE_AUTHORIZATION_REQUIRED'
            ? '登录或管理权限发生变化，请重新授权继续确认。'
            : '尚未取得宿主确认，空间保持当前状态，后台将继续核对。'
        "
        type="warning"
        :closable="false"
        show-icon
      />
      <el-button
        v-if="
          operation.code === 'WORKSPACE_LIFECYCLE_AUTHORIZATION_REQUIRED' &&
          !operation.completedAtUtc
        "
        :disabled="busy"
        @click="authorize"
        >重新授权继续</el-button
      >
    </template>
  </el-card>
</template>

<style scoped>
.lifecycle-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
  margin-block: 0.75rem;
}
.lifecycle-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}
.workspace-lifecycle code {
  overflow-wrap: anywhere;
}
.workspace-lifecycle :deep(.el-checkbox) {
  height: auto;
  white-space: normal;
}
.workspace-lifecycle :deep(.el-checkbox__label) {
  white-space: normal;
}
</style>
