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
  assertUnpackingIntent,
  canUnpackArchive,
  canReceiveUnpacking,
  unpackingState,
  unpackingResult,
  treeTerminal,
  treeUuid,
  validUnpackingCommand,
  sameUnpackingCommand,
} from '@/services/fileTreeUnpackings'
import type {
  UnpackingFormat,
  TreeUnpackingCommand,
  TreeUnpackingIntent,
} from '@/services/fileTreeUnpackings'

const props = defineProps<{ roots: FileRoot[]; rootId: string; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; changed: []; failure: [reason: unknown] }>()
const route = useRoute(),
  router = useRouter()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  intent = ref<TreeUnpackingIntent | null>(null)
const source = ref<TreeUnpackingCommand['source'] | null>(null),
  expectedVersion = ref(''),
  targetRoot = ref(''),
  targetPath = ref(''),
  format = ref<UnpackingFormat>('tar.gz')
const pendingKey = ref(''),
  referenceId = ref(''),
  now = ref(Date.now())
const verificationFailed = ref(false),
  confirmationUncertain = ref(false)
const targets = computed(() => props.roots.filter(canReceiveUnpacking))
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
  if (route.query.treeUnpackId === id && !route.query.treeUnpackKey) return
  const query: LocationQueryRaw = { ...route.query, treeUnpackId: id }
  delete query.treeUnpackKey
  await router.replace({ query })
}
async function accept(value: TreeUnpackingIntent, token: number, expected?: string) {
  if (!owned(token)) return
  verificationFailed.value = true
  assertUnpackingIntent(value, expected)
  if (intent.value && !sameUnpackingCommand(intent.value.command, value.command))
    throw new Error('原任务源、目标或格式已改变；保留引用，不确认、不执行')
  if (
    intent.value?.archiveBytes !== null &&
    intent.value?.archiveBytes !== undefined &&
    (intent.value.archiveBytes !== value.archiveBytes ||
      intent.value.archiveSha256 !== value.archiveSha256 ||
      intent.value.entryCount !== value.entryCount ||
      intent.value.totalBytes !== value.totalBytes)
  )
    throw new Error('原计划输入证明或清单已改变；保留引用，不确认、不执行')
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
    const result = await apiRequest<TreeUnpackingIntent>(`files/tree-unpackings/${path}`, {
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
    !canUnpackArchive(
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
  format.value = entry.name.endsWith('.tar.gz') ? 'tar.gz' : 'tar'
  const name = entry.name.replace(/\.tar(?:\.gz)?$/, '')
  targetPath.value = filePath(
    targetRoot.value === props.rootId ? props.path : '',
    name !== entry.name ? name : `${entry.name}.unpacked`,
  )
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
  const command: TreeUnpackingCommand = {
    source: { ...source.value },
    expectedVersion: expectedVersion.value,
    format: format.value,
    target: { rootId: targetRoot.value, path: targetPath.value },
  }
  if (
    !validUnpackingCommand(command) ||
    !targets.value.some((root) => root.id === command.target.rootId)
  ) {
    error.value = '请选择具备解压授权的目标根和不存在的新目录，源归档与目标不能重叠'
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
    const query: LocationQueryRaw = { ...route.query, treeUnpackKey: key }
    delete query.treeUnpackId
    await router.replace({ query })
    if (!owned(token)) return
    const result = await apiRequest<TreeUnpackingIntent>('files/tree-unpackings', {
      method: 'POST',
      body: JSON.stringify(command),
      headers: { 'Idempotency-Key': key },
      signal: controller.signal,
    })
    assertUnpackingIntent(result)
    if (!sameUnpackingCommand(result.command, command))
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
      await apiRequest<TreeUnpackingIntent>(`files/tree-unpackings/${row.id}/confirm`, {
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
      '未确认计划将禁止迟到确认；已开始解压会请求取消，最终以原任务回执为准，不假定已停止或回滚。',
      '取消目录操作',
      { confirmButtonText: '确认取消', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    busy.value = true
    controller = new AbortController()
    await accept(
      await apiRequest<TreeUnpackingIntent>(`files/tree-unpackings/${row.id}/cancel`, {
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
      '确认接受未知历史结果并解除本任务路径锁。不会标为成功、重做解压或删除私有部分对象；请先核实目标与任务记录。',
      '确认未知结果',
      { confirmButtonText: '已核实，确认处置', cancelButtonText: '继续保留', type: 'warning' },
    )
    if (!owned(token)) return
    busy.value = true
    controller = new AbortController()
    await apiRequest(`files/tree-unpackings/${row.id}/acknowledge`, {
      method: 'POST',
      body: JSON.stringify({ version: row.version, confirmed: true }),
      headers: { 'Idempotency-Key': row.id },
      signal: controller.signal,
    })
    await accept(
      await apiRequest<TreeUnpackingIntent>(`files/tree-unpackings/${row.id}`, {
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
    delete query.treeUnpackId
    delete query.treeUnpackKey
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
    () => route.query.treeUnpackId,
    () => route.query.treeUnpackKey,
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
        !route.query.treePackKey
      emit('locked', true)
      void reference()
    } else if (typeof key === 'string' && treeUuid.test(key) && key !== pendingKey.value) {
      reset()
      pendingKey.value = key
      dialog.value =
        !route.query.treeCopyId &&
        !route.query.treeCopyKey &&
        !route.query.treeMoveId &&
        !route.query.treeMoveKey
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
    class="tree-unpack-reference"
    role="status"
  >
    <span>{{ intent ? unpackingState(intent.state) : '原计划登记结果待核实' }}</span>
    <el-button link type="primary" @click="dialog = true">查看目录任务</el-button>
    <el-button link :disabled="busy" @click="release">关闭引用</el-button>
  </div>
  <el-dialog
    v-if="authorized"
    v-model="dialog"
    title="解压归档至新目录"
    width="min(640px, calc(100vw - 24px))"
    align-center
    :close-on-click-modal="false"
    class="tree-unpack-dialog"
  >
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <template v-if="intent">
      <el-alert
        :title="unpackingState(intent.state)"
        :type="
          intent.state === 'Succeeded' ? 'success' : intent.state === 'Unknown' ? 'warning' : 'info'
        "
        :closable="false"
      />
      <dl class="tree-unpack-details">
        <dt>源归档</dt>
        <dd>{{ intent.command.source.rootId }} / {{ intent.command.source.path }}</dd>
        <dt>目标目录</dt>
        <dd>{{ intent.command.target.rootId }} / {{ intent.command.target.path }}</dd>
        <dt>完整清单</dt>
        <dd>
          {{ intent.entryCount ?? '尚未完成扫描' }} 项 ·
          {{ intent.totalBytes === null ? '待统计' : formatBytes(intent.totalBytes) }}
        </dd>
        <dt>输入格式</dt>
        <dd>{{ intent.command.format === 'tar.gz' ? 'TAR.GZ（gzip）' : 'TAR' }}</dd>
        <template v-if="intent.archiveBytes !== null">
          <dt>输入大小</dt>
          <dd>{{ formatBytes(intent.archiveBytes!) }}</dd>
          <dt>SHA256</dt>
          <dd>{{ intent.archiveSha256 }}</dd>
        </template>
        <dt>计划任务</dt>
        <dd>{{ intent.planningJobId }}</dd>
        <template v-if="intent.executionJobId"
          ><dt>执行任务</dt>
          <dd>{{ intent.executionJobId }}</dd></template
        >
      </dl>
      <p v-if="intent.resultCode" role="status">{{ unpackingResult(intent.resultCode) }}</p>
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
        这是原任务的完整新目录发布回执，不保证目标在之后未被其他程序修改；原输入归档保留，未覆盖已有目录。
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
      <el-alert
        v-if="!targets.length"
        type="warning"
        :closable="false"
        title="没有具备解压写入授权的目标根；请检查登记根配置，不会自动扩权。"
      />
      <el-form-item label="源归档"><el-input :model-value="source.path" readonly /></el-form-item>
      <el-form-item label="归档格式">
        <el-select v-model="format" aria-label="归档格式">
          <el-option label="TAR.GZ（gzip）" value="tar.gz" />
          <el-option label="TAR" value="tar" />
        </el-select>
      </el-form-item>
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
        完整清单最多 10,000 项、解压内容 16 GiB、输入归档 17
        GiB。生成计划不写入目标；输入归档保持不变，目标目录必须不存在。保护文件、链接、路径穿越及不支持的成员会使整个计划拒绝。
      </p>
    </el-form>
    <p v-else>只查询原登记键，不重新发送登记或执行请求。未找到不代表原请求从未提交。</p>
    <template #footer
      ><div class="tree-unpack-buttons">
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
          >确认解压</el-button
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
.tree-unpack-reference {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
}
.tree-unpack-details {
  display: grid;
  grid-template-columns: 76px minmax(0, 1fr);
  gap: 10px;
  margin: 18px 0;
}
.tree-unpack-details dt {
  color: var(--el-text-color-secondary);
}
.tree-unpack-details dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.tree-unpack-buttons {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
}
.tree-unpack-buttons :deep(.el-button + .el-button) {
  margin-left: 0;
}
:global(.tree-unpack-dialog .el-dialog__body) {
  max-height: calc(100dvh - 200px);
  overflow-y: auto;
}
p {
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
  line-height: 1.7;
}
</style>
