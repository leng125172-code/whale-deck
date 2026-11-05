<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { RouterLink } from 'vue-router'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { filePath } from '@/services/fileManagement'
import type { FileEntry } from '@/services/fileManagement'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  assertRecycleAdvance,
  assertRecycleIntent,
  canRecycleContainerEntry,
  cancelRecycle,
  confirmRecycle,
  recycleState,
  recycleResult,
  recycleTerminal,
  currentRecycleInstance,
  getRecycle,
  queueRecycle,
  validRecycleCommand,
} from '@/services/containerRecycles'
import type { ContainerRecycleCommand, ContainerRecycleIntent } from '@/services/containerRecycles'
import { recycleHint, removeRecycleHint, saveRecycleHint } from '@/services/containerRecycleHints'
import type { ContainerRecycleHint } from '@/services/containerRecycleHints'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; refresh: [] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  clock = ref(Date.now()),
  confirmingCancel = ref(false)
const source = ref<ContainerRecycleCommand['location'] | null>(null),
  sourceVersion = ref('')
const hint = ref<ContainerRecycleHint | null>(null),
  intent = ref<ContainerRecycleIntent | null>(null),
  frozen = ref<ContainerRecycleCommand | null>(null)
const uncertain = ref(false)
const active = computed(
  () => !!hint.value && (!intent.value || !recycleTerminal(intent.value.state)),
)
const expired = computed(
  () =>
    !!intent.value?.readyExpiresAtUtc && Date.parse(intent.value.readyExpiresAtUtc) <= clock.value,
)
const stale = computed(() => !!intent.value && !currentRecycleInstance(intent.value, props.target))
const canConfirm = computed(
  () =>
    intent.value?.state === 'Ready' &&
    !!intent.value.confirmation &&
    !expired.value &&
    !stale.value &&
    !uncertain.value,
)
const command = computed(
  () =>
    intent.value?.command ??
    frozen.value ??
    (source.value
      ? {
          location: source.value,
          expectedVersion: sourceVersion.value,
        }
      : null),
)
let generation = 0,
  timer: number | null = null,
  controller: AbortController | null = null,
  refreshed = ''
