<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { LocationQueryRaw } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import { filePath } from '@/services/fileManagement'
import type { FileEntry, FileRoot } from '@/services/fileManagement'
import {
  assertRecyclingIntent,
  canRecycleDirectory,
  canRecycleRoot,
  recyclingState,
  recyclingResult,
  treeTerminal,
  treeUuid,
  validRecyclingCommand,
  sameRecyclingCommand,
} from '@/services/fileTreeRecyclings'
import type { TreeRecyclingCommand, TreeRecyclingIntent } from '@/services/fileTreeRecyclings'

const props = defineProps<{ roots: FileRoot[]; rootId: string; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; changed: []; failure: [reason: unknown] }>()
const route = useRoute(),
  router = useRouter()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  intent = ref<TreeRecyclingIntent | null>(null)
const source = ref<TreeRecyclingCommand['source'] | null>(null),
  expectedVersion = ref('')
const pendingKey = ref(''),
  referenceId = ref(''),
  now = ref(Date.now())
const verificationFailed = ref(false),
  confirmationUncertain = ref(false)
const active = computed(() => !!intent.value && !treeTerminal(intent.value.state))
const canConfirm = computed(
  () =>
    !verificationFailed.value &&
    !confirmationUncertain.value &&
    intent.value?.state === 'Ready' &&
    !!intent.value.confirmation &&
    Date.parse(intent.value.confirmation.expiresAtUtc) > now.value,
)
let generation = 0,
  disposed = false,
  timer: number | undefined,
  clockTimer: number | undefined
let controller: AbortController | undefined
const frozenConfirmation = ref<{ planHash: string; confirmed: boolean; version: number } | null>(
  null,
)
let notified = ''

