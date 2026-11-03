<script setup lang="ts">
import { assertAclRestore, getAclRestore, queueAclRestore } from '@/services/containerAclRestores'
import type { ContainerAclRestore } from '@/services/containerAclRestores'
import { assertContainerAclHistorySnapshot } from '@/services/containerAclHistory'
import type { ContainerAclHistorySnapshot } from '@/services/containerAclHistory'
import AclEntryList from './AclEntryList.vue'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { RouterLink } from 'vue-router'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { filePath } from '@/services/fileManagement'
import type { FileEntry } from '@/services/fileManagement'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  assertAclAdvance,
  assertAclIntent,
  canAclContainerEntry,
  cancelAcl,
  confirmAcl,
  aclState,
  aclResult,
  aclTerminal,
  currentAclInstance,
  getAcl,
  queueAcl,
  validAclCommand,
  parseAclId,
  aclMode,
  aclModeLabel,
} from '@/services/containerAcls'
import type { ContainerAclCommand, ContainerAclIntent, AclTag } from '@/services/containerAcls'
import { aclHint, removeAclHint, saveAclHint } from '@/services/containerAclHints'
import type { ContainerAclHint } from '@/services/containerAclHints'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; refresh: [] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  aclType = ref<'access' | 'default'>('access'),
  clearDefault = ref(false),
  addTag = ref<'user' | 'group' | 'mask'>('user'),
  clock = ref(Date.now()),
  confirmingCancel = ref(false)
const draft = ref<{ key: number; tag: AclTag; id: string; permissions: number }[]>([])
let draftKey = 0
const sourceKind = ref<'File' | 'Directory'>('File')
function addEntry() {
  if (!busy.value && !hint.value && draft.value.length < 64)
    draft.value.push({ key: ++draftKey, tag: addTag.value, id: '', permissions: 0 })
}
const source = ref<ContainerAclCommand['location'] | null>(null),
  sourceVersion = ref('')
const hint = ref<ContainerAclHint | null>(null),
  intent = ref<ContainerAclIntent | null>(null),
  frozen = ref<ContainerAclCommand | null>(null)