function stop() {
  generation++
  controller?.abort()
  controller = null
  busy.value = false
  if (timer !== null) window.clearTimeout(timer)
  timer = null
}
function clear() {
  if (confirmingCancel.value) {
    ElMessageBox.close()
    confirmingCancel.value = false
  }
  stop()
  dialog.value = false
  hint.value = null
  intent.value = null
  source.value = null
  frozen.value = null
  sourceVersion.value = ''
  error.value = ''
  uncertain.value = false
}
function failure(reason: unknown) {
  if (intent.value?.state === 'Ready') uncertain.value = true
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    const subject = authSession.user.value?.subject
    if (subject) removeRecycleHint(subject, props.target.containerId)
    clear()
  }
  error.value =
    reason instanceof ApiError && reason.status === 404
      ? '尚未找到原登记，请继续查询同一引用；不会自动重新提交。'
      : reason instanceof Error
        ? reason.message
        : '文件回收结果无法核实，请查询原任务'
}
function accept(value: ContainerRecycleIntent) {
  const original = hint.value
  if (!original) throw new Error('原文件回收登记已失效')
  assertRecycleIntent(
    value,
    original.containerId,
    original.id ?? undefined,
    frozen.value ?? undefined,
  )
  assertRecycleAdvance(intent.value, value)
  intent.value = value
  hint.value = { ...original, id: value.id }
  clock.value = Date.now()
  try {
    saveRecycleHint(hint.value)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '原任务引用未能保存'
  }
  if (recycleTerminal(value.state)) {
    removeRecycleHint(original.subject, original.containerId)
    if (value.state === 'Succeeded' && refreshed !== value.id) {
      refreshed = value.id
      emit('refresh')
    }
  }
}
function schedule(token: number) {
  if (token !== generation) return
  if (timer !== null) window.clearTimeout(timer)
  timer = null
  if (!active.value || !intent.value || document.hidden || confirmingCancel.value) return
  if (intent.value?.state === 'Ready') {
    const remaining = Date.parse(intent.value.readyExpiresAtUtc!) - Date.now()
    if (remaining <= 0) {
      clock.value = Date.now()
      return
    }
    timer = window.setTimeout(
      () => {
        timer = null
        clock.value = Date.now()
      },
      Math.min(remaining + 25, 300025),
    )
    return
  }
  if (intent.value?.state === 'Unknown') return
  timer = window.setTimeout(() => {
    timer = null
    void query()
  }, 6000)
}
async function perform(
  action: (signal: AbortSignal) => Promise<ContainerRecycleIntent>,
  received?: () => void,
) {
  if (!allowed.value || busy.value) return
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  try {
    const value = await action(local.signal)
    if (token !== generation || local.signal.aborted || !allowed.value) return
    accept(value)
    received?.()
  } catch (reason) {
    if (token === generation && !local.signal.aborted) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
      schedule(token)
    }
  }
}
async function query() {
  const original = hint.value
  if (!original || confirmingCancel.value) return
  await perform(
    (signal) =>
      getRecycle(original.containerId, original.id ?? original.key, signal, original.id === null),
    () => {
      uncertain.value = false
    },
  )
}
function open(entry: FileEntry) {
  if (!allowed.value || busy.value || props.disabled || !canRecycleContainerEntry(entry))
    return false
  if (active.value) {
    dialog.value = true
    return true
  }
  clear()
  source.value = {
    containerId: props.target.containerId,
    instanceId: props.target.instanceId,
    path: filePath(props.path, entry.name),
  }
  sourceVersion.value = entry.version
  dialog.value = true
  return true
}
async function prepare(retry = false) {
  if (
    !allowed.value ||
    busy.value ||
    props.disabled ||
    (retry ? !hint.value || !!intent.value || !frozen.value : !!hint.value || !source.value)
  )
    return
  if (!retry) {
    const value = command.value,
      subject = authSession.user.value?.subject
    if (!value || !subject || !validRecycleCommand(value)) {
      error.value = '原文件或当前版本无法核实，请刷新文件列表后重新预检'
      return
    }
    const original = {
      subject,
      containerId: props.target.containerId,
      key: createIdempotencyKey(),
      id: null,
    }
    try {
      saveRecycleHint(original)
    } catch (reason) {
      failure(reason)
      return
    }
    hint.value = original
    frozen.value = {
      location: { ...value.location },
      expectedVersion: value.expectedVersion,
    }
  }
  const original = hint.value!,
    body = frozen.value!
  if (body.location.instanceId !== props.target.instanceId) {
    error.value = '容器实例已变化，请只查询原任务'
    return
  }
  await perform((signal) => queueRecycle(body, original.key, signal))
}
async function confirm() {
  const original = intent.value
  clock.value = Date.now()
  if (!allowed.value || props.disabled || busy.value || !original || !canConfirm.value) return
  uncertain.value = true
  await perform(
    (signal) => confirmRecycle(original, signal),
    () => {
      uncertain.value = false
    },
  )
  if (uncertain.value && hint.value && error.value)
    error.value += '；原确认尚未核实，请查询原任务，未自动重试确认。'
}
async function cancel() {
  const original = intent.value
  if (
    !allowed.value ||
    busy.value ||
    confirmingCancel.value ||
    !original ||
    recycleTerminal(original.state) ||
    original.state === 'Unknown'
  )
    return
  stop()
  confirmingCancel.value = true
  try {
    await ElMessageBox.confirm(
      '确认请求取消文件回收？已经完成的文件回收不会自动撤销，不确定结果仍需核实。',
      '请求取消文件回收',
      {
        type: 'warning',
        confirmButtonText: '确认请求取消',
        cancelButtonText: '继续等待',
        closeOnClickModal: false,
      },
    )
    if (!allowed.value || hint.value?.id !== original.id) return
    confirmingCancel.value = false
    await perform((signal) => cancelRecycle(original, signal))
  } catch {
    /* Dismissal sends no cancellation. */
  } finally {
    confirmingCancel.value = false
    schedule(generation)
  }
}
function visible() {
  if (document.hidden) stop()
  else {
    clock.value = Date.now()
    if (hint.value && !confirmingCancel.value) void query()
  }
}
watch(
  [
    allowed,
    () => authSession.user.value?.subject,
    () => props.target.containerId,
    () => props.target.instanceId,
  ],
  () => {
    clear()
    const subject = authSession.user.value?.subject
    if (allowed.value && subject) {
      hint.value = recycleHint(subject, props.target.containerId)
      if (hint.value) void query()
    }
  },
  { immediate: true },
)
watch(
  () => props.path,
  () => {
    if (!hint.value) clear()
  },
)
watch(dialog, (value) => {
  if (!value) {
    stop()
    if (!hint.value) {
      source.value = null
      frozen.value = null
      error.value = ''
    } else schedule(generation)
  }
})
watch(
  [dialog, busy, active, confirmingCancel],
  () => emit('locked', dialog.value || busy.value || active.value || confirmingCancel.value),
  { immediate: true },
)
onMounted(() => document.addEventListener('visibilitychange', visible))
onBeforeUnmount(() => {
  stop()
  if (confirmingCancel.value) ElMessageBox.close()
  document.removeEventListener('visibilitychange', visible)
  emit('locked', false)
})
defineExpose({ open })
</script>

