<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { RouterLink } from 'vue-router'
import ContainerRecursiveAclFacts from '@/components/containers/ContainerRecursiveAclFacts.vue'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { filePath } from '@/services/fileManagement'
import AclTargetEditor from './AclTargetEditor.vue'
import type { ContainerAclEntry } from '@/services/containerAcls'
import type { FileEntry } from '@/services/fileManagement'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  assertRecursiveAclAdvance,
  assertRecursiveAclIntent,
  canRecursiveAclContainerEntry,
  cancelRecursiveAcl,
  confirmRecursiveAcl,
  recursiveAclState,
  recursiveAclResult,
  recursiveAclTerminal,
  currentRecursiveAclInstance,
  getRecursiveAcl,
  queueRecursiveAcl,
  validRecursiveAclCommand,
  cloneRecursiveAclCommand,
  recursiveAclUnresolved,
} from '@/services/containerRecursiveAcls'
import type {
  ContainerRecursiveAclCommand,
  ContainerRecursiveAclIntent,
} from '@/services/containerRecursiveAcls'
import {
  recursiveAclHint,
  removeRecursiveAclHint,
  saveRecursiveAclHint,
} from '@/services/containerRecursiveAclHints'
import type { ContainerRecursiveAclHint } from '@/services/containerRecursiveAclHints'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; refresh: [] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  aclType = ref<'access' | 'default'>('access'),
  clearDefault = ref(false),
  files = ref<ContainerAclEntry[]>([]),
  directories = ref<ContainerAclEntry[]>([]),
  clock = ref(Date.now()),
  confirmingCancel = ref(false)
const source = ref<ContainerRecursiveAclCommand['location'] | null>(null),
  sourceVersion = ref('')
const hint = ref<ContainerRecursiveAclHint | null>(null),
  intent = ref<ContainerRecursiveAclIntent | null>(null),
  frozen = ref<ContainerRecursiveAclCommand | null>(null)
