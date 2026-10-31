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
  assertOwnershipAdvance,
  assertOwnershipIntent,
  canOwnershipContainerEntry,
  cancelOwnership,
  confirmOwnership,
  ownershipState,
  ownershipResult,
  ownershipTerminal,
  currentOwnershipInstance,
  getOwnership,
  queueOwnership,
  validOwnershipCommand,
  parseOwnershipId,
} from '@/services/containerOwnerships'
import type {
  ContainerOwnershipCommand,
  ContainerOwnershipIntent,
} from '@/services/containerOwnerships'
import {
  ownershipHint,
  removeOwnershipHint,
  saveOwnershipHint,
} from '@/services/containerOwnershipHints'
import type { ContainerOwnershipHint } from '@/services/containerOwnershipHints'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; refresh: [] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  uid = ref(''),
  gid = ref(''),
  clock = ref(Date.now()),
  confirmingCancel = ref(false)
const source = ref<ContainerOwnershipCommand['location'] | null>(null),
  sourceVersion = ref('')
const hint = ref<ContainerOwnershipHint | null>(null),
  intent = ref<ContainerOwnershipIntent | null>(null),
  frozen = ref<ContainerOwnershipCommand | null>(null)
const uncertain = ref(false)
const active = computed(
  () => !!hint.value && (!intent.value || !ownershipTerminal(intent.value.state)),
)
const expired = computed(
  () =>
    !!intent.value?.readyExpiresAtUtc && Date.parse(intent.value.readyExpiresAtUtc) <= clock.value,
)
const stale = computed(
  () => !!intent.value && !currentOwnershipInstance(intent.value, props.target),
)
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
          uid: parseOwnershipId(uid.value),
          gid: parseOwnershipId(gid.value),
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
  uid.value = ''
  gid.value = ''
  error.value = ''
  uncertain.value = false
}
function failure(reason: unknown) {
  if (intent.value?.state === 'Ready') uncertain.value = true
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    const subject = authSession.user.value?.subject
    if (subject) removeOwnershipHint(subject, props.target.containerId)
    clear()
  }
  error.value =
    reason instanceof ApiError && reason.status === 404
      ? '尚未找到原登记，请继续查询同一引用；不会自动重新提交。'
      : reason instanceof Error
        ? reason.message
        : '所有者修改结果无法核实，请查询原任务'
}
function accept(value: ContainerOwnershipIntent) {
  const original = hint.value
  if (!original) throw new Error('原所有者修改登记已失效')
  assertOwnershipIntent(
    value,
    original.containerId,
    original.id ?? undefined,
    frozen.value ?? undefined,
  )
  assertOwnershipAdvance(intent.value, value)
  intent.value = value
  hint.value = { ...original, id: value.id }
  uid.value = String(value.command.uid)
  gid.value = String(value.command.gid)
  clock.value = Date.now()
  try {
    saveOwnershipHint(hint.value)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '原任务引用未能保存'
  }
  if (ownershipTerminal(value.state)) {
    removeOwnershipHint(original.subject, original.containerId)
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
  action: (signal: AbortSignal) => Promise<ContainerOwnershipIntent>,
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
      getOwnership(original.containerId, original.id ?? original.key, signal, original.id === null),
    () => {
      uncertain.value = false
    },
  )
}
function open(entry: FileEntry) {
  if (!allowed.value || busy.value || props.disabled || !canOwnershipContainerEntry(entry))
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
  uid.value = ''
  gid.value = ''
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
    if (!value || !subject || !validOwnershipCommand(value)) {
      error.value = '请明确填写目标UID和GID，范围0–4294967294的十进制整数；不递归，不修改模式或ACL'
      return
    }
    const original = {
      subject,
      containerId: props.target.containerId,
      key: createIdempotencyKey(),
      id: null,
    }
    try {
      saveOwnershipHint(original)
    } catch (reason) {
      failure(reason)
      return
    }
    hint.value = original
    frozen.value = {
      location: { ...value.location },
      uid: value.uid,
      gid: value.gid,
      expectedVersion: value.expectedVersion,
    }
  }
  const original = hint.value!,
    body = frozen.value!
  if (body.location.instanceId !== props.target.instanceId) {
    error.value = '容器实例已变化，请只查询原任务'
    return
  }
  await perform((signal) => queueOwnership(body, original.key, signal))
}
async function confirm() {
  const original = intent.value
  clock.value = Date.now()
  if (!allowed.value || props.disabled || busy.value || !original || !canConfirm.value) return
  uncertain.value = true
  await perform(
    (signal) => confirmOwnership(original, signal),
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
    ownershipTerminal(original.state) ||
    original.state === 'Unknown'
  )
    return
  stop()
  confirmingCancel.value = true
  try {
    await ElMessageBox.confirm(
      '确认请求取消所有者修改？已经完成的UID/GID变更不会自动恢复，不确定结果仍需核实。',
      '请求取消所有者修改',
      {
        type: 'warning',
        confirmButtonText: '确认请求取消',
        cancelButtonText: '继续等待',
        closeOnClickModal: false,
      },
    )
    if (!allowed.value || hint.value?.id !== original.id) return
    confirmingCancel.value = false
    await perform((signal) => cancelOwnership(original, signal))
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
      hint.value = ownershipHint(subject, props.target.containerId)
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
  <section v-if="allowed && hint" class="container-ownership" aria-label="容器所有者修改任务">
    <span role="status">{{ intent ? ownershipState(intent.state) : '原预检登记待核实' }}</span>
    <el-button link :disabled="busy || disabled" @click="dialog = true"
      >查看所有者修改任务</el-button
    >
    <el-button link :disabled="busy || confirmingCancel" @click="query"
      >查询原所有者修改任务</el-button
    >
    <RouterLink v-if="intent?.state === 'Unknown'" to="/files">前往所有者修改异常处置</RouterLink>
    <p v-if="error && !dialog" role="alert" class="container-ownership__error">{{ error }}</p>
  </section>
  <el-dialog
    v-if="allowed"
    v-model="dialog"
    title="修改容器文件或目录所有者"
    width="min(620px, calc(100vw - 24px))"
    class="container-ownership-dialog"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <p v-if="command" class="container-ownership__path">对象：/{{ command.location.path }}</p>
    <p v-else>原任务对象待核实，仅可查询原登记。</p>
    <el-form label-position="top" @submit.prevent="prepare()">
      <el-form-item label="目标用户 UID（十进制，0有效）">
        <el-input
          v-model="uid"
          aria-label="目标UID"
          inputmode="numeric"
          maxlength="10"
          placeholder="必须明确输入UID"
          :disabled="busy || !!hint"
        />
      </el-form-item>
      <el-form-item label="目标用户组 GID（十进制，0有效）">
        <el-input
          v-model="gid"
          aria-label="目标GID"
          inputmode="numeric"
          maxlength="10"
          placeholder="必须明确输入GID"
          :disabled="busy || !!hint"
        />
      </el-form-item>
    </el-form>
    <el-alert
      v-if="!intent"
      title="目标UID/GID必须明确填写，原属主以预检为准。确认后先保留HDD元数据，再仅修改当前对象所有者；不递归、不修改模式或ACL。"
      type="info"
      :closable="false"
    />
    <div v-if="intent" class="container-ownership__summary">
      <strong role="status">{{ ownershipState(intent.state) }}</strong>
      <p v-if="intent.kind">
        {{ intent.kind === 'Directory' ? '目录' : '文件' }} · 原UID/GID {{ intent.originalUid }}:{{
          intent.originalGid
        }}
        → {{ intent.command.uid }}:{{ intent.command.gid }}
        <span v-if="intent.noChange">（属主相同，仅核实）</span>
      </p>
      <p v-if="intent.kind">
        容器执行身份（内核FSUID/FSGID）：{{ intent.executorUid }}:{{
          intent.executorGid
        }}，不作为原文件属主。
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
        所有者变更：{{
          intent.ownerConfirmed === true
            ? '已确认'
            : intent.ownerConfirmed === false
              ? '已确认未发生'
              : '尚未确认'
        }}； HDD元数据：{{
          intent.historyVerified
            ? '已验证留存'
            : intent.historyMayExist
              ? '可能存在，尚未验证'
              : '未记录写入'
        }}。 元数据留存不包含文件内容。
      </p>
      <p v-if="intent.resultCode">
        <el-tooltip :content="intent.resultCode"
          ><span>{{
            ownershipResult(
              intent.state === 'Expired' || (intent.state === 'Ready' && expired)
                ? 'CONTAINER_OWNERSHIP_PLAN_EXPIRED'
                : intent.resultCode,
            )
          }}</span></el-tooltip
        >
      </p>
      <RouterLink v-if="intent.state === 'Unknown'" to="/files">前往所有者修改异常处置</RouterLink>
    </div>
    <p v-if="hint && !intent" class="container-ownership__path">
      原登记引用：<code>{{ hint.key }}</code>
    </p>
    <p v-if="error" role="alert" class="container-ownership__error">{{ error }}</p>
    <template #footer>
      <el-button @click="dialog = false">{{ hint ? '关闭' : '返回' }}</el-button>
      <el-button v-if="!hint" type="primary" :loading="busy" :disabled="disabled" @click="prepare()"
        >生成所有者修改预检</el-button
      >
      <el-button v-if="hint" :disabled="busy || confirmingCancel" @click="query"
        >查询原所有者修改任务</el-button
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
        >确认所有者修改</el-button
      >
      <el-button
        v-if="intent && !ownershipTerminal(intent.state) && intent.state !== 'Unknown'"
        :disabled="busy || confirmingCancel"
        @click="cancel"
        >请求取消所有者修改</el-button
      >
    </template>
  </el-dialog>
</template>

<style scoped>
:global(.container-ownership-dialog) {
  display: flex;
  flex-direction: column;
  max-height: calc(100dvh - 24px);
}
:global(.container-ownership-dialog .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
:global(.container-ownership-dialog .el-dialog__header),
:global(.container-ownership-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
@media (max-width: 600px) {
  :global(.container-ownership-dialog) {
    margin-block: 12px;
  }
}
.container-ownership {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding-block: 8px;
  font-size: 13px;
}
.container-ownership__path,
.container-ownership__summary,
.container-ownership__error {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.container-ownership__summary p {
  margin-block: 10px;
}
.container-ownership__error {
  color: var(--el-color-danger);
  width: 100%;
}
</style>