function reset() {
  generation++
  controller?.abort()
  controller = undefined
  window.clearTimeout(timer)
  window.clearInterval(clockTimer)
  busy.value = false
  dialog.value = false
  error.value = ''
  intent.value = null
  source.value = null
  pendingKey.value = ''
  referenceId.value = ''
  frozenConfirmation.value = null
  verificationFailed.value = false
  confirmationUncertain.value = false
  notified = ''
  emit('locked', false)
}
function owned(token: number) {
  return !disposed && token === generation && authorized.value
}
function fail(reason: unknown) {
  error.value =
    reason instanceof ApiError && reason.status === 404
      ? '尚未查到原任务；可能仍在登记，或原登录已更换。保留引用后重查；原登录失效可使用顶部未知任务处置，不自动新建。'
      : reason instanceof Error
        ? reason.message
        : '无法查询原目录任务'
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    reset()
    emit('failure', reason)
  }
}
async function setReference(id: string) {
  if (route.query.treeTrashId === id && !route.query.treeTrashKey) return
  const query: LocationQueryRaw = { ...route.query, treeTrashId: id }
  delete query.treeTrashKey
  await router.replace({ query })
}
async function accept(value: TreeRecyclingIntent, token: number, expected?: string) {
  if (!owned(token)) return
  verificationFailed.value = true
  assertRecyclingIntent(value, expected)
  if (intent.value && !sameRecyclingCommand(intent.value.command, value.command))
    throw new Error('原任务源目录或版本已改变；保留引用，不确认、不执行')
  if (
    intent.value?.entryCount !== null &&
    intent.value?.entryCount !== undefined &&
    (intent.value.entryCount !== value.entryCount || intent.value.totalBytes !== value.totalBytes)
  )
    throw new Error('原目录清单已改变；保留引用，不确认、不执行')
  if (
    intent.value?.captureVerified &&
    (!value.captureVerified ||
      intent.value.archiveBytes !== value.archiveBytes ||
      intent.value.archiveSha256 !== value.archiveSha256 ||
      intent.value.captureId !== value.captureId)
  )
    throw new Error('原 HDD 捕获证明已改变；保留引用，不确认、不执行')
  verificationFailed.value = false
  confirmationUncertain.value = false
  intent.value = value
  referenceId.value = value.id
  pendingKey.value = ''
  error.value = ''
  frozenConfirmation.value = null
  emit('locked', !treeTerminal(value.state))
  await setReference(value.id)
  if (!owned(token)) return
  if (value.state === 'Succeeded' && notified !== value.id) {
    notified = value.id
    emit('changed')
  }
  schedule()
}
function schedule() {
  window.clearTimeout(timer)
  window.clearInterval(clockTimer)
  if (!authorized.value || !intent.value) return
  if (intent.value.state === 'Ready') {
    clockTimer = window.setInterval(() => {
      now.value = Date.now()
    }, 1000)
  } else if (!treeTerminal(intent.value.state) && intent.value.state !== 'Unknown')
    timer = window.setTimeout(() => void reference(false), 2000)
}
async function reference(manual = true) {
  if (!authorized.value || busy.value || (!referenceId.value && !pendingKey.value)) return
  const token = generation
  busy.value = true
  controller = new AbortController()
  try {
    const path = referenceId.value ? referenceId.value : `by-key/${pendingKey.value}`
    const result = await apiRequest<TreeRecyclingIntent>(`files/tree-recyclings/${path}`, {
      signal: controller.signal,
    })
    await accept(result, token, referenceId.value || undefined)
  } catch (reason) {
    if (owned(token)) {
      fail(reason)
      if (!manual) window.clearTimeout(timer)
    }
  } finally {
    if (owned(token)) busy.value = false
  }
}
function open(entry: FileEntry) {
  if (
    !authorized.value ||
    props.disabled ||
    !canRecycleDirectory(
      props.roots.find((root) => root.id === props.rootId),
      entry,
    )
  )
    return
  if (intent.value || pendingKey.value || referenceId.value) {
    dialog.value = true
    return
  }
  reset()
  source.value = { rootId: props.rootId, path: filePath(props.path, entry.name) }
  expectedVersion.value = entry.version
  dialog.value = true
}
async function queue() {
  if (
    !authorized.value ||
    busy.value ||
    !source.value ||
    intent.value ||
    pendingKey.value ||
    referenceId.value
  )
    return
  const command: TreeRecyclingCommand = {
    source: { ...source.value },
    expectedVersion: expectedVersion.value,
  }
  if (
    props.disabled ||
    !validRecyclingCommand(command) ||
    !canRecycleRoot(props.roots.find((root) => root.id === command.source.rootId))
  ) {
    error.value = '目录操作正在进行或源目录无效，请先核实其他任务'
    return
  }
  const token = generation,
    key = createIdempotencyKey()
  busy.value = true
  pendingKey.value = key
  controller = new AbortController()
  try {
    // Save only an opaque registration reference BEFORE sending. Reload and
    // re-login recover by GET, never resubmit a POST from stored browser data.
    const query: LocationQueryRaw = { ...route.query, treeTrashKey: key }
    delete query.treeTrashId
    await router.replace({ query })
    if (!owned(token)) return
    const result = await apiRequest<TreeRecyclingIntent>('files/tree-recyclings', {
      method: 'POST',
      body: JSON.stringify(command),
      headers: { 'Idempotency-Key': key },
      signal: controller.signal,
    })
    assertRecyclingIntent(result)
    if (!sameRecyclingCommand(result.command, command))
      throw new Error('计划目标与原请求不一致，保留原引用')
    await accept(result, token)
  } catch (reason) {
    if (owned(token)) fail(reason)
  } finally {
    if (owned(token)) busy.value = false
  }
}
async function confirm() {
  const row = intent.value
  if (!authorized.value || busy.value || !row || !canConfirm.value) return
  if (!frozenConfirmation.value)
    frozenConfirmation.value = {
      planHash: row.confirmation!.planHash,
      confirmed: true,
      version: row.version,
    }
  const token = generation
  busy.value = true
  confirmationUncertain.value = true
  controller = new AbortController()
  try {
    await accept(
      await apiRequest<TreeRecyclingIntent>(`files/tree-recyclings/${row.id}/confirm`, {
        method: 'POST',
        body: JSON.stringify(frozenConfirmation.value),
        headers: { 'Idempotency-Key': row.id },
        signal: controller.signal,
      }),
      token,
      row.id,
    )
  } catch (reason) {
    if (owned(token)) fail(reason)
  } finally {
    if (owned(token)) busy.value = false
  }
}
async function cancel() {
  const row = intent.value
  if (!authorized.value || busy.value || !row || treeTerminal(row.state)) return
  const token = generation
  try {
    await ElMessageBox.confirm(
      '未确认计划将禁止迟到确认；已开始的回收会请求取消，最终以原任务回执为准，不假定已停止或撤销。',
      '取消目录操作',
      { confirmButtonText: '确认取消', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    busy.value = true
    controller = new AbortController()
    await accept(
      await apiRequest<TreeRecyclingIntent>(`files/tree-recyclings/${row.id}/cancel`, {
        method: 'POST',
        headers: { 'Idempotency-Key': row.id },
        signal: controller.signal,
      }),
      token,
      row.id,
    )
  } catch (reason) {
    if (owned(token) && reason instanceof Error) fail(reason)
  } finally {
    if (owned(token)) busy.value = false
  }
}
async function acknowledge() {
  const row = intent.value
  if (!authorized.value || busy.value || row?.state !== 'Unknown') return
  const token = generation
  try {
    await ElMessageBox.confirm(
      '确认接受未知历史结果并解除本任务路径锁。不会标为成功、重做回收或删除HDD归档/私有原件；请先核实源目录与任务记录。',
      '确认未知结果',
      { confirmButtonText: '已核实，确认处置', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    busy.value = true
    controller = new AbortController()
    await apiRequest(`files/tree-recyclings/${row.id}/acknowledge`, {
      method: 'POST',
      body: JSON.stringify({ version: row.version, confirmed: true }),
      headers: { 'Idempotency-Key': row.id },
      signal: controller.signal,
    })
    await accept(
      await apiRequest<TreeRecyclingIntent>(`files/tree-recyclings/${row.id}`, {
        signal: controller.signal,
      }),
      token,
      row.id,
    )
  } catch (reason) {
    if (owned(token) && reason instanceof Error) fail(reason)
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
        '仅关闭此页引用，不取消、不重做任何原任务。请先在任务中心核实原计划；未确认的计划会自动过期。',
        '关闭任务引用',
        { confirmButtonText: '仅关闭引用', cancelButtonText: '保留引用' },
      )
    if (!owned(token)) return
    const query = { ...route.query }
    delete query.treeTrashId
    delete query.treeTrashKey
    reset()
    await router.replace({ query })
    ElMessage.info('引用已关闭，原任务与审计保留')
  } catch {
    /* A canceled dialog does not discard a reference. */
  }
}
watch(
  [
    () => authSession.user.value?.subject,
    authorized,
    () => route.query.treeTrashId,
    () => route.query.treeTrashKey,
  ],
  ([subject, allowed, id, key], old) => {
    if (!allowed || !subject) {
      reset()
      return
    }
    if (old && subject !== old[0]) reset()
    if (typeof id === 'string' && treeUuid.test(id)) {
      if (id === intent.value?.id || id === referenceId.value) return
      reset()
      referenceId.value = id
      dialog.value =
        !route.query.treeCopyId &&
        !route.query.treeCopyKey &&
        !route.query.treeMoveId &&
        !route.query.treeMoveKey &&
        !route.query.treePackId &&
        !route.query.treePackKey &&
        !route.query.treeUnpackId &&
        !route.query.treeUnpackKey
      emit('locked', true)
      void reference()
    } else if (typeof key === 'string' && treeUuid.test(key) && key !== pendingKey.value) {
      reset()
      pendingKey.value = key
      dialog.value =
        !route.query.treeCopyId &&
        !route.query.treeCopyKey &&
        !route.query.treeMoveId &&
        !route.query.treeMoveKey &&
        !route.query.treePackId &&
        !route.query.treePackKey &&
        !route.query.treeUnpackId &&
        !route.query.treeUnpackKey
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
  <div
    v-if="authorized && (intent || pendingKey || referenceId)"
    class="tree-trash-reference"
    role="status"
  >
    <span>{{ intent ? recyclingState(intent.state) : '原计划登记结果待核实' }}</span>
    <el-button link type="primary" @click="dialog = true">查看回收任务</el-button>
    <el-button link :disabled="busy" @click="release">关闭引用</el-button>
  </div>
  <el-dialog
    v-if="authorized"
    v-model="dialog"
    title="回收目录 · 先验证 HDD 归档"
    width="min(640px, calc(100vw - 24px))"
    align-center
    :close-on-click-modal="false"
    class="tree-trash-dialog"
  >
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <template v-if="intent">
      <el-alert
        :title="recyclingState(intent.state)"
        :type="
          intent.state === 'Succeeded' ? 'success' : intent.state === 'Unknown' ? 'warning' : 'info'
        "
        :closable="false"
      />
      <dl class="tree-trash-details">
        <dt>源目录</dt>
        <dd>{{ intent.command.source.rootId }} / {{ intent.command.source.path }}</dd>
        <dt>完整清单</dt>
        <dd>
          {{ intent.entryCount ?? '尚未完成扫描' }} 项 ·
          {{ intent.totalBytes === null ? '待统计' : formatBytes(intent.totalBytes) }}
        </dd>
        <dt>HDD 捕获</dt>
        <dd>
          {{
            intent.captureVerified
              ? '归档已验证'
              : intent.captureMayBeRetained === null
                ? '尚未取得回执'
                : intent.captureMayBeRetained
                  ? '可能保留未验证归档'
                  : '未取得已验证归档'
          }}
        </dd>
        <template v-if="intent.captureVerified"
          ><dt>归档大小</dt>
          <dd>{{ formatBytes(intent.archiveBytes!) }}</dd>
          <dt>SHA256</dt>
          <dd>{{ intent.archiveSha256 }}</dd>
          <dt>捕获 ID</dt>
          <dd>{{ intent.captureId }}</dd></template
        >
        <dt>源移除</dt>
        <dd>
          {{
            intent.sourceRemoved === null
              ? '未知，不能推断未发生'
              : intent.sourceRemoved
                ? '原任务已移除源目录路径'
                : '未取得源移除完成证明'
          }}
        </dd>
        <dt>原件保留</dt>
        <dd>
          {{
            intent.originalsRetained === null
              ? '未知'
              : intent.originalsRetained
                ? '原件已私有保留'
                : '未取得完整原件保留证明'
          }}
        </dd>
        <dt>计划任务</dt>
        <dd>{{ intent.planningJobId }}</dd>
        <template v-if="intent.executionJobId"
          ><dt>执行任务</dt>
          <dd>{{ intent.executionJobId }}</dd></template
        >
      </dl>
      <p v-if="intent.resultCode" role="status">{{ recyclingResult(intent.resultCode) }}</p>
      <p v-if="intent.confirmation">{{ intent.confirmation.warning }}</p>
      <p
        v-if="
          intent.state === 'Ready' &&
          intent.confirmation &&
          Date.parse(intent.confirmation.expiresAtUtc) <= now
        "
        role="alert"
      >
        计划已过期，请查询原任务；不会自动执行。
      </p>
      <p v-if="confirmationUncertain" role="alert">
        确认响应尚未核实，请查询原任务；不会重复确认或创建另一个执行任务。
      </p>
      <p v-if="intent.state === 'Succeeded'">
        这是原任务的历史完成回执，不保证源路径之后未被其他程序重新创建。HDD
        归档和私有原件均未彻底删除。
      </p>
      <p v-if="intent.state === 'Unknown'">
        保留路径锁与现场，不自动重做或清理。原登录失效时可在文件管理顶部使用“未知任务处置”，由当前管理员独立确认。
      </p>
    </template>
    <el-form
      v-else-if="source && !pendingKey && !referenceId"
      label-position="top"
      @submit.prevent="queue"
    >
      <el-form-item label="待回收的源目录"
        ><el-input :model-value="source.path" readonly
      /></el-form-item>
      <el-alert
        title="生成计划只扫描；确认后才验证 HDD 归档并逐文件保留原件。"
        type="warning"
        :closable="false"
        show-icon
      />
      <p>
        最多 10,000 项、16 GiB 内容。HDD
        核验失败不修改源目录；不直接彻底删除。目录包含受保护文件、链接或挂载时，整个计划拒绝。
      </p>
    </el-form>
    <p v-else>只查询原登记引用，不重新发送登记或执行请求。未找到不代表原请求从未提交。</p>
    <template #footer
      ><div class="tree-trash-buttons">
        <el-button @click="dialog = false">收起</el-button>
        <el-button v-if="intent || pendingKey || referenceId" :loading="busy" @click="reference()"
          >查询原任务</el-button
        >
        <el-button
          v-if="intent && !treeTerminal(intent.state)"
          :disabled="busy || verificationFailed"
          type="danger"
          plain
          @click="cancel"
          >取消操作</el-button
        >
        <el-button
          v-if="intent?.state === 'Unknown'"
          :disabled="busy || verificationFailed"
          type="warning"
          @click="acknowledge"
          >确认未知结果</el-button
        >
        <el-button
          v-if="intent?.state === 'Ready' || frozenConfirmation"
          :disabled="busy || !canConfirm"
          type="danger"
          @click="confirm"
          >确认回收目录</el-button
        >
        <el-button
          v-if="source && !intent && !pendingKey && !referenceId"
          :disabled="busy || props.disabled"
          type="primary"
          @click="queue"
          >生成回收计划</el-button
        >
      </div></template
    >
  </el-dialog>
</template>
<style scoped>
.tree-trash-reference {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
}
.tree-trash-details {
  display: grid;
  grid-template-columns: 76px minmax(0, 1fr);
  gap: 10px;
  margin: 18px 0;
}
.tree-trash-details dt {
  color: var(--el-text-color-secondary);
}
.tree-trash-details dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.tree-trash-buttons {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
}
.tree-trash-buttons :deep(.el-button + .el-button) {
  margin-left: 0;
}
:global(.tree-trash-dialog .el-dialog__body) {
  max-height: calc(100dvh - 200px);
  overflow-y: auto;
}
p {
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
  line-height: 1.7;
}
</style>
