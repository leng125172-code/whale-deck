<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { RouterLink } from 'vue-router'
import ContainerRecursiveAclRestoreFacts from '@/components/containers/ContainerRecursiveAclRestoreFacts.vue'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import ContainerRecursiveAclRestoreSource from './ContainerRecursiveAclRestoreSource.vue'
import {
  validContainerRecursiveAclHistorySummary,
  sameContainerRecursiveAclHistorySummary,
} from '@/services/containerRecursiveAclHistory'
import type {
  ContainerRecursiveAclHistorySummary,
  ContainerRecursiveAclHistorySnapshot,
} from '@/services/containerRecursiveAclHistory'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  assertRecursiveAclRestoreAdvance,
  assertRecursiveAclRestoreIntent,
  cancelRecursiveAclRestore,
  confirmRecursiveAclRestore,
  recursiveAclRestoreState,
  recursiveAclRestoreResult,
  recursiveAclRestoreTerminal,
  currentRecursiveAclRestoreInstance,
  getRecursiveAclRestore,
  queueRecursiveAclRestore,
  validRecursiveAclRestoreCommand,
  cloneRecursiveAclRestoreCommand,
  recursiveAclRestoreUnresolved,
} from '@/services/containerRecursiveAclRestores'
import type {
  ContainerRecursiveAclRestoreCommand,
  ContainerRecursiveAclRestoreIntent,
} from '@/services/containerRecursiveAclRestores'
import {
  recursiveAclRestoreHint,
  removeRecursiveAclRestoreHint,
  saveRecursiveAclRestoreHint,
} from '@/services/containerRecursiveAclRestoreHints'
import type { ContainerRecursiveAclRestoreHint } from '@/services/containerRecursiveAclRestoreHints'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; refresh: [] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  clock = ref(Date.now()),
  confirmingCancel = ref(false)
const source = ref<ContainerRecursiveAclRestoreCommand['location'] | null>(null),
  sourceVersion = ref('')
const hint = ref<ContainerRecursiveAclRestoreHint | null>(null),
  intent = ref<ContainerRecursiveAclRestoreIntent | null>(null),
  frozen = ref<ContainerRecursiveAclRestoreCommand | null>(null)
