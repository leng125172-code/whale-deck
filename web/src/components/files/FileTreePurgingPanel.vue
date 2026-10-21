<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { LocationQueryRaw } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import type { FileRoot } from '@/services/fileManagement'
import { captureKind } from '@/services/fileTreeRecyclings'
import type { TreeCapture } from '@/services/fileTreeRecyclings'
import {
  assertPurgingIntent,
  canPurgeCapture,
  canPurgeRoot,
  purgingFact,
  purgingFollows,
  purgingResult,
  purgingScope,
  purgingScopes,
  purgingState,
  purgingWarning,
  samePurgingRequest,
  treeTerminal,
  treeUuid,
  validPurgingRequest,
} from '@/services/fileTreePurgings'
import type {
  PurgingScope,
  TreePurgingIntent,
  TreePurgingRequest,
} from '@/services/fileTreePurgings'

const props = defineProps<{ roots: FileRoot[]; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; changed: []; failure: [reason: unknown] }>()
const route = useRoute(),
  router = useRouter()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref('')
const intent = ref<TreePurgingIntent | null>(null),
  selected = ref<TreeCapture | null>(null),
  scope = ref<PurgingScope | ''>('')
const referenceId = ref(''),
  pendingKey = ref(''),
  now = ref(Date.now())
const verificationFailed = ref(false),
  confirmationUncertain = ref(false)
const hasReference = computed(() => !!(intent.value || referenceId.value || pendingKey.value))
const active = computed(() => !!intent.value && !treeTerminal(intent.value.state))
const canConfirm = computed(
  () =>
    authorized.value &&
    !props.disabled &&
    !verificationFailed.value &&
    !confirmationUncertain.value &&
    intent.value?.state === 'Ready' &&
    !!intent.value.confirmation &&
    Date.parse(intent.value.confirmation.expiresAtUtc) > now.value &&
    canPurgeRoot(props.roots.find((root) => root.id === intent.value!.command.source.rootId)),
)
let generation = 0,
  disposed = false,
  timer: number | undefined,
  clockTimer: number | undefined,
  controller: AbortController | undefined