<template>
  <section v-if="allowed && hint" class="container-recycle" aria-label="容器文件回收任务">
    <span role="status">{{ intent ? recycleState(intent.state) : '原预检登记待核实' }}</span>
    <el-button link :disabled="busy || disabled" @click="dialog = true">查看文件回收任务</el-button>
    <el-button link :disabled="busy || confirmingCancel" @click="query"
      >查询原文件回收任务</el-button
    >
    <RouterLink v-if="intent?.state === 'Unknown'" to="/files">前往文件回收异常处置</RouterLink>
    <p v-if="error && !dialog" role="alert" class="container-recycle__error">{{ error }}</p>
  </section>
  <el-dialog
    v-if="allowed"
    v-model="dialog"
    title="回收容器普通文件"
    width="min(620px, calc(100vw - 24px))"
    class="container-recycle-dialog"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <p v-if="command" class="container-recycle__path">对象：/{{ command.location.path }}</p>
    <p v-else>原任务对象待核实，仅可查询原登记。</p>
    <el-alert
      v-if="!intent"
      title="确认后保存HDD原件，并将原文件移入私有回收区。当前仅支持普通文件，不彻底删除或覆盖目标。"
      type="info"
      :closable="false"
    />
    <div v-if="intent" class="container-recycle__summary">
      <strong role="status">{{ recycleState(intent.state) }}</strong>
      <p v-if="intent.sizeBytes !== null">
        普通文件 · {{ intent.sizeBytes }} 字节 · 原UID/GID {{ intent.originalUid }}:{{
          intent.originalGid
        }}
      </p>
      <p v-if="intent.readyExpiresAtUtc">
        计划到期：{{ new Date(intent.readyExpiresAtUtc).toLocaleString() }}
      </p>
      <p>
        预检任务：<code>{{ intent.planningJobId }}</code>
      </p>
      <p v-if="intent.executionJobId">
        执行任务：<code>{{ intent.executionJobId }}</code>
      </p>
      <el-alert
        v-if="intent.confirmation"
        :title="intent.confirmation.warning"
        type="warning"
        :closable="false"
      />
      <el-alert
        v-if="stale"
        title="容器实例已变化；只能查询或取消原任务，不能确认旧计划。"
        type="warning"
        :closable="false"
      />
      <el-alert
        v-if="expired && intent.state === 'Ready'"
        title="计划已过期，请查询原任务；不会自动重新预检。"
        type="warning"
        :closable="false"
      />
      <p v-if="intent.executionJobId">
        回收源事实：{{
          intent.receipt?.sourceRetained === true
            ? '原文件已留存回收区'
            : intent.receipt?.sourceRestored === true
              ? '原文件曾恢复'
              : intent.receipt?.state === 'Aborted'
                ? '原操作已终止'
                : '尚未证明'
        }}； HDD原件：{{
          intent.historyVerified
            ? '已验证留存'
            : intent.historyMayExist
              ? '可能存在，未验证'
              : '未记录'
        }}。Unknown不代表未发生移除。
      </p>
      <p v-if="intent.resultCode">
        <el-tooltip :content="intent.resultCode"
          ><span>{{
            recycleResult(
              intent.state === 'Expired' || (intent.state === 'Ready' && expired)
                ? 'CONTAINER_RECYCLE_PLAN_EXPIRED'
                : intent.resultCode,
            )
          }}</span></el-tooltip
        >
      </p>
      <RouterLink v-if="intent.state === 'Unknown'" to="/files">前往文件回收异常处置</RouterLink>
    </div>
    <p v-if="hint && !intent" class="container-recycle__path">
      原登记引用：<code>{{ hint.key }}</code>
    </p>
    <p v-if="error" role="alert" class="container-recycle__error">{{ error }}</p>
    <template #footer>
      <el-button @click="dialog = false">{{ hint ? '关闭' : '返回' }}</el-button>
      <el-button v-if="!hint" type="primary" :loading="busy" :disabled="disabled" @click="prepare()"
        >生成文件回收预检</el-button
      >
      <el-button v-if="hint" :disabled="busy || confirmingCancel" @click="query"
        >查询原文件回收任务</el-button
      >
      <el-button
        v-if="hint && !intent && frozen"
        :disabled="busy || disabled"
        @click="prepare(true)"
        >重试原预检登记</el-button
      >
      <el-button
        v-if="intent?.state === 'Ready'"
        type="primary"
        :loading="busy"
        :disabled="disabled || !canConfirm"
        @click="confirm"
        >确认文件回收</el-button
      >
      <el-button
        v-if="intent && !recycleTerminal(intent.state) && intent.state !== 'Unknown'"
        :disabled="busy || confirmingCancel"
        @click="cancel"
        >请求取消文件回收</el-button
      >
    </template>
  </el-dialog>
</template>

<style scoped>
:global(.container-recycle-dialog) {
  display: flex;
  flex-direction: column;
  max-height: calc(100dvh - 24px);
}
:global(.container-recycle-dialog .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
:global(.container-recycle-dialog .el-dialog__header),
:global(.container-recycle-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
@media (max-width: 600px) {
  :global(.container-recycle-dialog) {
    margin-block: 12px;
  }
}
.container-recycle {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding-block: 8px;
  font-size: 13px;
}
.container-recycle__path,
.container-recycle__summary,
.container-recycle__error {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.container-recycle__summary p {
  margin-block: 10px;
}
.container-recycle__error {
  color: var(--el-color-danger);
  width: 100%;
}
</style>