const uncertain = ref(false)
const active = computed(
  () => !!hint.value && (!intent.value || !recursiveAclRestoreTerminal(intent.value.state)),
)
const expired = computed(
  () =>
    !!intent.value?.readyExpiresAtUtc && Date.parse(intent.value.readyExpiresAtUtc) <= clock.value,
)
const stale = computed(
  () => !!intent.value && !currentRecursiveAclRestoreInstance(intent.value, props.target),
)
const canConfirm = computed(
  () =>
    intent.value?.state === 'Ready' &&
    !!intent.value.confirmation &&
    !expired.value &&
    !stale.value &&
    !uncertain.value,
)
const previewSource = ref<ContainerRecursiveAclHistorySummary | null>(null)
const command = computed(() => intent.value?.command ?? frozen.value)
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
  previewSource.value = null
  error.value = ''
  uncertain.value = false
}
function failure(reason: unknown) {
  if (intent.value?.state === 'Ready') uncertain.value = true
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    const subject = authSession.user.value?.subject
    if (subject) removeRecursiveAclRestoreHint(subject, props.target.containerId)
    clear()
  }
  error.value =
    reason instanceof ApiError && reason.status === 404
      ? '尚未找到原登记，请继续查询同一引用；不会自动重新提交。'
      : reason instanceof Error
        ? reason.message
        : '递归ACL恢复结果无法核实，请查询原任务'
}
function accept(value: ContainerRecursiveAclRestoreIntent) {
  const original = hint.value
  if (!original) throw new Error('原递归ACL恢复登记已失效')
  assertRecursiveAclRestoreIntent(
    value,
    original.containerId,
    original.id ?? undefined,
    frozen.value ?? undefined,
  )
  assertRecursiveAclRestoreAdvance(intent.value, value)
  if (value.restoreSource) {
    if (
      previewSource.value &&
      !sameContainerRecursiveAclHistorySummary(value.restoreSource, previewSource.value)
    )
      throw new Error('恢复来源摘要已变化，不能确认')
    previewSource.value = { ...value.restoreSource }
  }
  intent.value = value
  hint.value = { ...original, id: value.id }
  clock.value = Date.now()
  try {
    saveRecursiveAclRestoreHint(hint.value)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '原任务引用未能保存'
  }
  if (recursiveAclRestoreTerminal(value.state)) {
    removeRecursiveAclRestoreHint(original.subject, original.containerId)
    if (
      (value.state === 'Succeeded' || (value.receipt?.changedCount ?? 0) > 0) &&
      refreshed !== value.id
    ) {
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
  if (intent.value && recursiveAclRestoreUnresolved(intent.value.state)) return
  timer = window.setTimeout(() => {
    timer = null
    void query()
  }, 6000)
}
async function perform(
  action: (signal: AbortSignal) => Promise<ContainerRecursiveAclRestoreIntent>,
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
      getRecursiveAclRestore(
        original.containerId,
        original.id ?? original.key,
        signal,
        original.id === null,
      ),
    () => {
      uncertain.value = false
    },
  )
}
function openRestore(snapshot: ContainerRecursiveAclHistorySnapshot, version: string) {
  if (
    !allowed.value ||
    busy.value ||
    props.disabled ||
    !validContainerRecursiveAclHistorySummary(
      snapshot.summary,
      snapshot.location,
      snapshot.location.path,
    ) ||
    !/^[A-F0-9]{64}$/.test(version) ||
    snapshot.location.containerId !== props.target.containerId ||
    snapshot.location.instanceId !== props.target.instanceId
  )
    return false
  if (active.value) {
    dialog.value = true
    return true
  }
  clear()
  source.value = { ...snapshot.location }
  sourceVersion.value = version
  previewSource.value = { ...snapshot.summary }
  frozen.value = {
    location: { ...snapshot.location },
    captureId: snapshot.summary.captureId,
    expectedHistoryDigest: snapshot.summary.historyDigest,
    expectedVersion: version,
  }
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
    if (!value || !subject || !validRecursiveAclRestoreCommand(value)) {
      error.value = '请核对所选历史来源、摘要和当前目录版本。'
      return
    }
    const original = {
      subject,
      containerId: props.target.containerId,
      key: createIdempotencyKey(),
      id: null,
    }
    try {
      saveRecursiveAclRestoreHint(original)
    } catch (reason) {
      failure(reason)
      return
    }
    hint.value = original
    frozen.value = cloneRecursiveAclRestoreCommand(value)
  }
  const original = hint.value!,
    body = frozen.value!
  if (body.location.instanceId !== props.target.instanceId) {
    error.value = '容器实例已变化，请只查询原任务'
    return
  }
  await perform((signal) => queueRecursiveAclRestore(body, original.key, signal))
}
async function confirm() {
  const original = intent.value
  clock.value = Date.now()
  if (!allowed.value || props.disabled || busy.value || !original || !canConfirm.value) return
  uncertain.value = true
  await perform(
    (signal) => confirmRecursiveAclRestore(original, signal),
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
    recursiveAclRestoreTerminal(original.state) ||
    recursiveAclRestoreUnresolved(original.state)
  )
    return
  stop()
  confirmingCancel.value = true
  try {
    await ElMessageBox.confirm(
      '确认请求取消递归ACL恢复？已经完成的逐项ACL恢复不会自动撤销；可能留下Partial或Unknown，仍需核实。',
      '请求取消递归ACL恢复',
      {
        type: 'warning',
        confirmButtonText: '确认请求取消',
        cancelButtonText: '继续等待',
        closeOnClickModal: false,
      },
    )
    if (!allowed.value || hint.value?.id !== original.id) return
    confirmingCancel.value = false
    await perform((signal) => cancelRecursiveAclRestore(original, signal))
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
      hint.value = recursiveAclRestoreHint(subject, props.target.containerId)
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
defineExpose({ openRestore })
</script>

<template>
  <section
    v-if="allowed && hint"
    class="container-recursive-acl-restore"
    aria-label="容器递归ACL恢复任务"
  >
    <span role="status">{{
      intent ? recursiveAclRestoreState(intent.state) : '原预检登记待核实'
    }}</span>
    <el-button link :disabled="busy || disabled" @click="dialog = true"
      >查看递归ACL恢复任务</el-button
    >
    <el-button link :disabled="busy || confirmingCancel" @click="query"
      >查询原递归ACL恢复任务</el-button
    >
    <RouterLink v-if="intent && recursiveAclRestoreUnresolved(intent.state)" to="/files"
      >前往递归ACL恢复异常处置</RouterLink
    >
    <p v-if="error && !dialog" role="alert" class="container-recursive-acl-restore__error">
      {{ error }}
    </p>
  </section>
  <el-dialog
    v-if="allowed"
    v-model="dialog"
    title="递归恢复容器目录历史ACL"
    width="min(620px, calc(100vw - 24px))"
    class="container-recursive-acl-restore-dialog"
    align-center
    :close-on-click-modal="false"
    destroy-on-close
  >
    <p v-if="command" class="container-recursive-acl-restore__path">
      对象：/{{ command.location.path }}
    </p>
    <p v-else>原任务对象待核实，仅可查询原登记。</p>
    <p v-if="command">
      来源捕获：<code>{{ command.captureId }}</code>
    </p>
    <p v-if="command">
      来源历史摘要：<code>{{ command.expectedHistoryDigest }}</code>
    </p>
    <el-alert
      v-if="!intent"
      title="按所选来源的原修改类型逐项恢复异构ACL，最多10000项；不恢复旧inode或所有者，不改另一类ACL。预检不写入；确认后仍校验来源和当前清单并先留存当前前像。取消可能已部分恢复，不自动续做或回滚。"
      type="info"
      :closable="false"
    />
    <ContainerRecursiveAclRestoreSource
      v-if="command && previewSource"
      :location="command.location"
      :summary="previewSource"
    />
    <div v-if="intent" class="container-recursive-acl-restore__summary">
      <strong role="status">{{ recursiveAclRestoreState(intent.state) }}</strong>
      <ContainerRecursiveAclRestoreFacts :row="intent" />
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
      <p v-if="intent.resultCode">
        <el-tooltip :content="intent.resultCode"
          ><span>{{
            recursiveAclRestoreResult(
              intent.state === 'Expired' || (intent.state === 'Ready' && expired)
                ? 'CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_EXPIRED'
                : intent.resultCode,
            )
          }}</span></el-tooltip
        >
      </p>
      <RouterLink v-if="recursiveAclRestoreUnresolved(intent.state)" to="/files"
        >前往递归ACL恢复异常处置</RouterLink
      >
    </div>
    <p v-if="hint && !intent" class="container-recursive-acl-restore__path">
      原登记引用：<code>{{ hint.key }}</code>
    </p>
    <p v-if="error" role="alert" class="container-recursive-acl-restore__error">{{ error }}</p>
    <template #footer>
      <el-button @click="dialog = false">{{ hint ? '关闭' : '返回' }}</el-button>
      <el-button v-if="!hint" type="primary" :loading="busy" :disabled="disabled" @click="prepare()"
        >生成递归ACL恢复预检</el-button
      >
      <el-button v-if="hint" :disabled="busy || confirmingCancel" @click="query"
        >查询原递归ACL恢复任务</el-button
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
        >确认递归ACL恢复</el-button
      >
      <el-button
        v-if="
          intent &&
          !recursiveAclRestoreTerminal(intent.state) &&
          !recursiveAclRestoreUnresolved(intent.state)
        "
        :disabled="busy || confirmingCancel"
        @click="cancel"
        >请求取消递归ACL恢复</el-button
      >
    </template>
  </el-dialog>
</template>

<style scoped>
:global(.container-recursive-acl-restore-dialog) {
  display: flex;
  flex-direction: column;
  max-height: calc(100dvh - 24px);
}
:global(.container-recursive-acl-restore-dialog .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
:global(.container-recursive-acl-restore-dialog .el-dialog__header),
:global(.container-recursive-acl-restore-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
:global(.container-recursive-acl-restore-dialog .el-dialog__footer) {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: flex-end;
}
:global(.container-recursive-acl-restore-dialog .el-dialog__footer .el-button + .el-button) {
  margin-left: 0;
}
@media (max-width: 600px) {
  :global(.container-recursive-acl-restore-dialog) {
    margin-block: 12px;
  }
}
.container-recursive-acl-restore {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding-block: 8px;
  font-size: 13px;
}
.container-recursive-acl-restore__path,
.container-recursive-acl-restore__summary,
.container-recursive-acl-restore__error {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.container-recursive-acl-restore__summary p {
  margin-block: 10px;
}
.container-recursive-acl-restore__error {
  color: var(--el-color-danger);
  width: 100%;
}
</style>