let notified = ''
function owned(token: number) {
  return !disposed && token === generation && authorized.value
}
function reset() {
  generation++
  controller?.abort()
  controller = undefined
  window.clearTimeout(timer)
  window.clearInterval(clockTimer)
  busy.value = dialog.value = verificationFailed.value = confirmationUncertain.value = false
  intent.value = null
  selected.value = null
  scope.value = ''
  referenceId.value = pendingKey.value = error.value = ''
  notified = ''
  emit('locked', false)
}
function fail(cause: unknown) {
  verificationFailed.value = true
  if (cause instanceof ApiError && [401, 403].includes(cause.status)) {
    reset()
    emit('failure', cause)
    return
  }
  error.value =
    cause instanceof ApiError
      ? cause.problem.code
        ? purgingResult(cause.problem.code)
        : cause.status === 404
          ? '尚未查到原引用，或原登录已更换；只查询原键，不自动新建。'
          : '服务暂不可用；保留原引用，不自动重新发送。'
      : '清理请求或响应无法核实；保留原引用，只查询，不自动重做。'
}
async function setReference(id: string) {
  if (route.query.treePurgeId === id && !route.query.treePurgeKey) return
  const query: LocationQueryRaw = { ...route.query, treePurgeId: id }
  delete query.treePurgeKey
  await router.replace({ query })
}
function schedule() {
  window.clearTimeout(timer)
  window.clearInterval(clockTimer)
  if (!authorized.value || !intent.value) return
  if (intent.value.state === 'Ready')
    clockTimer = window.setInterval(() => {
      now.value = Date.now()
    }, 1000)
  else if (!treeTerminal(intent.value.state) && intent.value.state !== 'Unknown')
    timer = window.setTimeout(() => void reference(), 2000)
}
async function accept(value: TreePurgingIntent, token: number, expected?: string) {
  if (!owned(token)) return
  verificationFailed.value = true
  assertPurgingIntent(value, expected)
  const capture = selected.value
  if (
    capture &&
    (value.command.captureId !== capture.id ||
      value.command.captureKind !== captureKind(capture) ||
      value.command.source.rootId !== capture.source.source.rootId ||
      value.command.source.path !== capture.source.source.path ||
      value.command.scope !== scope.value ||
      (value.entryCount !== null &&
        (value.entryCount !== capture.entryCount ||
          value.totalBytes !== capture.totalBytes ||
          value.archiveBytes !== capture.archiveBytes)))
  )
    throw new Error('清理捕获、范围或清单不一致')
  if (intent.value && !purgingFollows(intent.value, value))
    throw new Error('原清理绑定或已确认事实发生变化')
  intent.value = value
  referenceId.value = value.id
  pendingKey.value = ''
  error.value = ''
  verificationFailed.value = confirmationUncertain.value = false
  now.value = Date.now()
  emit('locked', !treeTerminal(value.state))
  await setReference(value.id)
  if (!owned(token)) return
  if (treeTerminal(value.state) && notified !== value.id) {
    notified = value.id
    emit('changed')
  }
  schedule()
}
async function reference() {
  if (!authorized.value || busy.value || !hasReference.value) return
  const token = generation
  busy.value = true
  controller = new AbortController()
  try {
    const suffix = referenceId.value || `by-key/${pendingKey.value}`
    await accept(
      await apiRequest<TreePurgingIntent>(`files/tree-purgings/${suffix}`, {
        signal: controller.signal,
      }),
      token,
      referenceId.value || undefined,
    )
  } catch (cause) {
    if (owned(token)) {
      window.clearTimeout(timer)
      fail(cause)
    }
  } finally {
    if (owned(token)) busy.value = false
  }
}
function open(capture: TreeCapture) {
  if (!authorized.value) return
  if (hasReference.value) {
    dialog.value = true
    ElMessage.info('已有原清理任务引用，请先核实；不会替换捕获或重新删除')
    return
  }
  if (
    props.disabled ||
    !canPurgeCapture(
      props.roots.find((root) => root.id === capture.source.source.rootId),
      capture,
    )
  )
    return
  reset()
  selected.value = JSON.parse(JSON.stringify(capture)) as TreeCapture
  dialog.value = true
}
async function queue() {
  const capture = selected.value
  if (
    !authorized.value ||
    busy.value ||
    props.disabled ||
    hasReference.value ||
    !capture ||
    !scope.value ||
    !canPurgeCapture(
      props.roots.find((root) => root.id === capture.source.source.rootId),
      capture,
      scope.value,
    )
  )
    return
  const command: TreePurgingRequest = { captureId: capture.id, scope: scope.value }
  if (!validPurgingRequest(command)) return
  const token = generation,
    key = createIdempotencyKey()
  busy.value = true
  pendingKey.value = key
  controller = new AbortController()
  try {
    // Persist only an opaque key before POST; navigation or a lost response
    // can recover by GET, never by replaying a destructive browser payload.
    const query: LocationQueryRaw = { ...route.query, treePurgeKey: key }
    delete query.treePurgeId
    await router.replace({ query })
    if (!owned(token)) return
    const result = await apiRequest<TreePurgingIntent>('files/tree-purgings', {
      method: 'POST',
      body: JSON.stringify(command),
      headers: { 'Idempotency-Key': key },
      signal: controller.signal,
    })
    assertPurgingIntent(result)
    if (!samePurgingRequest(result.command, command)) throw new Error('原清理请求发生变化')
    await accept(result, token)
  } catch (cause) {
    if (owned(token)) fail(cause)
  } finally {
    if (owned(token)) busy.value = false
  }
}
async function confirm() {
  const row = intent.value
  if (!authorized.value || busy.value || !row?.confirmation || !canConfirm.value) return
  const token = generation,
    proof = row.confirmation
  busy.value = true
  try {
    await ElMessageBox.confirm(
      `${purgingScope(row.command.scope)}。${proof.warning}`,
      '永久清理不可撤销',
      {
        type: 'warning',
        confirmButtonText: '确认永久清理',
        cancelButtonText: '暂不清理',
        distinguishCancelAndClose: true,
      },
    )
    if (
      !owned(token) ||
      intent.value !== row ||
      !canConfirm.value ||
      Date.parse(proof.expiresAtUtc) <= Date.now()
    )
      return
    controller = new AbortController()
    confirmationUncertain.value = true
    await accept(
      await apiRequest<TreePurgingIntent>(`files/tree-purgings/${row.id}/confirm`, {
        method: 'POST',
        body: JSON.stringify({ planHash: proof.planHash, confirmed: true, version: row.version }),
        headers: { 'Idempotency-Key': row.id },
        signal: controller.signal,
      }),
      token,
      row.id,
    )
  } catch (cause) {
    if (owned(token) && cause !== 'cancel' && cause !== 'close') fail(cause)
  } finally {
    if (owned(token)) busy.value = false
  }
}
async function cancel() {
  const row = intent.value
  if (
    !authorized.value ||
    busy.value ||
    verificationFailed.value ||
    !row ||
    treeTerminal(row.state)
  )
    return
  const token = generation
  busy.value = true
  try {
    await ElMessageBox.confirm(
      '请求取消原清理。已删除内容不能恢复；不会假定清理已停止，最终以原任务的组件事实为准。',
      '取消目录清理',
      { confirmButtonText: '确认取消', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    controller = new AbortController()
    await accept(
      await apiRequest<TreePurgingIntent>(`files/tree-purgings/${row.id}/cancel`, {
        method: 'POST',
        headers: { 'Idempotency-Key': row.id },
        signal: controller.signal,
      }),
      token,
      row.id,
    )
  } catch (cause) {
    if (owned(token) && cause !== 'cancel' && cause !== 'close') fail(cause)
  } finally {
    if (owned(token)) busy.value = false
  }
}
async function acknowledge() {
  const row = intent.value
  if (!authorized.value || busy.value || verificationFailed.value || row?.state !== 'Unknown')
    return
  const token = generation
  busy.value = true
  try {
    await ElMessageBox.confirm(
      '只封存原清理并接受未知历史，不续删、不恢复。已确认删除事实永久保留，其余副本可能已删除；核实后才解除原锁。',
      '接受清理未知结果',
      { confirmButtonText: '已核实，接受未知结果', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    controller = new AbortController()
    verificationFailed.value = true
    await apiRequest(`files/tree-purgings/${row.id}/acknowledge`, {
      method: 'POST',
      body: JSON.stringify({ version: row.version, confirmed: true }),
      headers: { 'Idempotency-Key': row.id },
      signal: controller.signal,
    })
    await accept(
      await apiRequest<TreePurgingIntent>(`files/tree-purgings/${row.id}`, {
        signal: controller.signal,
      }),
      token,
      row.id,
    )
  } catch (cause) {
    if (owned(token) && cause !== 'cancel' && cause !== 'close') fail(cause)
  } finally {
    if (owned(token)) busy.value = false
  }
}
async function release() {
  if (busy.value) return
  const token = generation
  try {
    if (active.value || pendingKey.value || error.value)
      await ElMessageBox.confirm(
        '仅关闭此页引用，不取消、不重做原清理。后台任务与永久审计保留，请在任务中心核实。',
        '关闭清理引用',
        { confirmButtonText: '仅关闭引用', cancelButtonText: '保留引用' },
      )
    if (!owned(token)) return
    const query = { ...route.query }
    delete query.treePurgeId
    delete query.treePurgeKey
    reset()
    await router.replace({ query })
  } catch {
    /* Canceled dialogs preserve the original reference. */
  }
}
function noOtherReference() {
  return ![
    'treeCopyId',
    'treeCopyKey',
    'treeMoveId',
    'treeMoveKey',
    'treePackId',
    'treePackKey',
    'treeUnpackId',
    'treeUnpackKey',
    'treeTrashId',
    'treeTrashKey',
    'treeRestoreId',
    'treeRestoreKey',
    'treeOverwriteRestoreId',
    'treeOverwriteRestoreKey',
    'fileRecoveryId',
    'fileRecoveryKey',
  ].some((key) => route.query[key])
}
watch(
  [
    () => authSession.user.value?.subject,
    authorized,
    () => route.query.treePurgeId,
    () => route.query.treePurgeKey,
  ],
  ([subject, allowed, id, key], old) => {
    if (!allowed || !subject) {
      reset()
      return
    }
    if (old && subject !== old[0]) reset()
    if (typeof id === 'string' && treeUuid.test(id) && id !== referenceId.value) {
      reset()
      referenceId.value = id
      dialog.value = noOtherReference()
      emit('locked', true)
      void reference()
    } else if (!id && typeof key === 'string' && treeUuid.test(key) && key !== pendingKey.value) {
      reset()
      pendingKey.value = key
      dialog.value = noOtherReference()
      emit('locked', true)
      void reference()
    }
  },
  { immediate: true },
)
watch([busy, active, pendingKey, referenceId], () =>
  emit(
    'locked',
    busy.value || active.value || (!intent.value && !!(pendingKey.value || referenceId.value)),
  ),
)
onBeforeUnmount(() => {
  disposed = true
  reset()
})
defineExpose({ open })
</script>

<template>
  <div v-if="authorized && hasReference" class="tree-purge-reference" role="status">
    <span>{{ intent ? purgingState(intent.state) : '原清理登记结果待核实' }}</span>
    <el-button link type="primary" @click="dialog = true">查看清理任务</el-button>
    <el-button link :disabled="busy" @click="release">关闭引用</el-button>
  </div>
  <el-dialog
    v-if="authorized"
    v-model="dialog"
    title="永久清理目录历史组件"
    width="min(680px, calc(100vw - 24px))"
    align-center
    :close-on-click-modal="false"
    class="tree-purge-dialog"
  >
    <el-alert
      title="永久清理不可撤销，可能删除最后副本。只处理选定捕获的历史组件，不删除原路径上当前的新内容；原记录和审计永久保留。"
      type="warning"
      :closable="false"
      show-icon
    />
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <template v-if="intent">
      <el-alert
        :title="purgingState(intent.state)"
        :type="
          intent.state === 'Succeeded' ? 'success' : intent.state === 'Unknown' ? 'warning' : 'info'
        "
        :closable="false"
      />
      <dl class="tree-purge-details">
        <dt>历史源</dt>
        <dd>{{ intent.command.source.rootId }} / {{ intent.command.source.path }}</dd>
        <dt>捕获 ID</dt>
        <dd>{{ intent.command.captureId }}</dd>
        <dt>捕获类型</dt>
        <dd>{{ intent.command.captureKind === 'Overwrite' ? '覆盖前备份' : '目录回收' }}</dd>
        <dt>清理范围</dt>
        <dd>{{ purgingScope(intent.command.scope) }}</dd>
        <dt>原完整清单</dt>
        <dd>
          {{ intent.entryCount ?? '待统计' }} 项 ·
          {{ intent.totalBytes === null ? '待统计' : formatBytes(intent.totalBytes) }}
        </dd>
        <dt>原归档大小</dt>
        <dd>{{ intent.archiveBytes === null ? '待核实' : formatBytes(intent.archiveBytes) }}</dd>
        <dt>删除开始</dt>
        <dd>
          {{
            intent.deletionStarted === null
              ? '结果待核实'
              : intent.deletionStarted
                ? '已开始，不代表全部完成'
                : '未开始'
          }}
        </dd>
        <dt>HDD归档</dt>
        <dd>{{ purgingFact(intent.archiveRemoved) }}</dd>
        <dt>保留原件</dt>
        <dd>{{ purgingFact(intent.originalsRemoved) }}</dd>
        <dt>计划任务</dt>
        <dd>{{ intent.planningJobId }}</dd>
        <dt>执行任务</dt>
        <dd>{{ intent.executionJobId || '尚未确认' }}</dd>
      </dl>
      <p v-if="intent.resultCode" role="status">{{ purgingResult(intent.resultCode) }}</p>
      <p v-if="intent.confirmation">{{ intent.confirmation.warning }}</p>
      <p
        v-if="
          intent.state === 'Ready' &&
          intent.confirmation &&
          Date.parse(intent.confirmation.expiresAtUtc) <= now
        "
        role="alert"
      >
        原计划已过期，不会自动执行；请查询原任务后关闭引用，再显式生成新计划。
      </p>
      <p v-if="confirmationUncertain" role="alert">
        确认响应尚未核实，只查询原任务；不会重复发送确认。
      </p>
      <p v-if="intent.state === 'Unknown' || intent.state === 'Acknowledged'">
        未知不等于未删除，也不代表副本仍在；已建立的删除事实不会被确认未知结果清除，不自动续删或回滚。
      </p>
      <p v-if="intent.state === 'Succeeded'">
        只证明本次所选组件的清理。未选组件没有本次删除，不保证其仍可恢复；原捕获记录不是备份本体。
      </p>
    </template>
    <el-form v-else-if="selected && !hasReference" label-position="top" @submit.prevent="queue">
      <el-form-item label="历史源目录"
        ><el-input
          :model-value="`${selected.source.source.rootId} / ${selected.source.source.path}`"
          readonly
      /></el-form-item>
      <el-form-item label="捕获 ID"><el-input :model-value="selected.id" readonly /></el-form-item>
      <el-form-item label="清理范围（必须明确选择）">
        <el-select
          v-model="scope"
          aria-label="清理范围"
          placeholder="请选择清理范围"
          :disabled="busy"
        >
          <el-option
            v-for="option in purgingScopes"
            :key="option"
            :value="option"
            :label="purgingScope(option)"
            :disabled="
              !canPurgeCapture(
                roots.find((root) => root.id === selected!.source.source.rootId),
                selected,
                option,
              )
            "
          />
        </el-select>
      </el-form-item>
      <el-alert v-if="scope" :title="purgingWarning(scope)" type="warning" :closable="false" />
      <p v-if="selected.archiveState === 'Uncertain' || selected.originalsState === 'Uncertain'">
        此捕获存在历史清理不确定性；新计划会重新验证所选组件，不能按记录推断副本仍存在。
      </p>
      <p>
        生成计划只读取并验证本次所选组件，不开始删除；计划就绪后仍需明确确认。大小为原清单/归档统计，不是保证可释放的磁盘空间。
      </p>
    </el-form>
    <p v-else>只查询原登记键；未找到不代表原请求从未提交，不自动重新登记。</p>
    <template #footer
      ><div class="tree-purge-buttons">
        <el-button @click="dialog = false">收起</el-button>
        <el-button v-if="hasReference" :loading="busy" @click="reference">查询原任务</el-button>
        <el-button
          v-if="intent && !treeTerminal(intent.state)"
          :disabled="busy || verificationFailed"
          type="danger"
          plain
          @click="cancel"
          >取消清理</el-button
        >
        <el-button
          v-if="intent?.state === 'Unknown'"
          :disabled="busy || verificationFailed"
          type="warning"
          @click="acknowledge"
          >确认未知结果</el-button
        >
        <el-button
          v-if="intent?.state === 'Ready' && !confirmationUncertain"
          :disabled="busy || !canConfirm"
          type="danger"
          @click="confirm"
          >确认清理</el-button
        >
        <el-button
          v-if="selected && !hasReference"
          :disabled="
            busy ||
            disabled ||
            !scope ||
            !canPurgeCapture(
              roots.find((root) => root.id === selected!.source.source.rootId),
              selected,
              scope,
            )
          "
          type="primary"
          @click="queue"
          >生成清理计划</el-button
        >
      </div></template
    >
  </el-dialog>
</template>

<style scoped>
.tree-purge-reference {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
}
.tree-purge-details {
  display: grid;
  grid-template-columns: 96px minmax(0, 1fr);
  gap: 10px;
  margin: 18px 0;
  font-size: 13px;
}
.tree-purge-details dt {
  color: var(--el-text-color-secondary);
}
.tree-purge-details dd {
  margin: 0;
  min-width: 0;
  overflow-wrap: anywhere;
}
.tree-purge-buttons {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
}
.tree-purge-buttons :deep(.el-button + .el-button) {
  margin-left: 0;
}
:global(.tree-purge-dialog .el-dialog__body) {
  max-height: calc(100dvh - 200px);
  overflow-y: auto;
  overflow-x: hidden;
}
:global(.tree-purge-dialog .el-alert) {
  margin-bottom: 12px;
}
.el-select {
  width: 100%;
}
p {
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
  line-height: 1.7;
  font-size: 12px;
}
@media (max-width: 480px) {
  .tree-purge-details {
    grid-template-columns: minmax(0, 1fr);
    gap: 4px;
  }
}
</style>
