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
  assertTreeIntent,
  canCopyDirectory,
  canReceiveDirectory,
  treeState,
  treeTerminal,
  treeUuid,
  validTreeLocation,
} from '@/services/fileTreeCopies'
import type { TreeCopyCommand, TreeCopyIntent } from '@/services/fileTreeCopies'

const props = defineProps<{ roots: FileRoot[]; rootId: string; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; changed: []; failure: [reason: unknown] }>()
const route = useRoute(),
  router = useRouter()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  intent = ref<TreeCopyIntent | null>(null)
const source = ref<TreeCopyCommand['source'] | null>(null),
  expectedVersion = ref(''),
  targetRoot = ref(''),
  targetPath = ref('')
const pendingKey = ref(''),
  referenceId = ref(''),
  now = ref(Date.now())
const targets = computed(() => props.roots.filter(canReceiveDirectory))
const active = computed(() => !!intent.value && !treeTerminal(intent.value.state))
const canConfirm = computed(
  () =>
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
  if (route.query.treeCopyId === id && !route.query.treeCopyKey) return
  const query: LocationQueryRaw = { ...route.query, treeCopyId: id }
  delete query.treeCopyKey
  await router.replace({ query })
}
async function accept(value: TreeCopyIntent, token: number, expected?: string) {
  if (!owned(token)) return
  assertTreeIntent(value, expected)
  intent.value = value
  referenceId.value = value.id
  pendingKey.value = ''
  error.value = ''
  if (value.executionJobId !== null || treeTerminal(value.state) || value.state === 'Unknown')
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
    const result = await apiRequest<TreeCopyIntent>(`files/tree-copies/${path}`, {
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
    !canCopyDirectory(
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
  targetRoot.value = targets.value.some((root) => root.id === props.rootId)
    ? props.rootId
    : targets.value[0]?.id || ''
  targetPath.value = filePath(props.path, `${entry.name}-副本`)
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
  const command: TreeCopyCommand = {
    source: { ...source.value },
    expectedVersion: expectedVersion.value,
    target: { rootId: targetRoot.value, path: targetPath.value },
  }
  if (
    !validTreeLocation(command.target) ||
    !targets.value.some((root) => root.id === command.target.rootId)
  ) {
    error.value = '请选择允许写入的目标根和合法相对路径'
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
    const query: LocationQueryRaw = { ...route.query, treeCopyKey: key }
    delete query.treeCopyId
    await router.replace({ query })
    if (!owned(token)) return
    const result = await apiRequest<TreeCopyIntent>('files/tree-copies', {
      method: 'POST',
      body: JSON.stringify(command),
      headers: { 'Idempotency-Key': key },
      signal: controller.signal,
    })
    if (JSON.stringify(result.command) !== JSON.stringify(command))
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
  if (!authorized.value || busy.value || !row || (!frozenConfirmation.value && !canConfirm.value))
    return
  if (!frozenConfirmation.value)
    frozenConfirmation.value = {
      planHash: row.confirmation!.planHash,
      confirmed: true,
      version: row.version,
    }
  const token = generation
  busy.value = true
  controller = new AbortController()
  try {
    await accept(
      await apiRequest<TreeCopyIntent>(`files/tree-copies/${row.id}/confirm`, {
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
      '未确认计划将禁止迟到确认；已开始复制会请求取消，最终以原任务回执为准，不假定已停止或回滚。',
      '取消目录操作',
      { confirmButtonText: '确认取消', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    busy.value = true
    controller = new AbortController()
    await accept(
      await apiRequest<TreeCopyIntent>(`files/tree-copies/${row.id}/cancel`, {
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
      '确认接受未知历史结果并解除本任务路径锁。不会标为成功、重做复制或删除私有部分对象；请先核实目标与任务记录。',
      '确认未知结果',
      { confirmButtonText: '已核实，确认处置', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    busy.value = true
    controller = new AbortController()
    await apiRequest(`files/tree-copies/${row.id}/acknowledge`, {
      method: 'POST',
      body: JSON.stringify({ version: row.version, confirmed: true }),
      headers: { 'Idempotency-Key': row.id },
      signal: controller.signal,
    })
    await accept(
      await apiRequest<TreeCopyIntent>(`files/tree-copies/${row.id}`, {
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
    delete query.treeCopyId
    delete query.treeCopyKey
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
    () => route.query.treeCopyId,
    () => route.query.treeCopyKey,
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
      dialog.value = true
      emit('locked', true)
      void reference()
    } else if (typeof key === 'string' && treeUuid.test(key) && key !== pendingKey.value) {
      reset()
      pendingKey.value = key
      dialog.value = true
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
    class="tree-copy-reference"
    role="status"
  >
    <span>{{ intent ? treeState(intent.state) : '原计划登记结果待核实' }}</span>
    <el-button link type="primary" @click="dialog = true">查看目录任务</el-button>
    <el-button link :disabled="busy" @click="release">关闭引用</el-button>
  </div>
  <el-dialog
    v-if="authorized"
    v-model="dialog"
    title="复制目录"
    width="min(640px, calc(100vw - 24px))"
    align-center
    :close-on-click-modal="false"
  >
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <template v-if="intent">
      <el-alert
        :title="treeState(intent.state)"
        :type="
          intent.state === 'Succeeded' ? 'success' : intent.state === 'Unknown' ? 'warning' : 'info'
        "
        :closable="false"
      />
      <dl class="tree-copy-details">
        <dt>源目录</dt>
        <dd>{{ intent.command.source.rootId }} / {{ intent.command.source.path }}</dd>
        <dt>目标目录</dt>
        <dd>{{ intent.command.target.rootId }} / {{ intent.command.target.path }}</dd>
        <dt>完整清单</dt>
        <dd>
          {{ intent.entryCount ?? '尚未完成扫描' }} 项 ·
          {{ intent.totalBytes === null ? '待统计' : formatBytes(intent.totalBytes) }}
        </dd>
        <dt>计划任务</dt>
        <dd>{{ intent.planningJobId }}</dd>
        <template v-if="intent.executionJobId"
          ><dt>执行任务</dt>
          <dd>{{ intent.executionJobId }}</dd></template
        >
      </dl>
      <p v-if="intent.confirmation">{{ intent.confirmation.warning }}</p>
      <p v-if="intent.state === 'Ready' && !canConfirm" role="alert">
        计划已过期，请查询原任务并关闭引用后重新生成；不会自动执行。
      </p>
      <p v-if="intent.state === 'Succeeded'">
        这是原任务的完成回执，不保证目标在之后未被其他程序修改。
      </p>
      <p v-if="intent.partialRetained || intent.state === 'Unknown'">
        私有部分对象或结果证据保留，不自动清理、不自动回滚，也不属于 HDD 备份。
      </p>
    </template>
    <el-form
      v-else-if="source && !pendingKey && !referenceId"
      label-position="top"
      @submit.prevent="queue"
    >
      <el-form-item label="源目录"><el-input :model-value="source.path" readonly /></el-form-item>
      <el-form-item label="目标根"
        ><el-select v-model="targetRoot" aria-label="目标根"
          ><el-option
            v-for="root in targets"
            :key="root.id"
            :value="root.id"
            :label="root.name" /></el-select
      ></el-form-item>
      <el-form-item label="目标相对路径"
        ><el-input v-model="targetPath" :maxlength="4096" aria-label="目标相对路径"
      /></el-form-item>
      <p>
        完整扫描最多 10,000 项、16
        GiB。生成计划不会复制；目标必须不存在，保护文件、链接及不支持的成员会使整个计划拒绝。
      </p>
    </el-form>
    <p v-else>只查询原登记键，不重新发送登记或执行请求。未找到不代表原请求从未提交。</p>
    <template #footer
      ><div class="tree-copy-buttons">
        <el-button @click="dialog = false">收起</el-button>
        <el-button v-if="intent || pendingKey || referenceId" :loading="busy" @click="reference()"
          >查询原任务</el-button
        >
        <el-button
          v-if="intent && !treeTerminal(intent.state)"
          :disabled="busy"
          type="danger"
          plain
          @click="cancel"
          >取消操作</el-button
        >
        <el-button
          v-if="intent?.state === 'Unknown'"
          :disabled="busy"
          type="warning"
          @click="acknowledge"
          >确认未知结果</el-button
        >
        <el-button
          v-if="intent?.state === 'Ready' || frozenConfirmation"
          :disabled="busy || (!canConfirm && !frozenConfirmation)"
          type="primary"
          @click="confirm"
          >确认复制</el-button
        >
        <el-button
          v-if="source && !intent && !pendingKey && !referenceId"
          :disabled="busy || !targets.length"
          type="primary"
          @click="queue"
          >生成计划</el-button
        >
      </div></template
    >
  </el-dialog>
</template>

<style scoped>
.tree-copy-reference {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
}
.tree-copy-details {
  display: grid;
  grid-template-columns: 76px minmax(0, 1fr);
  gap: 10px;
  margin: 18px 0;
}
.tree-copy-details dt {
  color: var(--el-text-color-secondary);
}
.tree-copy-details dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.tree-copy-buttons {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
}
.tree-copy-buttons :deep(.el-button + .el-button) {
  margin-left: 0;
}
p {
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
  line-height: 1.7;
}
</style>