const uncertain = ref(false)
const active = computed(
  () => !!hint.value && (!intent.value || !recursiveAclTerminal(intent.value.state)),
)
const expired = computed(
  () =>
    !!intent.value?.readyExpiresAtUtc && Date.parse(intent.value.readyExpiresAtUtc) <= clock.value,
)
const stale = computed(
  () => !!intent.value && !currentRecursiveAclInstance(intent.value, props.target),
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
          aclType: aclType.value,
          fileEntries: aclType.value === 'default' ? null : sorted(files.value),
          directoryEntries:
            aclType.value === 'default' && clearDefault.value ? [] : sorted(directories.value),
          expectedVersion: sourceVersion.value,
        }
      : null),
)
function baseEntries(directory: boolean): ContainerAclEntry[] {
  return [
    { tag: 'user_obj', id: null, permissions: directory ? 7 : 6 },
    { tag: 'group_obj', id: null, permissions: directory ? 5 : 4 },
    { tag: 'other', id: null, permissions: 0 },
  ]
}
function sorted(entries: ContainerAclEntry[]) {
  const rank = { user_obj: 1, user: 2, group_obj: 4, group: 8, mask: 16, other: 32 }
  return entries
    .map((e) => ({ ...e }))
    .sort((a, b) => rank[a.tag] - rank[b.tag] || (a.id ?? -1) - (b.id ?? -1))
}
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
  aclType.value = 'access'
  clearDefault.value = false
  files.value = baseEntries(false)
  directories.value = baseEntries(true)
  error.value = ''
  uncertain.value = false
}
function failure(reason: unknown) {
  if (intent.value?.state === 'Ready') uncertain.value = true
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    const subject = authSession.user.value?.subject
    if (subject) removeRecursiveAclHint(subject, props.target.containerId)
    clear()
  }
  error.value =
    reason instanceof ApiError && reason.status === 404
      ? '尚未找到原登记，请继续查询同一引用；不会自动重新提交。'
      : reason instanceof Error
        ? reason.message
        : '递归ACL修改结果无法核实，请查询原任务'
}
function accept(value: ContainerRecursiveAclIntent) {
  const original = hint.value
  if (!original) throw new Error('原递归ACL修改登记已失效')
  assertRecursiveAclIntent(
    value,
    original.containerId,
    original.id ?? undefined,
    frozen.value ?? undefined,
  )
  assertRecursiveAclAdvance(intent.value, value)
  intent.value = value
  hint.value = { ...original, id: value.id }
  aclType.value = value.command.aclType
  clearDefault.value =
    value.command.aclType === 'default' && value.command.directoryEntries.length === 0
  files.value = value.command.fileEntries?.map((e) => ({ ...e })) ?? []
  directories.value = value.command.directoryEntries.map((e) => ({ ...e }))
  clock.value = Date.now()
  try {
    saveRecursiveAclHint(hint.value)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '原任务引用未能保存'
  }
  if (recursiveAclTerminal(value.state)) {
    removeRecursiveAclHint(original.subject, original.containerId)
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
  if (intent.value && recursiveAclUnresolved(intent.value.state)) return
  timer = window.setTimeout(() => {
    timer = null
    void query()
  }, 6000)
}
async function perform(
  action: (signal: AbortSignal) => Promise<ContainerRecursiveAclIntent>,
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
      getRecursiveAcl(
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
function open(entry: FileEntry) {
  if (!allowed.value || busy.value || props.disabled || !canRecursiveAclContainerEntry(entry))
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
  aclType.value = 'access'
  clearDefault.value = false
  files.value = baseEntries(false)
  directories.value = baseEntries(true)
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
    if (!value || !subject || !validRecursiveAclCommand(value)) {
      error.value = '请核对ACL完整条目、数值ID（0有效）及mask，每组最多64项，不接受重复或缺字段。'
      return
    }
    const original = {
      subject,
      containerId: props.target.containerId,
      key: createIdempotencyKey(),
      id: null,
    }
    try {
      saveRecursiveAclHint(original)
    } catch (reason) {
      failure(reason)
      return
    }
    hint.value = original
    frozen.value = cloneRecursiveAclCommand(value)
  }
  const original = hint.value!,
    body = frozen.value!
  if (body.location.instanceId !== props.target.instanceId) {
    error.value = '容器实例已变化，请只查询原任务'
    return
  }
  await perform((signal) => queueRecursiveAcl(body, original.key, signal))
}
async function confirm() {
  const original = intent.value
  clock.value = Date.now()
  if (!allowed.value || props.disabled || busy.value || !original || !canConfirm.value) return
  uncertain.value = true
  await perform(
    (signal) => confirmRecursiveAcl(original, signal),
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
    recursiveAclTerminal(original.state) ||
    recursiveAclUnresolved(original.state)
  )
    return
  stop()
  confirmingCancel.value = true
  try {
    await ElMessageBox.confirm(
      '确认请求取消递归ACL修改？已经完成的逐项ACL变更不会自动恢复；可能留下Partial或Unknown，仍需核实。',
      '请求取消递归ACL修改',
      {
        type: 'warning',
        confirmButtonText: '确认请求取消',
        cancelButtonText: '继续等待',
        closeOnClickModal: false,
      },
    )
    if (!allowed.value || hint.value?.id !== original.id) return
    confirmingCancel.value = false
    await perform((signal) => cancelRecursiveAcl(original, signal))
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
      hint.value = recursiveAclHint(subject, props.target.containerId)
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
  <section v-if="allowed && hint" class="container-recursive-acl" aria-label="容器递归ACL修改任务">
    <span role="status">{{ intent ? recursiveAclState(intent.state) : '原预检登记待核实' }}</span>
    <el-button link :disabled="busy || disabled" @click="dialog = true"
      >查看递归ACL修改任务</el-button
    >
    <el-button link :disabled="busy || confirmingCancel" @click="query"
      >查询原递归ACL修改任务</el-button
    >
    <RouterLink v-if="intent && recursiveAclUnresolved(intent.state)" to="/files"
      >前往递归ACL修改异常处置</RouterLink
    >
    <p v-if="error && !dialog" role="alert" class="container-recursive-acl__error">
      {{ error }}
    </p>
  </section>
  <el-dialog
    v-if="allowed"
    v-model="dialog"
    title="递归修改容器目录ACL"
    width="min(620px, calc(100vw - 24px))"
    class="container-recursive-acl-dialog"
    align-center
    :close-on-click-modal="false"
    destroy-on-close
  >
    <p v-if="command" class="container-recursive-acl__path">对象：/{{ command.location.path }}</p>
    <p v-else>原任务对象待核实，仅可查询原登记。</p>
    <el-form v-if="!hint" label-position="top" @submit.prevent="prepare()">
      <el-form-item label="ACL类型">
        <el-select v-model="aclType" aria-label="递归ACL类型" :disabled="busy || !!hint"
          ><el-option value="access" label="access · 文件与目录访问ACL" /><el-option
            value="default"
            label="default · 仅目录继承ACL"
        /></el-select>
      </el-form-item>
      <el-checkbox v-if="aclType === 'default'" v-model="clearDefault" :disabled="busy || !!hint"
        >明确清除所有目录的default ACL</el-checkbox
      >
      <AclTargetEditor
        v-if="aclType === 'access'"
        v-model="files"
        label="文件目标ACL"
        :disabled="busy || !!hint"
      />
      <AclTargetEditor
        v-if="aclType === 'access' || !clearDefault"
        v-model="directories"
        label="目录目标ACL"
        :disabled="busy || !!hint"
      />
      <p v-else role="status">目标：清除所有目录的default ACL；文件ACL保持不变。</p>
    </el-form>
    <el-alert
      v-if="!intent"
      title="access将完整替换文件/目录访问ACL，未列出的命名条目会移除，模式随mask变化；default仅设置或清除目录继承，文件保持不变。数值ID是ACL条目，不更改inode所有者。先预检，确认后逐项保留HDD旧ACL，最多10000项、目录最后处理。取消可能部分修改，不自动续做或回滚。"
      type="info"
      :closable="false"
    />
    <div v-if="intent" class="container-recursive-acl__summary">
      <strong role="status">{{ recursiveAclState(intent.state) }}</strong>
      <ContainerRecursiveAclFacts :row="intent" />
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
            recursiveAclResult(
              intent.state === 'Expired' || (intent.state === 'Ready' && expired)
                ? 'CONTAINER_RECURSIVE_ACL_PLAN_EXPIRED'
                : intent.resultCode,
            )
          }}</span></el-tooltip
        >
      </p>
      <RouterLink v-if="recursiveAclUnresolved(intent.state)" to="/files"
        >前往递归ACL修改异常处置</RouterLink
      >
    </div>
    <p v-if="hint && !intent" class="container-recursive-acl__path">
      原登记引用：<code>{{ hint.key }}</code>
    </p>
    <p v-if="error" role="alert" class="container-recursive-acl__error">{{ error }}</p>
    <template #footer>
      <el-button @click="dialog = false">{{ hint ? '关闭' : '返回' }}</el-button>
      <el-button v-if="!hint" type="primary" :loading="busy" :disabled="disabled" @click="prepare()"
        >生成递归ACL修改预检</el-button
      >
      <el-button v-if="hint" :disabled="busy || confirmingCancel" @click="query"
        >查询原递归ACL修改任务</el-button
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
        >确认递归ACL修改</el-button
      >
      <el-button
        v-if="
          intent && !recursiveAclTerminal(intent.state) && !recursiveAclUnresolved(intent.state)
        "
        :disabled="busy || confirmingCancel"
        @click="cancel"
        >请求取消递归ACL修改</el-button
      >
    </template>
  </el-dialog>
</template>

<style scoped>
:global(.container-recursive-acl-dialog) {
  display: flex;
  flex-direction: column;
  max-height: calc(100dvh - 24px);
}
:global(.container-recursive-acl-dialog .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
:global(.container-recursive-acl-dialog .el-dialog__header),
:global(.container-recursive-acl-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
:global(.container-recursive-acl-dialog .el-dialog__footer) {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: flex-end;
}
:global(.container-recursive-acl-dialog .el-dialog__footer .el-button + .el-button) {
  margin-left: 0;
}
@media (max-width: 600px) {
  :global(.container-recursive-acl-dialog) {
    margin-block: 12px;
  }
}
.container-recursive-acl {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding-block: 8px;
  font-size: 13px;
}
.container-recursive-acl__path,
.container-recursive-acl__summary,
.container-recursive-acl__error {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.container-recursive-acl__summary p {
  margin-block: 10px;
}
.container-recursive-acl__error {
  color: var(--el-color-danger);
  width: 100%;
}
</style>