const restoreSource = ref<ContainerAclHistorySnapshot | null>(null)
const restoring = computed(() => !!restoreSource.value || hint.value?.restore === true)
const uncertain = ref(false)
const active = computed(() => !!hint.value && (!intent.value || !aclTerminal(intent.value.state)))
const expired = computed(
  () =>
    !!intent.value?.readyExpiresAtUtc && Date.parse(intent.value.readyExpiresAtUtc) <= clock.value,
)
const stale = computed(() => !!intent.value && !currentAclInstance(intent.value, props.target))
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
          entries: (clearDefault.value && aclType.value === 'default'
            ? []
            : draft.value.map((e) => ({
                tag: e.tag,
                id: e.tag === 'user' || e.tag === 'group' ? parseAclId(e.id) : null,
                permissions: e.permissions,
              }))
          ).sort(
            (a, b) =>
              ({ user_obj: 1, user: 2, group_obj: 4, group: 8, mask: 16, other: 32 })[a.tag] -
                { user_obj: 1, user: 2, group_obj: 4, group: 8, mask: 16, other: 32 }[b.tag] ||
              (a.id ?? -1) - (b.id ?? -1),
          ),
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
  restoreSource.value = null
  draft.value = []
  aclType.value = 'access'
  clearDefault.value = false
  error.value = ''
  uncertain.value = false
}
function failure(reason: unknown) {
  if (intent.value?.state === 'Ready') uncertain.value = true
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    const subject = authSession.user.value?.subject
    if (subject) removeAclHint(subject, props.target.containerId)
    clear()
  }
  error.value =
    reason instanceof ApiError && reason.status === 404
      ? '尚未找到原登记，请继续查询同一引用；不会自动重新提交。'
      : restoring.value
        ? 'ACL恢复请求无法核实，请查询原任务；不会自动重新提交或修改历史来源。'
        : reason instanceof Error
          ? reason.message
          : 'ACL修改结果无法核实，请查询原任务'
}
function accept(value: ContainerAclIntent) {
  const original = hint.value
  if (!original) throw new Error('原ACL修改登记已失效')
  assertAclIntent(value, original.containerId, original.id ?? undefined, frozen.value ?? undefined)
  assertAclAdvance(intent.value, value)
  intent.value = value
  hint.value = { ...original, id: value.id }
  aclType.value = value.command.aclType
  clearDefault.value = aclType.value === 'default' && !value.command.entries.length
  draft.value = value.command.entries.map((e) => ({
    key: ++draftKey,
    tag: e.tag,
    id: e.id === null ? '' : String(e.id),
    permissions: e.permissions,
  }))
  clock.value = Date.now()
  try {
    saveAclHint(hint.value)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '原任务引用未能保存'
  }
  if (aclTerminal(value.state)) {
    removeAclHint(original.subject, original.containerId)
    if (value.state === 'Succeeded' && refreshed !== value.id) {
      refreshed = value.id
      emit('refresh')
    }
  }
}
function acceptRestore(value: ContainerAclRestore) {
  const original = hint.value
  if (!original || !original.restore) throw new Error('原ACL恢复引用已失效')
  assertAclRestore(
    value,
    original.containerId,
    original.id ?? undefined,
    restoreSource.value ?? undefined,
    frozen.value?.expectedVersion,
  )
  restoreSource.value = JSON.parse(JSON.stringify(value.source))
  accept(value.intent)
}
function openRestore(value: ContainerAclHistorySnapshot, currentVersion: string) {
  if (
    !allowed.value ||
    busy.value ||
    props.disabled ||
    active.value ||
    !/^[A-F0-9]{64}$/.test(currentVersion)
  )
    return false
  const snapshot = assertContainerAclHistorySnapshot(
    value,
    props.target,
    value.location.path,
    value.entry,
  )
  if (
    !snapshot.location.path.startsWith(props.path ? props.path + '/' : '') ||
    snapshot.location.path.slice(props.path ? props.path.length + 1 : 0).includes('/')
  )
    return false
  clear()
  restoreSource.value = JSON.parse(JSON.stringify(snapshot))
  source.value = { ...snapshot.location }
  sourceVersion.value = currentVersion
  sourceKind.value = snapshot.entry.kind
  frozen.value = {
    location: { ...snapshot.location },
    aclType: snapshot.entry.changedAclType,
    entries: (snapshot.entry.changedAclType === 'access'
      ? snapshot.accessAcl
      : snapshot.defaultAcl
    ).map((e) => ({ ...e })),
    expectedVersion: currentVersion,
  }
  dialog.value = true
  return true
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
  action: (signal: AbortSignal) => Promise<ContainerAclIntent | ContainerAclRestore>,
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
    if ('intent' in value) acceptRestore(value)
    else accept(value)
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
      original.restore
        ? getAclRestore(
            original.containerId,
            original.id ?? original.key,
            signal,
            original.id === null,
          )
        : getAcl(original.containerId, original.id ?? original.key, signal, original.id === null),
    () => {
      uncertain.value = false
    },
  )
}
function open(entry: FileEntry) {
  if (!allowed.value || busy.value || props.disabled || !canAclContainerEntry(entry)) return false
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
  sourceKind.value = entry.kind as 'File' | 'Directory'
  draft.value = ['user_obj', 'group_obj', 'other'].map((tag, i) => ({
    key: ++draftKey,
    tag: tag as AclTag,
    id: '',
    permissions: parseInt(entry.mode[i + 1]!, 8),
  }))
  aclType.value = 'access'
  clearDefault.value = false
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
    if (!value || !subject || !validAclCommand(value)) {
      error.value =
        '请核对完整ACL条目：指定用户/组须明确数值ID（0有效）和mask，权限0–7；条目不得重复，最多64项。default空列表表示清空，仅限目录。'
      return
    }
    const original = {
      subject,
      containerId: props.target.containerId,
      key: createIdempotencyKey(),
      id: null,
      ...(restoreSource.value ? { restore: true as const } : {}),
    }
    try {
      saveAclHint(original)
    } catch (reason) {
      failure(reason)
      return
    }
    hint.value = original
    frozen.value = {
      location: { ...value.location },
      aclType: value.aclType,
      entries: value.entries.map((e) => ({ ...e })),
      expectedVersion: value.expectedVersion,
    }
  }
  const original = hint.value!,
    body = frozen.value!
  if (body.location.instanceId !== props.target.instanceId) {
    error.value = '容器实例已变化，请只查询原任务'
    return
  }
  if (original.restore) {
    const snapshot = restoreSource.value
    if (!snapshot) return
    await perform((signal) =>
      queueAclRestore(
        {
          location: body.location,
          captureId: snapshot.entry.captureId,
          expectedHistoryDigest: snapshot.entry.historyDigest,
          expectedVersion: body.expectedVersion,
        },
        original.key,
        snapshot,
        signal,
      ),
    )
  } else await perform((signal) => queueAcl(body, original.key, signal))
}
async function confirm() {
  const original = intent.value
  clock.value = Date.now()
  if (!allowed.value || props.disabled || busy.value || !original || !canConfirm.value) return
  uncertain.value = true
  await perform(
    (signal) => confirmAcl(original, signal),
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
    aclTerminal(original.state) ||
    original.state === 'Unknown'
  )
    return
  stop()
  confirmingCancel.value = true
  try {
    await ElMessageBox.confirm(
      '确认请求取消ACL修改？已经完成的ACL变更不会自动恢复，不确定结果仍需核实。',
      '请求取消ACL修改',
      {
        type: 'warning',
        confirmButtonText: '确认请求取消',
        cancelButtonText: '继续等待',
        closeOnClickModal: false,
      },
    )
    if (!allowed.value || hint.value?.id !== original.id) return
    confirmingCancel.value = false
    await perform((signal) => cancelAcl(original, signal))
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
      hint.value = aclHint(subject, props.target.containerId)
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
      restoreSource.value = null
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
defineExpose({ open, openRestore })
</script>

<template>
  <section v-if="allowed && hint" class="container-acl" aria-label="容器ACL修改任务">
    <span role="status">{{ intent ? aclState(intent.state) : '原预检登记待核实' }}</span>
    <el-button link :disabled="busy || disabled" @click="dialog = true">查看ACL修改任务</el-button>
    <el-button link :disabled="busy || confirmingCancel" @click="query"
      >查询原ACL修改任务</el-button
    >
    <RouterLink v-if="intent?.state === 'Unknown'" to="/files">前往ACL修改异常处置</RouterLink>
    <p v-if="error && !dialog" role="alert" class="container-acl__error">{{ error }}</p>
  </section>
  <el-dialog
    v-if="allowed"
    v-model="dialog"
    :title="restoring ? '从历史恢复容器ACL' : '修改容器文件或目录ACL'"
    width="min(620px, calc(100vw - 24px))"
    class="container-acl-dialog"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <p v-if="command" class="container-acl__path">对象：/{{ command.location.path }}</p>
    <p v-else>原任务对象待核实，仅可查询原登记。</p>
    <el-form v-if="!hint && !restoring" label-position="top" @submit.prevent="prepare()">
      <el-form-item label="修改的ACL类型"
        ><el-radio-group v-model="aclType" aria-label="ACL类型" :disabled="busy || !!hint"
          ><el-radio-button value="access">access 访问权限</el-radio-button
          ><el-radio-button value="default" :disabled="sourceKind === 'File' && !intent"
            >default 默认继承</el-radio-button
          ></el-radio-group
        ></el-form-item
      >
      <el-checkbox
        v-if="aclType === 'default'"
        v-model="clearDefault"
        :disabled="busy || !!hint"
        aria-label="明确清空默认ACL"
        >明确清空默认ACL（不修改现有子项）</el-checkbox
      >
      <div
        v-if="!clearDefault || aclType !== 'default'"
        class="container-acl__draft"
        aria-label="目标ACL条目"
      >
        <div v-for="(entry, index) in draft" :key="entry.key" class="container-acl__entry">
          <strong>{{
            {
              user_obj: '文件属主',
              user: '指定用户',
              group_obj: '文件属组',
              group: '指定组',
              mask: 'mask',
              other: '其他用户',
            }[entry.tag]
          }}</strong>
          <el-input
            v-if="['user', 'group'].includes(entry.tag)"
            v-model="entry.id"
            inputmode="numeric"
            maxlength="10"
            :aria-label="`条目${index + 1} 数值ID`"
            placeholder="明确输入ID，0有效"
            :disabled="busy || !!hint"
          />
          <el-select
            v-model="entry.permissions"
            :aria-label="`条目${index + 1} 权限`"
            :disabled="busy || !!hint"
            ><el-option
              v-for="bits in 8"
              :key="bits"
              :value="bits - 1"
              :label="`${bits - 1} · ${['---', '--x', '-w-', '-wx', 'r--', 'r-x', 'rw-', 'rwx'][bits - 1]}`"
          /></el-select>
          <el-button
            v-if="['user', 'group', 'mask'].includes(entry.tag)"
            :disabled="busy || !!hint"
            :aria-label="`移除条目${index + 1}`"
            @click="draft.splice(index, 1)"
            >移除</el-button
          >
        </div>
        <div class="container-acl__add">
          <el-select v-model="addTag" aria-label="新增条目类型" :disabled="busy || !!hint"
            ><el-option label="指定用户" value="user" /><el-option
              label="指定组"
              value="group" /><el-option label="mask" value="mask" /></el-select
          ><el-button :disabled="busy || !!hint || draft.length >= 64" @click="addEntry"
            >添加ACL条目</el-button
          >
        </div>
      </div>
    </el-form>
    <section v-if="restoreSource" aria-label="ACL恢复来源" class="container-acl__path">
      <h3>所选历史 · {{ restoreSource.entry.changedAclType }}</h3>
      <p>捕获标识：{{ restoreSource.entry.captureId }}</p>
      <p>来源摘要：{{ restoreSource.entry.historyDigest }}</p>
      <p>
        留存时间：{{
          new Date(restoreSource.entry.createdAtUnixMs).toLocaleString('zh-CN')
        }}；原实例：{{ restoreSource.entry.sourceInstanceId }}
      </p>
      <AclEntryList
        :entries="
          restoreSource.entry.changedAclType === 'access'
            ? restoreSource.accessAcl
            : restoreSource.defaultAcl
        "
        label="所选历史目标ACL"
      />
      <el-alert
        type="warning"
        :closable="false"
        :title="`仅恢复${restoreSource.entry.changedAclType} ACL，另一类ACL、属主、内容及现有子项保持；不递归。确认后执行前先留存当前ACL原件。历史ACL不是当前权限，必须核对新预检。`"
      />
    </section>
    <AclEntryList
      v-if="hint && !intent && command"
      :entries="command.entries"
      :label="`待核实目标${command.aclType} ACL`"
    />
    <el-alert
      v-if="!intent && !restoring"
      title="初始条目仅按普通权限填写，不代表原有ACL。此操作完整替换所选一类ACL，请在预检后逐条核对原ACL；named用户/组须显式mask，access的mask会改变普通权限组位。default仅影响后续继承，现有子项保持；不递归。"
      type="info"
      :closable="false"
    />
    <div v-if="intent" class="container-acl__summary">
      <strong role="status">{{ aclState(intent.state) }}</strong>
      <p v-if="intent.kind">
        {{ intent.kind === 'Directory' ? '目录' : '文件' }} · 原UID/GID {{ intent.originalUid }}:{{
          intent.originalGid
        }}，保持属主；目标{{ intent.command.aclType }}模式 {{ aclModeLabel(intent.targetMode) }}
        <span v-if="intent.noChange">（ACL相同，仅核实）</span>
      </p>
      <p v-if="intent.kind">
        容器执行身份：{{ intent.executorUid }}:{{ intent.executorGid }}；原access模式
        {{ aclModeLabel(intent.originalAccessAcl ? aclMode(intent.originalAccessAcl) : null) }}。
      </p>
      <AclEntryList v-if="intent.kind" :entries="intent.originalAccessAcl" label="原access ACL" />
      <AclEntryList v-if="intent.kind" :entries="intent.originalDefaultAcl" label="原default ACL" />
      <AclEntryList
        v-if="intent.kind"
        :entries="intent.command.entries"
        :label="`目标${intent.command.aclType} ACL`"
      />
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
        ACL变更：{{
          intent.aclConfirmed === true
            ? '已确认'
            : intent.aclConfirmed === false
              ? '已确认未发生'
              : '尚未确认'
        }}； HDD元数据：{{
          intent.historyVerified
            ? '已验证留存'
            : intent.historyMayExist
              ? '可能存在，尚未验证'
              : '未记录写入'
        }}。 元数据留存不包含文件内容。原Root回执：{{
          intent.receipt?.state ?? '尚无可信回执'
        }}，不等于当前对象状态。
      </p>
      <p v-if="intent.resultCode">
        <el-tooltip :content="intent.resultCode"
          ><span>{{
            aclResult(
              intent.state === 'Expired' || (intent.state === 'Ready' && expired)
                ? 'CONTAINER_ACL_PLAN_EXPIRED'
                : intent.resultCode,
            )
          }}</span></el-tooltip
        >
      </p>
      <RouterLink v-if="intent.state === 'Unknown'" to="/files">前往ACL修改异常处置</RouterLink>
    </div>
    <p v-if="hint && !intent" class="container-acl__path">
      原登记引用：<code>{{ hint.key }}</code>
    </p>
    <p v-if="error" role="alert" class="container-acl__error">{{ error }}</p>
    <template #footer>
      <el-button @click="dialog = false">{{ hint ? '关闭' : '返回' }}</el-button>
      <el-button
        v-if="!hint"
        type="primary"
        :loading="busy"
        :disabled="disabled"
        @click="prepare()"
        >{{ restoring ? '生成ACL恢复预检' : '生成ACL修改预检' }}</el-button
      >
      <el-button v-if="hint" :disabled="busy || confirmingCancel" @click="query"
        >查询原ACL修改任务</el-button
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
        >{{ restoring ? '确认ACL历史恢复' : '确认ACL修改' }}</el-button
      >
      <el-button
        v-if="intent && !aclTerminal(intent.state) && intent.state !== 'Unknown'"
        :disabled="busy || confirmingCancel"
        @click="cancel"
        >请求取消ACL修改</el-button
      >
    </template>
  </el-dialog>
</template>

<style scoped>
:global(.container-acl-dialog) {
  display: flex;
  flex-direction: column;
  max-height: calc(100dvh - 24px);
  margin-block: 12px;
}
:global(.container-acl-dialog .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
:global(.container-acl-dialog .el-dialog__header),
:global(.container-acl-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
@media (max-width: 600px) {
  :global(.container-acl-dialog) {
    margin-block: 12px;
  }
}
.container-acl {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding-block: 8px;
  font-size: 13px;
}
.container-acl__path,
.container-acl__summary,
.container-acl__error {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.container-acl__summary p {
  margin-block: 10px;
}
.container-acl__error {
  color: var(--el-color-danger);
  width: 100%;
}
.container-acl__entry {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 8px;
  padding: 10px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
  align-items: center;
}
.container-acl__entry > * {
  min-width: 0;
}
.container-acl__add {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  padding-block: 12px;
}
.container-acl__add .el-select {
  width: 150px;
}
.container-acl__entry .el-button {
  margin-left: 0;
}
</style>
