<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { RouterLink } from 'vue-router'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import { filePath } from '@/services/fileManagement'
import type { FileEntry } from '@/services/fileManagement'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  assertMoveAdvance,
  assertMoveIntent,
  canMoveContainerEntry,
  cancelMove,
  confirmMove,
  moveState,
  moveFactText,
  moveTerminal,
  currentMoveInstance,
  defaultMoveTarget,
  getMove,
  queueMove,
  validMoveCommand,
} from '@/services/containerMoves'
import type { ContainerMoveCommand, ContainerMoveIntent } from '@/services/containerMoves'
import { moveHint, removeMoveHint, saveMoveHint } from '@/services/containerMoveHints'
import type { ContainerMoveHint } from '@/services/containerMoveHints'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; refresh: [] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  targetPath = ref(''),
  clock = ref(Date.now()),
  confirmingCancel = ref(false)
const source = ref<ContainerMoveCommand['source'] | null>(null),
  sourceVersion = ref('')
const hint = ref<ContainerMoveHint | null>(null),
  intent = ref<ContainerMoveIntent | null>(null),
  frozen = ref<ContainerMoveCommand | null>(null)
const uncertain = ref(false)
const active = computed(() => !!hint.value && (!intent.value || !moveTerminal(intent.value.state)))
const expired = computed(
  () =>
    !!intent.value?.readyExpiresAtUtc && Date.parse(intent.value.readyExpiresAtUtc) <= clock.value,
)
const stale = computed(() => !!intent.value && !currentMoveInstance(intent.value, props.target))
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
      ? { source: source.value, targetPath: targetPath.value, expectedVersion: sourceVersion.value }
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
  targetPath.value = ''
  error.value = ''
  uncertain.value = false
}
function failure(reason: unknown) {
  if (intent.value?.state === 'Ready') uncertain.value = true
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    const subject = authSession.user.value?.subject
    if (subject) removeMoveHint(subject, props.target.containerId)
    clear()
  }
  error.value =
    reason instanceof ApiError && reason.status === 404
      ? '尚未找到原登记，请继续查询同一引用；不会自动重新提交。'
      : reason instanceof Error
        ? reason.message
        : '移动结果无法核实，请查询原任务'
}
function accept(value: ContainerMoveIntent) {
  const original = hint.value
  if (!original) throw new Error('原移动登记已失效')
  assertMoveIntent(value, original.containerId, original.id ?? undefined, frozen.value ?? undefined)
  assertMoveAdvance(intent.value, value)
  intent.value = value
  hint.value = { ...original, id: value.id }
  targetPath.value = value.command.targetPath
  clock.value = Date.now()
  try {
    saveMoveHint(hint.value)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '原任务引用未能保存'
  }
  if (moveTerminal(value.state)) {
    removeMoveHint(original.subject, original.containerId)
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
  action: (signal: AbortSignal) => Promise<ContainerMoveIntent>,
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
      getMove(original.containerId, original.id ?? original.key, signal, original.id === null),
    () => {
      uncertain.value = false
    },
  )
}
function open(entry: FileEntry) {
  if (!allowed.value || busy.value || props.disabled || !canMoveContainerEntry(entry)) return false
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
  targetPath.value = defaultMoveTarget(props.path, entry.name)
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
    if (!value || !subject || !validMoveCommand(value)) {
      error.value = '请输入有效的相对目标路径，不能覆盖源或放在源目录内部'
      return
    }
    const original = {
      subject,
      containerId: props.target.containerId,
      key: createIdempotencyKey(),
      id: null,
    }
    try {
      saveMoveHint(original)
    } catch (reason) {
      failure(reason)
      return
    }
    hint.value = original
    frozen.value = {
      source: { ...value.source },
      targetPath: value.targetPath,
      expectedVersion: value.expectedVersion,
    }
  }
  const original = hint.value!,
    body = frozen.value!
  if (body.source.instanceId !== props.target.instanceId) {
    error.value = '容器实例已变化，请只查询原任务'
    return
  }
  await perform((signal) => queueMove(body, original.key, signal))
}
async function confirm() {
  const original = intent.value
  clock.value = Date.now()
  if (!allowed.value || props.disabled || busy.value || !original || !canConfirm.value) return
  uncertain.value = true
  await perform(
    (signal) => confirmMove(original, signal),
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
    moveTerminal(original.state) ||
    original.state === 'Unknown'
  )
    return
  stop()
  confirmingCancel.value = true
  try {
    await ElMessageBox.confirm(
      '确认请求取消这个移动任务？已经完成的移动不会撤销，不确定结果仍需核实。',
      '请求取消移动',
      {
        type: 'warning',
        confirmButtonText: '确认请求取消',
        cancelButtonText: '继续等待',
        closeOnClickModal: false,
      },
    )
    if (!allowed.value || hint.value?.id !== original.id) return
    confirmingCancel.value = false
    await perform((signal) => cancelMove(original, signal))
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
      hint.value = moveHint(subject, props.target.containerId)
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
  <section v-if="allowed && hint" class="container-move" aria-label="容器移动任务">
    <span role="status">{{ intent ? moveState(intent.state) : '原预检登记待核实' }}</span>
    <el-button link :disabled="busy || disabled" @click="dialog = true">查看移动任务</el-button>
    <el-button link :disabled="busy || confirmingCancel" @click="query">查询原移动任务</el-button>
    <RouterLink v-if="intent?.state === 'Unknown'" to="/files">前往移动异常处置</RouterLink>
    <p v-if="error && !dialog" role="alert" class="container-move__error">{{ error }}</p>
  </section>
  <el-dialog
    v-if="allowed"
    v-model="dialog"
    title="移动容器文件或目录"
    width="min(620px, calc(100vw - 24px))"
    class="container-move-dialog"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <p v-if="command" class="container-move__path">来源：/{{ command.source.path }}</p>
    <p v-else>原任务来源待核实，仅可查询原登记。</p>
    <el-form label-position="top" @submit.prevent="prepare()">
      <el-form-item label="目标相对路径"
        ><el-input v-model="targetPath" aria-label="移动目标相对路径" :disabled="busy || !!hint"
      /></el-form-item>
    </el-form>
    <el-alert
      v-if="!intent"
      title="先在后台校验完整来源，再明确确认移动。目标必须不存在；校验目标后移除源路径，原文件转存私有原件区。"
      type="info"
      :closable="false"
    />
    <div v-if="intent" class="container-move__summary">
      <strong role="status">{{ moveState(intent.state) }}</strong>
      <p>目标发布：{{ moveFactText(intent.destinationPublished) }}</p>
      <p>源路径移除：{{ moveFactText(intent.sourceRemoved) }}</p>
      <p>原件留存：{{ moveFactText(intent.sourceRetained) }}</p>
      <p v-if="intent.kind">
        {{ intent.kind === 'Directory' ? '目录' : '文件' }} · {{ intent.entryCount }} 项 ·
        {{ formatBytes(intent.totalBytes!) }}
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
      <p v-if="intent.privateMayExist">
        可能保留私有原件或暂存数据，尚未确认清理；不会自动重复移动。
      </p>
      <p v-if="intent.resultCode">
        <code>{{ intent.resultCode }}</code>
      </p>
      <RouterLink v-if="intent.state === 'Unknown'" to="/files">前往移动异常处置</RouterLink>
    </div>
    <p v-if="hint && !intent" class="container-move__path">
      原登记引用：<code>{{ hint.key }}</code>
    </p>
    <p v-if="error" role="alert" class="container-move__error">{{ error }}</p>
    <template #footer>
      <el-button @click="dialog = false">{{ hint ? '关闭' : '返回' }}</el-button>
      <el-button v-if="!hint" type="primary" :loading="busy" :disabled="disabled" @click="prepare()"
        >生成移动预检</el-button
      >
      <el-button v-if="hint" :disabled="busy || confirmingCancel" @click="query"
        >查询原移动任务</el-button
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
        >确认移动</el-button
      >
      <el-button
        v-if="intent && !moveTerminal(intent.state) && intent.state !== 'Unknown'"
        :disabled="busy || confirmingCancel"
        @click="cancel"
        >请求取消移动</el-button
      >
    </template>
  </el-dialog>
</template>

<style scoped>
.container-move {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding-block: 8px;
  font-size: 13px;
}
.container-move__path,
.container-move__summary,
.container-move__error {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.container-move__summary p {
  margin-block: 10px;
}
.container-move__error {
  color: var(--el-color-danger);
  width: 100%;
}
</style>
