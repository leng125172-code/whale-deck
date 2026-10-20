<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { LocationQueryRaw } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import { ApiError, apiRequest, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { treeUuid } from '@/services/fileTreeCopies'
import {
  assertRecoveryIntent,
  assertRecoveryPage,
  recoveryKind,
  recoveryResult,
  recoveryState,
  recoveryTerminal,
  recoveryPurgeScope,
  recoveryPurgeState,
  recoveryDeletionFact,
  sameRecoveryPurge,
  recoveryPurgeFollows,
} from '@/services/fileRecovery'
import type {
  RecoveryCandidate,
  RecoveryCommand,
  RecoveryDisposition,
  RecoveryIntent,
  RecoveryPage,
  RecoveryReason,
} from '@/services/fileRecovery'

const emit = defineEmits<{ changed: []; failure: [reason: unknown] }>()
const route = useRoute(),
  router = useRouter()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  loading = ref(false),
  error = ref('')
const entries = ref<RecoveryCandidate[]>([]),
  next = ref<string | null>(null),
  cursors = ref<string[]>(['']),
  page = ref(0)
const candidate = ref<RecoveryCandidate | null>(null),
  intent = ref<RecoveryIntent | null>(null)
const disposition = ref<RecoveryDisposition>('AcknowledgeUnknown'),
  reason = ref<RecoveryReason>('OriginalSessionLost')
const referenceId = ref(''),
  pendingKey = ref(''),
  now = ref(Date.now()),
  confirmUncertain = ref(false)
const verificationFailed = ref(false)
const hasReference = computed(() => !!(referenceId.value || pendingKey.value || intent.value))
const canConfirm = computed(() => {
  const row = intent.value,
    selected = candidate.value
  return (
    row?.state === 'Ready' &&
    !!row.confirmation &&
    Date.parse(row.confirmation.expiresAtUtc) > now.value &&
    !confirmUncertain.value &&
    !verificationFailed.value &&
    selected?.integrity === 'Verified' &&
    selected.version === row.originalVersion &&
    selected.executionJobId === row.originalExecutionJobId &&
    selected.kind === row.kind &&
    selected.intentId === row.originalIntentId &&
    (row.kind !== 'tree-purge' ||
      (!!row.purge &&
        !!selected.purge &&
        sameRecoveryPurge(row.purge, selected.purge) &&
        row.purge.version === row.originalVersion &&
        row.purge.state === 'Unknown'))
  )
})
let generation = 0,
  disposed = false,
  timer: number | undefined,
  clockTimer: number | undefined
let listController: AbortController | undefined, operationController: AbortController | undefined
let notified = ''
function owned(token: number) {
  return !disposed && token === generation && authorized.value
}
function reset() {
  generation++
  listController?.abort()
  operationController?.abort()
  window.clearTimeout(timer)
  window.clearInterval(clockTimer)
  busy.value = loading.value = false
  error.value = ''
  entries.value = []
  next.value = null
  cursors.value = ['']
  page.value = 0
  intent.value = candidate.value = null
  referenceId.value = pendingKey.value = ''
  confirmUncertain.value = false
  verificationFailed.value = false
  dialog.value = false
  notified = ''
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
      ? recoveryResult(cause.problem.code || null) ||
        (
          {
            404: '尚未查到原引用，或当前登录已更换。只重查原引用，不自动登记。',
            409: '任务状态、版本或接管占用已变化，请核对原任务。',
            429: '请求或接管预算已用完，请稍后重查。',
            503: '依赖暂不可用，原引用和证据保留。',
          } as Record<number, string>
        )[cause.status] ||
        '请求未完成，请核对原引用。'
      : '连接或响应无法核实。保留原引用，只重查，不自动重做。'
}
async function list(index = page.value) {
  if (!authorized.value || loading.value) return
  const token = generation
  loading.value = true
  listController = new AbortController()
  try {
    const after = cursors.value[index] || ''
    const result = assertRecoveryPage(
      await apiRequest<RecoveryPage>(
        `files/recovery?take=25${after ? `&after=${encodeURIComponent(after)}` : ''}`,
        { signal: listController.signal },
      ),
    )
    if (!owned(token)) return
    entries.value = result.entries
    next.value = result.nextCursor
    page.value = index
    error.value = ''
  } catch (cause) {
    if (owned(token)) fail(cause)
  } finally {
    if (owned(token)) loading.value = false
  }
}
async function more() {
  if (!next.value) return
  cursors.value[page.value + 1] = next.value
  await list(page.value + 1)
}
function select(row: RecoveryCandidate) {
  if (busy.value || hasReference.value || row.integrity !== 'Verified' || !row.executionJobId)
    return
  candidate.value = row
  error.value = ''
}
async function setReference(id: string) {
  if (route.query.fileRecoveryId === id && !route.query.fileRecoveryKey) return
  const query: LocationQueryRaw = { ...route.query, fileRecoveryId: id }
  delete query.fileRecoveryKey
  await router.replace({ query })
}
async function accept(
  row: RecoveryIntent,
  token: number,
  id?: string,
  command?: RecoveryCommand,
  signal?: AbortSignal,
) {
  if (!owned(token)) return
  verificationFailed.value = true
  assertRecoveryIntent(row, id, command)
  const previous = intent.value
  if (
    previous &&
    (row.id !== previous.id ||
      row.kind !== previous.kind ||
      row.originalIntentId !== previous.originalIntentId ||
      row.originalExecutionJobId !== previous.originalExecutionJobId ||
      row.originalVersion !== previous.originalVersion ||
      row.disposition !== previous.disposition ||
      row.reasonCode !== previous.reasonCode ||
      row.version < previous.version ||
      (previous.purge && (!row.purge || !recoveryPurgeFollows(previous.purge, row.purge))))
  )
    throw new Error('接管任务绑定或已确认删除事实发生变化，只查询原引用')
  if (
    candidate.value?.purge &&
    (!row.purge || !recoveryPurgeFollows(candidate.value.purge, row.purge))
  )
    throw new Error('清理捕获或范围发生变化，不允许确认')
  intent.value = row
  referenceId.value = row.id
  pendingKey.value = ''
  error.value = ''
  now.value = Date.now()
  await setReference(row.id)
  if (!owned(token)) return
  // After a reload, read current original metadata before offering confirmation.
  // A changed version is not silently adopted by an existing sealed plan.
  if (
    row.state === 'Ready' &&
    (!candidate.value ||
      candidate.value.intentId !== row.originalIntentId ||
      candidate.value.kind !== row.kind)
  ) {
    const original = await apiRequest<RecoveryCandidate>(
      `files/recovery/${row.kind}/${row.originalIntentId}`,
      { signal },
    )
    assertRecoveryPage({ entries: [original], nextCursor: null })
    if (!owned(token)) return
    candidate.value = original
  }
  if (candidate.value && row.state === 'Ready' && candidate.value.version !== row.originalVersion)
    error.value = '原任务版本与本计划不同；本页不会确认，请结束本次引用后重新核对。'
  verificationFailed.value = false
  window.clearTimeout(timer)
  window.clearInterval(clockTimer)
  if (row.state === 'Ready')
    clockTimer = window.setInterval(() => {
      now.value = Date.now()
    }, 1000)
  else if (!recoveryTerminal(row.state)) timer = window.setTimeout(() => void reference(), 4000)
  if (row.state === 'Disposed' && notified !== row.id) {
    notified = row.id
    emit('changed')
  }
}
async function reference() {
  if (!authorized.value || busy.value || (!referenceId.value && !pendingKey.value)) return
  const token = generation
  busy.value = true
  operationController = new AbortController()
  try {
    const suffix = referenceId.value || `by-key/${pendingKey.value}`
    const result = await apiRequest<RecoveryIntent>(`files/recovery/operations/${suffix}`, {
      signal: operationController.signal,
    })
    await accept(
      result,
      token,
      referenceId.value || undefined,
      undefined,
      operationController.signal,
    )
    if (owned(token)) confirmUncertain.value = false
  } catch (cause) {
    if (owned(token)) {
      window.clearTimeout(timer)
      fail(cause)
    }
  } finally {
    if (owned(token)) busy.value = false
  }
}
async function queue() {
  const selected = candidate.value
  if (
    !authorized.value ||
    busy.value ||
    hasReference.value ||
    !selected ||
    selected.integrity !== 'Verified' ||
    !selected.executionJobId
  )
    return
  const command: RecoveryCommand = {
    kind: selected.kind,
    intentId: selected.intentId,
    originalVersion: selected.version,
    disposition: disposition.value,
    reasonCode: reason.value,
  }
  const token = generation,
    key = createIdempotencyKey()
  pendingKey.value = key
  busy.value = true
  operationController = new AbortController()
  try {
    const query: LocationQueryRaw = { ...route.query, fileRecoveryKey: key }
    delete query.fileRecoveryId
    await router.replace({ query })
    if (!owned(token)) return
    const result = await apiRequest<RecoveryIntent>('files/recovery/operations', {
      method: 'POST',
      body: JSON.stringify(command),
      headers: { 'Idempotency-Key': key },
      signal: operationController.signal,
    })
    await accept(result, token, undefined, command, operationController.signal)
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
    confirmation = row.confirmation
  busy.value = true
  try {
    await ElMessageBox.confirm(
      confirmation.warning,
      row.disposition === 'AcknowledgeUnknown' ? '明确接受历史不确定性' : '按完整回执对账',
      { confirmButtonText: '确认处置', cancelButtonText: '暂不处置', type: 'warning' },
    )
    if (!owned(token) || !canConfirm.value) return
    operationController = new AbortController()
    // If this reply is lost, only GET can clear uncertainty. Never automatically POST again.
    confirmUncertain.value = true
    const result = await apiRequest<RecoveryIntent>(`files/recovery/operations/${row.id}/confirm`, {
      method: 'POST',
      body: JSON.stringify({
        planHash: confirmation.planHash,
        confirmed: true,
        version: row.version,
      }),
      headers: { 'Idempotency-Key': row.id },
      signal: operationController.signal,
    })
    await accept(result, token, row.id, undefined, operationController.signal)
    if (owned(token)) confirmUncertain.value = false
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
    !row ||
    !['Queued', 'Planning', 'Ready', 'DispositionQueued'].includes(row.state)
  )
    return
  const token = generation
  busy.value = true
  try {
    await ElMessageBox.confirm(
      '只取消本次接管，不取消原文件任务、不释放原锁。封存已经开始时服务端会拒绝取消。',
      '取消接管',
      { confirmButtonText: '确认取消', cancelButtonText: '保留任务' },
    )
    if (!owned(token)) return
    operationController = new AbortController()
    await accept(
      await apiRequest<RecoveryIntent>(`files/recovery/operations/${row.id}/cancel`, {
        method: 'POST',
        headers: { 'Idempotency-Key': row.id },
        signal: operationController.signal,
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
    await ElMessageBox.confirm(
      '仅关闭页面引用，不取消、不重做原任务或本次接管。未确认计划会到期；后台处置仍以原任务查询为准。',
      '关闭接管引用',
      { confirmButtonText: '仅关闭引用', cancelButtonText: '保留引用' },
    )
    if (!owned(token)) return
    const query = { ...route.query }
    delete query.fileRecoveryId
    delete query.fileRecoveryKey
    reset()
    dialog.value = true
    await router.replace({ query })
    await list(0)
  } catch {
    /* Dialog cancellation never drops a reference. */
  }
}
function open() {
  if (!authorized.value) return
  dialog.value = true
  if (!hasReference.value) {
    cursors.value = ['']
    void list(0)
  }
}
function refreshList() {
  cursors.value = ['']
  void list(0)
}
watch(
  [
    () => authSession.user.value?.subject,
    authorized,
    () => route.query.fileRecoveryId,
    () => route.query.fileRecoveryKey,
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
      dialog.value = true
      void reference()
    } else if (typeof key === 'string' && treeUuid.test(key) && key !== pendingKey.value && !id) {
      reset()
      pendingKey.value = key
      dialog.value = true
      void reference()
    }
  },
  { immediate: true },
)
onBeforeUnmount(() => {
  disposed = true
  reset()
})
</script>

<template>
  <div v-if="authorized" class="recovery-entry" data-testid="file-recovery-entry">
    <el-button @click="open">异常任务接管</el-button>
    <el-button
      v-if="hasReference"
      link
      :title="intent ? recoveryState(intent.state) : '原登记待核实'"
      @click="dialog = true"
      >查看接管任务</el-button
    >
  </div>
  <el-dialog
    v-if="authorized"
    v-model="dialog"
    title="异常文件任务"
    width="min(840px, calc(100vw - 24px))"
    align-center
    :close-on-click-modal="false"
    class="file-recovery-dialog"
  >
    <el-alert
      v-if="intent?.kind === 'tree-purge' || candidate?.kind === 'tree-purge'"
      title="接管只核对并封存原清理，不会再次删除。归档与原件分别保留删除事实；接受未知结果不代表副本仍存在。"
      type="warning"
      :closable="false"
    />
    <el-alert
      title="接管是当前管理员的新操作，不冒充原登录。不重做文件操作、不删除留存对象；接受未知结果不代表操作成功。"
      type="warning"
      :closable="false"
    />
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <el-alert
      v-if="
        intent?.kind === 'tree-overwrite-restore' || candidate?.kind === 'tree-overwrite-restore'
      "
      title="目录覆盖恢复接管只封存、核对回执和明确处置历史结果，绝不会再次执行覆盖；覆盖前备份和旧原件按原记录保留。"
      type="warning"
      :closable="false"
    />
    <template v-if="hasReference">
      <p role="status">
        {{ intent ? recoveryState(intent.state) : '正在核实原登记；只查询，不自动新建' }}
      </p>
      <dl class="recovery-details" v-if="intent">
        <dt>原任务类型</dt>
        <dd>{{ recoveryKind(intent.kind) }}</dd>
        <dt>原任务</dt>
        <dd>{{ intent.originalExecutionJobId }}</dd>
        <dt>原版本</dt>
        <dd>{{ intent.originalVersion }}</dd>
        <template v-if="intent.purge">
          <dt>捕获 ID</dt>
          <dd>{{ intent.purge.captureId }}</dd>
          <dt>捕获类型</dt>
          <dd>{{ intent.purge.captureKind === 'Trash' ? '目录回收' : '覆盖前备份' }}</dd>
          <dt>原清理范围</dt>
          <dd>{{ recoveryPurgeScope(intent.purge.scope) }}</dd>
          <dt>原记录当前版本</dt>
          <dd>{{ intent.purge.version }} · {{ recoveryPurgeState(intent.purge.state) }}</dd>
          <dt>删除是否开始</dt>
          <dd>
            {{
              intent.purge.deletionStarted === null
                ? '结果待核实'
                : intent.purge.deletionStarted
                  ? '已开始，不代表全部完成'
                  : '未开始'
            }}
          </dd>
          <dt>HDD归档</dt>
          <dd>{{ recoveryDeletionFact(intent.purge.archiveRemoved) }}</dd>
          <dt>保留原件</dt>
          <dd>{{ recoveryDeletionFact(intent.purge.originalsRemoved) }}</dd>
        </template>
        <template v-if="candidate?.source">
          <dt>原任务源路径</dt>
          <dd>{{ candidate.source.rootId }} / {{ candidate.source.path }}</dd>
        </template>
        <template v-if="candidate?.target">
          <dt>原任务目标</dt>
          <dd>{{ candidate.target.rootId }} / {{ candidate.target.path }}</dd>
        </template>
        <dt>接管计划</dt>
        <dd>{{ intent.planningJobId }}</dd>
        <dt>接管执行</dt>
        <dd>{{ intent.executionJobId || '尚未确认' }}</dd>
        <dt>核对回执</dt>
        <dd>{{ intent.rootState || '尚未取得' }}</dd>
        <dt>处置类型</dt>
        <dd>
          {{
            intent.disposition === 'AcknowledgeUnknown'
              ? '明确接受历史不确定性'
              : '按完整可信回执对账'
          }}
        </dd>
      </dl>
      <p v-if="intent?.resultCode && recoveryResult(intent.resultCode)">
        {{ recoveryResult(intent.resultCode) }}
      </p>
      <p
        v-if="intent?.state === 'Disposed' && intent.disposition === 'AcknowledgeUnknown'"
        class="recovery-warning"
      >
        已记录接受未知历史；没有证明原文件操作成功。请检查原路径和留存对象。
      </p>
      <p v-if="intent?.state === 'Disposed' && intent.disposition === 'ReconcileCompleted'">
        {{
          intent.originalResolution === 'Superseded'
            ? '完整原回执已对账，但当前目标已变化。'
            : '完整原回执已对账；当前管理员处置与原文件操作分别保留审计。'
        }}
      </p>
      <p v-if="confirmUncertain">确认响应未核实，请先查询原接管任务；不会自动重复确认。</p>
      <p v-if="intent?.state === 'Ready' && !canConfirm">
        计划可能过期或元数据未核实，请只查询原任务。
      </p>
      <el-button :loading="busy" @click="reference">查询原接管任务</el-button>
      <el-button
        v-if="intent?.state === 'Ready'"
        type="primary"
        :disabled="busy || !canConfirm"
        @click="confirm"
        >{{ intent.disposition === 'AcknowledgeUnknown' ? '接受未知结果' : '确认对账' }}</el-button
      >
      <el-button
        v-if="intent && ['Queued', 'Planning', 'Ready', 'DispositionQueued'].includes(intent.state)"
        :disabled="busy"
        @click="cancel"
        >取消本次接管</el-button
      >
      <el-button :disabled="busy" @click="release">关闭引用</el-button>
    </template>
    <template v-else>
      <div class="recovery-list-actions">
        <el-button :loading="loading" @click="refreshList">刷新异常清单</el-button
        ><span>仅显示当前持久 Unknown，最多25项/页</span>
      </div>
      <el-empty
        v-if="!loading && !entries.length"
        description="暂无待核对异常任务"
        :image-size="56"
      />
      <article
        v-for="entry in entries"
        :key="`${entry.kind}:${entry.intentId}`"
        class="recovery-candidate"
        :class="{
          'is-selected': candidate?.intentId === entry.intentId && candidate?.kind === entry.kind,
        }"
      >
        <div>
          <strong>{{ recoveryKind(entry.kind) }}</strong
          ><el-tag :type="entry.integrity === 'Verified' ? 'warning' : 'danger'">{{
            entry.integrity === 'Verified' ? '原绑定已核实，结果仍未知' : '绑定异常，禁止处置'
          }}</el-tag>
        </div>
        <p class="recovery-id">{{ entry.executionJobId || entry.intentId }}</p>
        <p v-if="entry.source">源：{{ entry.source.rootId }} / {{ entry.source.path }}</p>
        <p v-if="entry.target">目标：{{ entry.target.rootId }} / {{ entry.target.path }}</p>
        <template v-if="entry.purge">
          <p>
            捕获 ID：{{ entry.purge.captureId }} ·
            {{ entry.purge.captureKind === 'Trash' ? '目录回收' : '覆盖前备份' }}
          </p>
          <p>原清理范围：{{ recoveryPurgeScope(entry.purge.scope) }}</p>
          <p>HDD归档：{{ recoveryDeletionFact(entry.purge.archiveRemoved) }}</p>
          <p>保留原件：{{ recoveryDeletionFact(entry.purge.originalsRemoved) }}</p>
        </template>
        <p>
          版本 {{ entry.version }} · 留存对象：{{
            entry.hasRetainedObjects === null
              ? '未知'
              : entry.hasRetainedObjects
                ? '有留存标记，非完整性证明'
                : '无已记录标记'
          }}
        </p>
        <el-button
          :disabled="busy || entry.integrity !== 'Verified' || !entry.executionJobId"
          @click="select(entry)"
          >核对该任务</el-button
        >
      </article>
      <div class="recovery-list-actions">
        <el-button :disabled="loading || page === 0" @click="list(page - 1)">上一页</el-button
        ><span>第 {{ page + 1 }} 页</span
        ><el-button :disabled="loading || !next" @click="more">下一页</el-button>
      </div>
      <el-form v-if="candidate" label-position="top" class="recovery-form">
        <p>
          准备核对：{{ recoveryKind(candidate.kind) }} · {{ candidate.intentId }} · 原版本
          {{ candidate.version }}
        </p>
        <el-form-item label="处置类型"
          ><el-select v-model="disposition" aria-label="异常任务处置类型"
            ><el-option
              value="AcknowledgeUnknown"
              label="明确接受历史不确定性（不表示成功）" /><el-option
              value="ReconcileCompleted"
              label="仅按完整可信回执对账" /></el-select
        ></el-form-item>
        <el-form-item label="接管原因"
          ><el-select v-model="reason" aria-label="异常任务接管原因"
            ><el-option value="OriginalSessionLost" label="原登录已失效" /><el-option
              value="OperatorHandover"
              label="操作人员交接" /><el-option
              value="IncidentReview"
              label="故障核查" /></el-select
        ></el-form-item>
        <el-button type="primary" :loading="busy" @click="queue">生成接管计划</el-button>
        <p>此步骤只登记并查询原回执；计划就绪后仍需二次确认。</p>
      </el-form>
    </template>
  </el-dialog>
</template>

<style scoped>
.recovery-entry,
.recovery-list-actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
}
.recovery-entry {
  justify-content: flex-end;
}
.recovery-list-actions {
  margin: 16px 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.recovery-candidate {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 12px;
  margin: 10px 0;
  min-width: 0;
  overflow-wrap: anywhere;
}
.recovery-candidate.is-selected {
  border-color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
}
.recovery-candidate > div {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.recovery-candidate p,
.recovery-form p {
  margin: 8px 0;
  font-size: 12px;
  overflow-wrap: anywhere;
}
.recovery-candidate .el-tag {
  height: auto;
  white-space: normal;
}
.recovery-details {
  display: grid;
  grid-template-columns: 96px minmax(0, 1fr);
  gap: 8px;
  margin: 16px 0;
  font-size: 13px;
}
.recovery-details dd {
  margin: 0;
  overflow-wrap: anywhere;
  min-width: 0;
}
.recovery-details dt {
  color: var(--el-text-color-secondary);
}
.recovery-warning {
  color: var(--el-color-warning-dark-2);
}
.recovery-form {
  border-top: 1px solid var(--el-border-color);
  padding-top: 12px;
}
:global(.file-recovery-dialog .el-dialog__body) {
  max-height: calc(100dvh - 160px);
  overflow-y: auto;
  overflow-x: hidden;
}
:global(.file-recovery-dialog .el-alert) {
  margin-bottom: 12px;
}
@media (max-width: 480px) {
  .recovery-details {
    grid-template-columns: 1fr;
    gap: 4px;
  }
  .recovery-entry {
    justify-content: flex-start;
  }
}
</style>
