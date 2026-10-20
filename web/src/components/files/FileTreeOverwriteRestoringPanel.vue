<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { LocationQueryRaw } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import type { FileRoot, FileEntry } from '@/services/fileManagement'
import { filePath } from '@/services/fileManagement'
import type { TreeCapture } from '@/services/fileTreeRecyclings'
import {
  assertOverwriteIntent,
  canOverwriteCapture,
  canReceiveOverwrite,
  overwriteState,
  overwriteResult,
  treeTerminal,
  treeUuid,
  validOverwriteRequest,
  sameOverwriteRequest,
  assertOverwriteProgress,
  captureMatchesOverwrite,
  canOverwriteDirectory,
} from '@/services/fileTreeOverwriteRestorings'
import type {
  TreeOverwriteRestoringCommand,
  TreeOverwriteRestoringRequest,
  TreeOverwriteRestoringIntent,
} from '@/services/fileTreeOverwriteRestorings'

const props = defineProps<{
  roots: FileRoot[]
  rootId: string
  path: string
  entries: FileEntry[]
  disabled: boolean
}>()
const emit = defineEmits<{ locked: [value: boolean]; changed: []; failure: [reason: unknown] }>()
const route = useRoute(),
  router = useRouter()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  intent = ref<TreeOverwriteRestoringIntent | null>(null)
const source = ref<TreeOverwriteRestoringCommand['source'] | null>(null),
  selectedCapture = ref<TreeCapture | null>(null),
  targetRoot = ref(''),
  targetPath = ref(''),
  targetName = ref(''),
  targetExpectedVersion = ref('')
const pendingKey = ref(''),
  referenceId = ref(''),
  now = ref(Date.now())
const verificationFailed = ref(false),
  confirmationUncertain = ref(false)
const targets = computed(() => props.roots.filter(canReceiveOverwrite))
const currentTargets = computed(() =>
  props.entries.filter((entry) =>
    canOverwriteDirectory(
      props.roots.find((root) => root.id === props.rootId),
      entry,
    ),
  ),
)
function chooseTarget(name: string) {
  const entry = currentTargets.value.find((item) => item.name === name)
  targetName.value = entry?.name || ''
  targetRoot.value = entry ? props.rootId : ''
  targetPath.value = entry ? filePath(props.path, entry.name) : ''
  targetExpectedVersion.value = entry?.version || ''
}
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
  targetName.value = ''
  targetRoot.value = ''
  targetPath.value = ''
  targetExpectedVersion.value = ''
  selectedCapture.value = null
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
      ? '尚未查到原任务；可能仍在登记，或原登录会话已更换。保留引用后重查，不自动新建。'
      : reason instanceof Error
        ? reason.message
        : '无法查询原目录任务'
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    reset()
    emit('failure', reason)
  }
}
async function setReference(id: string) {
  if (route.query.treeOverwriteRestoreId === id && !route.query.treeOverwriteRestoreKey) return
  const query: LocationQueryRaw = { ...route.query, treeOverwriteRestoreId: id }
  delete query.treeOverwriteRestoreKey
  await router.replace({ query })
}
async function accept(value: TreeOverwriteRestoringIntent, token: number, expected?: string) {
  if (!owned(token)) return
  verificationFailed.value = true
  assertOverwriteIntent(value, expected)
  const capture = selectedCapture.value
  if (capture && !captureMatchesOverwrite(capture, value))
    throw new Error('原捕获与覆盖恢复计划的归档证明不一致；保留引用，不确认、不执行')
  if (intent.value) assertOverwriteProgress(intent.value, value)
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
    const result = await apiRequest<TreeOverwriteRestoringIntent>(
      `files/tree-overwrite-restorings/${path}`,
      {
        signal: controller.signal,
      },
    )
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
function open(capture: TreeCapture) {
  if (
    !authorized.value ||
    !canOverwriteCapture(
      props.roots.find((root) => root.id === capture.source.source.rootId),
      capture,
    )
  )
    return
  if (intent.value || pendingKey.value || referenceId.value) {
    dialog.value = true
    ElMessage.info('已有原恢复任务引用；先核实并关闭引用，再选择另一份捕获，不会替换或重做原任务')
    return
  }
  if (props.disabled) return
  reset()
  selectedCapture.value = JSON.parse(JSON.stringify(capture)) as TreeCapture
  source.value = { ...capture.source.source }
  chooseTarget(currentTargets.value[0]?.name || '')
  dialog.value = true
}

async function queue() {
  if (
    !authorized.value ||
    busy.value ||
    !source.value ||
    !selectedCapture.value ||
    !canOverwriteCapture(
      props.roots.find((root) => root.id === source.value!.rootId),
      selectedCapture.value,
    ) ||
    intent.value ||
    pendingKey.value ||
    referenceId.value
  )
    return
  const command: TreeOverwriteRestoringRequest = {
    captureId: selectedCapture.value.id,
    target: { rootId: targetRoot.value, path: targetPath.value },
    targetExpectedVersion: targetExpectedVersion.value,
  }
  if (
    !validOverwriteRequest(command) ||
    !targets.value.some((root) => root.id === command.target.rootId) ||
    command.target.rootId !== props.rootId ||
    !currentTargets.value.some(
      (entry) =>
        filePath(props.path, entry.name) === command.target.path &&
        entry.version === command.targetExpectedVersion,
    )
  ) {
    error.value =
      '请选择当前列表中具备覆盖授权的现存目录；目录或版本已变化时，请重新读取列表并选择目标'
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
    const query: LocationQueryRaw = { ...route.query, treeOverwriteRestoreKey: key }
    delete query.treeOverwriteRestoreId
    await router.replace({ query })
    if (!owned(token)) return
    const result = await apiRequest<TreeOverwriteRestoringIntent>(
      'files/tree-overwrite-restorings',
      {
        method: 'POST',
        body: JSON.stringify(command),
        headers: { 'Idempotency-Key': key },
        signal: controller.signal,
      },
    )
    assertOverwriteIntent(result)
    if (!sameOverwriteRequest(result.command, command))
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
      await apiRequest<TreeOverwriteRestoringIntent>(
        `files/tree-overwrite-restorings/${row.id}/confirm`,
        {
          method: 'POST',
          body: JSON.stringify(frozenConfirmation.value),
          headers: { 'Idempotency-Key': row.id },
          signal: controller.signal,
        },
      ),
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
      '未确认计划将禁止迟到确认；已开始覆盖恢复只请求取消，不保证已停止或回滚。覆盖前备份与旧原件按原回执保留。',
      '取消目录操作',
      { confirmButtonText: '确认取消', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    busy.value = true
    controller = new AbortController()
    await accept(
      await apiRequest<TreeOverwriteRestoringIntent>(
        `files/tree-overwrite-restorings/${row.id}/cancel`,
        {
          method: 'POST',
          headers: { 'Idempotency-Key': row.id },
          signal: controller.signal,
        },
      ),
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
      '确认接受未知历史结果并解除本任务路径锁。不会标为成功、重复覆盖或删除覆盖前备份及私有原件；请先核实目标与任务记录。',
      '确认未知结果',
      { confirmButtonText: '已核实，确认处置', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    busy.value = true
    controller = new AbortController()
    await apiRequest(`files/tree-overwrite-restorings/${row.id}/acknowledge`, {
      method: 'POST',
      body: JSON.stringify({ version: row.version, confirmed: true }),
      headers: { 'Idempotency-Key': row.id },
      signal: controller.signal,
    })
    await accept(
      await apiRequest<TreeOverwriteRestoringIntent>(`files/tree-overwrite-restorings/${row.id}`, {
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
    delete query.treeOverwriteRestoreId
    delete query.treeOverwriteRestoreKey
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
    () => route.query.treeOverwriteRestoreId,
    () => route.query.treeOverwriteRestoreKey,
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
        !route.query.treeUnpackKey &&
        !route.query.treeTrashId &&
        !route.query.treeTrashKey &&
        !route.query.treeRestoreId &&
        !route.query.treeRestoreKey
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
        !route.query.treeUnpackKey &&
        !route.query.treeTrashId &&
        !route.query.treeTrashKey &&
        !route.query.treeRestoreId &&
        !route.query.treeRestoreKey
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
    class="tree-overwrite-restore-reference"
    role="status"
  >
    <span>{{ intent ? overwriteState(intent.state) : '原计划登记结果待核实' }}</span>
    <el-button link type="primary" @click="dialog = true">查看覆盖恢复任务</el-button>
    <el-button link :disabled="busy" @click="release">关闭引用</el-button>
  </div>
  <el-dialog
    v-if="authorized"
    v-model="dialog"
    title="覆盖现有目录（先备份）"
    width="min(640px, calc(100vw - 24px))"
    align-center
    :close-on-click-modal="false"
    class="tree-overwrite-restore-dialog"
  >
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <template v-if="intent">
      <el-alert
        :title="overwriteState(intent.state)"
        :type="
          intent.state === 'Succeeded' ? 'success' : intent.state === 'Unknown' ? 'warning' : 'info'
        "
        :closable="false"
      />
      <dl class="tree-overwrite-restore-details">
        <dt>历史源</dt>
        <dd>{{ intent.command.source.rootId }} / {{ intent.command.source.path }}</dd>
        <dt>捕获 ID</dt>
        <dd>{{ intent.command.captureId }}</dd>
        <dt>归档类型</dt>
        <dd>
          {{ intent.command.captureKind === 'Overwrite' ? '覆盖前备份归档' : '目录回收归档' }}
        </dd>
        <dt>目标目录</dt>
        <dd>{{ intent.command.target.rootId }} / {{ intent.command.target.path }}</dd>
        <dt>目标原版本</dt>
        <dd>{{ intent.command.targetExpectedVersion }}</dd>
        <dt>当前目标</dt>
        <dd>
          {{ intent.targetEntryCount ?? '待扫描' }} 项 ·
          {{ intent.targetTotalBytes === null ? '待统计' : formatBytes(intent.targetTotalBytes) }}
        </dd>
        <dt>恢复内容</dt>
        <dd>
          {{ intent.entryCount ?? '尚未完成扫描' }} 项 ·
          {{ intent.totalBytes === null ? '待统计' : formatBytes(intent.totalBytes) }}
        </dd>
        <dt>输入格式</dt>
        <dd>TAR.GZ（历史归档）</dd>
        <template v-if="intent.archiveBytes !== null">
          <dt>输入大小</dt>
          <dd>{{ formatBytes(intent.archiveBytes!) }}</dd>
          <dt>SHA256</dt>
          <dd>{{ intent.archiveSha256 }}</dd>
        </template>
        <dt>覆盖前备份</dt>
        <dd>
          {{
            intent.backupVerified
              ? '曾完整验证'
              : intent.backupMayBeRetained
                ? '可能保留，需核实原记录'
                : '尚未取得已验证备份回执'
          }}
        </dd>
        <template v-if="intent.backupVerified"
          ><dt>备份捕获 ID</dt>
          <dd>{{ intent.overwrittenCaptureId }}</dd>
          <dt>备份归档</dt>
          <dd>{{ formatBytes(intent.backupArchiveBytes!) }}</dd>
          <dt>备份 SHA256</dt>
          <dd>{{ intent.backupArchiveSha256 }}</dd></template
        >
        <dt>原件留存</dt>
        <dd>
          {{
            intent.originalsRetained
              ? '原任务回执记录已留存'
              : '尚未核实，不能推断已删除或已安全保存'
          }}
        </dd>
        <dt>计划任务</dt>
        <dd>{{ intent.planningJobId }}</dd>
        <template v-if="intent.executionJobId"
          ><dt>执行任务</dt>
          <dd>{{ intent.executionJobId }}</dd></template
        >
      </dl>
      <p v-if="intent.resultCode" role="status">{{ overwriteResult(intent.resultCode) }}</p>
      <p v-if="intent.confirmation">{{ intent.confirmation.warning }}</p>
      <p
        v-if="
          intent.state === 'Ready' &&
          intent.confirmation &&
          Date.parse(intent.confirmation.expiresAtUtc) <= now
        "
        role="alert"
      >
        计划已过期，请查询原任务并关闭引用后重新生成；不会自动执行。
      </p>
      <p v-if="confirmationUncertain" role="alert">
        确认响应尚未核实，请查询原任务；不会重新发送确认或创建另一个执行任务。
      </p>
      <p v-if="intent.state === 'Succeeded'">
        这是原覆盖恢复任务的完成回执：覆盖前备份曾验证，旧原件曾保留。不保证目标之后未变化，也不保证历史归档、备份或旧原件之后仍健康。
      </p>
      <p v-if="intent.partialRetained || intent.state === 'Unknown'">
        结果未知或部分对象可能留存；保留路径锁、覆盖前备份和证据，不自动重复覆盖、不自动清理或回滚。留存标记不能替代实际
        HDD 备份验证。
      </p>
    </template>
    <el-form
      v-else-if="source && !pendingKey && !referenceId"
      label-position="top"
      @submit.prevent="queue"
    >
      <el-alert
        v-if="!targets.length"
        type="warning"
        :closable="false"
        title="没有具备恢复写入授权的目标根；请检查登记根配置，不会自动扩权。"
      />
      <el-form-item label="历史源目录"
        ><el-input :model-value="source.path" readonly
      /></el-form-item>
      <el-form-item label="捕获 ID"
        ><el-input :model-value="selectedCapture?.id" readonly
      /></el-form-item>
      <el-form-item label="当前目录中的覆盖目标">
        <el-select
          :model-value="targetName"
          aria-label="覆盖目标目录"
          :disabled="busy"
          @update:model-value="chooseTarget"
        >
          <el-option
            v-for="entry in currentTargets"
            :key="entry.name"
            :value="entry.name"
            :label="entry.name"
          />
        </el-select>
      </el-form-item>
      <p v-if="!currentTargets.length">
        当前文件列表没有可覆盖的普通目录。请先浏览到目标的父目录并读取列表，再选择历史捕获。
      </p>
      <el-form-item label="冻结的目标路径"
        ><el-input :model-value="targetRoot + ' / ' + targetPath" readonly
      /></el-form-item>
      <el-form-item label="冻结的当前目标版本"
        ><el-input :model-value="targetExpectedVersion" readonly
      /></el-form-item>
      <p>
        完整清单最多 10,000 项、恢复内容 16 GiB、输入归档 17
        GiB。生成计划不覆盖目标；确认后先完整备份并验证当前目标到
        HDD，再原子替换目录。历史输入归档与旧目标原件保留。保护文件、链接、路径穿越及不支持的成员会使整个计划拒绝。
      </p>
    </el-form>
    <p v-else>只查询原登记键，不重新发送登记或执行请求。未找到不代表原请求从未提交。</p>
    <template #footer
      ><div class="tree-overwrite-restore-buttons">
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
          type="primary"
          @click="confirm"
          >确认先备份再覆盖</el-button
        >
        <el-button
          v-if="source && !intent && !pendingKey && !referenceId"
          :disabled="busy || !currentTargets.length || !targetExpectedVersion"
          type="primary"
          @click="queue"
          >生成计划</el-button
        >
      </div></template
    >
  </el-dialog>
</template>

<style scoped>
.tree-overwrite-restore-reference {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
}
.tree-overwrite-restore-details {
  display: grid;
  grid-template-columns: 76px minmax(0, 1fr);
  gap: 10px;
  margin: 18px 0;
}
.tree-overwrite-restore-details dt {
  color: var(--el-text-color-secondary);
}
.tree-overwrite-restore-details dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.tree-overwrite-restore-buttons {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
}
.tree-overwrite-restore-buttons :deep(.el-button + .el-button) {
  margin-left: 0;
}
:global(.tree-overwrite-restore-dialog .el-dialog__body) {
  max-height: calc(100dvh - 200px);
  overflow-y: auto;
}
p {
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
  line-height: 1.7;
}
</style>
