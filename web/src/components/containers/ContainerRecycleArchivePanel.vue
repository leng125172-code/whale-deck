<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { RouterLink } from 'vue-router'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  assertArchiveAdvance,
  assertArchiveIntent,
  cancelArchive,
  confirmArchive,
  archiveState,
  archiveTerminal,
  currentArchiveInstance,
  getArchive,
  queueArchive,
  listArchiveSources,
} from '@/services/containerRecycleArchives'
import type {
  RecycleArchiveSource,
  RecycleArchiveIntent,
} from '@/services/containerRecycleArchives'
import {
  recycleArchiveHint,
  removeRecycleArchiveHint,
  saveRecycleArchiveHint,
} from '@/services/containerRecycleArchiveHints'
import type { ContainerRecycleArchiveHint } from '@/services/containerRecycleArchiveHints'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; refresh: []; archived: [id: string] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  clock = ref(Date.now()),
  confirmingCancel = ref(false)
const source = ref<RecycleArchiveSource | null>(null)
const rows = ref<RecycleArchiveSource[]>([]),
  next = ref<string | null>(null),
  after = ref<string | null>(null)
const hint = ref<ContainerRecycleArchiveHint | null>(null),
  intent = ref<RecycleArchiveIntent | null>(null)
const uncertain = ref(false)
const active = computed(
  () => !!hint.value && (!intent.value || !archiveTerminal(intent.value.state)),
)
const expired = computed(
  () =>
    !!intent.value?.readyExpiresAtUtc && Date.parse(intent.value.readyExpiresAtUtc) <= clock.value,
)
const stale = computed(() => !!intent.value && !currentArchiveInstance(intent.value, props.target))
const canConfirm = computed(
  () =>
    intent.value?.state === 'Ready' &&
    !!intent.value.confirmation &&
    !expired.value &&
    !stale.value &&
    !uncertain.value,
)
const location = computed(() => intent.value?.location ?? source.value?.location ?? null)
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
  rows.value = []
  next.value = null
  after.value = null
  error.value = ''
  uncertain.value = false
}
function failure(reason: unknown) {
  if (intent.value?.state === 'Ready') uncertain.value = true
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    const subject = authSession.user.value?.subject
    if (subject) removeRecycleArchiveHint(subject, props.target.containerId)
    clear()
  }
  error.value =
    reason instanceof ApiError && reason.status === 404
      ? '尚未找到原登记，请继续查询同一引用；不会自动重新提交。'
      : reason instanceof Error
        ? reason.message
        : '归档恢复结果无法核实，请查询原任务'
}
function accept(value: RecycleArchiveIntent) {
  const original = hint.value
  if (!original) throw new Error('原归档恢复登记已失效')
  assertArchiveIntent(
    value,
    original.containerId,
    original.id ?? undefined,
    original.sourceIntentId,
    source.value ?? undefined,
  )
  assertArchiveAdvance(intent.value, value)
  intent.value = value
  hint.value = { ...original, id: value.id }
  clock.value = Date.now()
  try {
    saveRecycleArchiveHint(hint.value)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '原任务引用未能保存'
  }
  if (archiveTerminal(value.state)) {
    removeRecycleArchiveHint(original.subject, original.containerId)
    if (value.state === 'Succeeded' && refreshed !== value.id) {
      refreshed = value.id
      if (value.receipt?.bindingCurrent === true) emit('archived', value.id)
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
  action: (signal: AbortSignal) => Promise<RecycleArchiveIntent>,
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
      getArchive(original.containerId, original.id ?? original.key, signal, original.id === null),
    () => {
      uncertain.value = false
    },
  )
}
async function loadSources(cursor: string | null = null) {
  if (!allowed.value || busy.value || active.value) return
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  source.value = null
  rows.value = []
  try {
    const page = await listArchiveSources(props.target.containerId, local.signal, cursor)
    if (token !== generation || local.signal.aborted) return
    rows.value = page.items
    next.value = page.next
    after.value = cursor
  } catch (reason) {
    if (token === generation && !local.signal.aborted) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
function open() {
  if (!allowed.value || busy.value || props.disabled) return false
  if (active.value) {
    dialog.value = true
    return true
  }
  clear()
  dialog.value = true
  void loadSources()
  return true
}
async function prepare(retry = false) {
  if (
    !allowed.value ||
    busy.value ||
    props.disabled ||
    (retry ? !hint.value || !!intent.value : !!hint.value || !source.value)
  )
    return
  if (!retry) {
    const subject = authSession.user.value?.subject,
      selected = source.value
    if (!subject || !selected || selected.location.instanceId !== props.target.instanceId) {
      error.value = '原容器实例已变化，请刷新后核实来源'
      return
    }
    const original = {
      subject,
      containerId: props.target.containerId,
      key: createIdempotencyKey(),
      id: null,
      sourceIntentId: selected.sourceIntentId,
    }
    try {
      saveRecycleArchiveHint(original)
    } catch (reason) {
      failure(reason)
      return
    }
    hint.value = original
  }
  const original = hint.value!
  await perform((signal) =>
    queueArchive(original.containerId, original.sourceIntentId, original.key, signal),
  )
}
async function confirm() {
  const original = intent.value
  clock.value = Date.now()
  if (!allowed.value || props.disabled || busy.value || !original || !canConfirm.value) return
  stop()
  confirmingCancel.value = true
  try {
    await ElMessageBox.confirm(
      '确认从这份HDD备份复制恢复为新文件？不会覆盖已有文件，原清理历史和HDD备份均保留。',
      '确认从归档复制恢复',
      {
        type: 'warning',
        confirmButtonText: '确认复制为新文件',
        cancelButtonText: '暂不恢复',
        closeOnClickModal: false,
      },
    )
    clock.value = Date.now()
    if (!allowed.value || props.disabled || hint.value?.id !== original.id || !canConfirm.value)
      return
    confirmingCancel.value = false
  } catch {
    return
  } finally {
    confirmingCancel.value = false
    schedule(generation)
  }
  uncertain.value = true
  await perform(
    (signal) => confirmArchive(original, signal),
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
    archiveTerminal(original.state) ||
    original.state === 'Unknown'
  )
    return
  stop()
  confirmingCancel.value = true
  try {
    await ElMessageBox.confirm(
      '确认请求取消归档恢复？已经发布的新文件不会自动删除，不确定结果仍需核实。',
      '请求取消归档恢复',
      {
        type: 'warning',
        confirmButtonText: '确认请求取消',
        cancelButtonText: '继续等待',
        closeOnClickModal: false,
      },
    )
    if (!allowed.value || hint.value?.id !== original.id) return
    confirmingCancel.value = false
    await perform((signal) => cancelArchive(original, signal))
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
      hint.value = recycleArchiveHint(subject, props.target.containerId)
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
  <section v-if="allowed" class="container-archive" aria-label="独立归档恢复">
    <el-button :disabled="busy || disabled" @click="open">从归档复制恢复</el-button>
    <template v-if="hint"
      ><span role="status">{{ intent ? archiveState(intent.state) : '原归档登记待核实' }}</span
      ><el-button link :disabled="busy || confirmingCancel" @click="query"
        >查询原归档恢复任务</el-button
      ></template
    >
    <p v-if="error && !dialog" role="alert" class="container-archive__error">{{ error }}</p>
  </section>
  <el-dialog
    v-if="allowed"
    v-model="dialog"
    title="从HDD归档复制恢复"
    top="12px"
    width="min(620px, calc(100vw - 24px))"
    class="container-archive-dialog"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <el-alert
      title="来源列表是已成功清理的历史摘要。预检会重读HDD备份、清理证明和容器实例；确认后在原目录复制为新文件，不覆盖已有文件，保留原清理历史与HDD备份。"
      type="info"
      :closable="false"
    />
    <div v-if="!hint" aria-label="选择成功归档来源">
      <div class="archive-source-actions">
        <el-button :loading="busy" @click="loadSources()">刷新归档来源</el-button
        ><el-button v-if="after" :disabled="busy" @click="loadSources()">返回首批来源</el-button
        ><el-button v-if="next" :disabled="busy" @click="loadSources(next)">下一批来源</el-button>
      </div>
      <p v-if="!rows.length && !busy && !error">暂无可选择的成功归档来源</p>
      <div v-for="row in rows" :key="row.sourceIntentId" class="archive-source-row">
        <p>原对象：/{{ row.location.path }}</p>
        <p>{{ row.sizeBytes }} 字节 · 原UID/GID {{ row.originalUid }}:{{ row.originalGid }}</p>
        <p>
          来源清理意图：<code>{{ row.sourceIntentId }}</code>
        </p>
        <el-button
          :disabled="busy || row.location.instanceId !== target.instanceId"
          @click="source = row"
          >{{
            source?.sourceIntentId === row.sourceIntentId ? '已选择此来源' : '选择此来源'
          }}</el-button
        >
        <p v-if="row.location.instanceId !== target.instanceId">
          原运行实例已变化，不能由此入口恢复。
        </p>
      </div>
    </div>
    <p v-if="location" class="container-archive__path">恢复位置：/{{ location.path }}</p>
    <p v-if="source && !hint" class="container-archive__path">
      HDD来源摘要：<code>{{ source.historyDigest }}</code>
    </p>
    <div v-if="intent" class="container-archive__summary">
      <strong role="status">{{ archiveState(intent.state) }}</strong>
      <p>
        来源清理意图：<code>{{ intent.sourceIntentId }}</code>
      </p>
      <p>
        原回收执行：<code>{{ intent.sourceOperationId }}</code>
      </p>
      <p>
        来源HDD摘要：<code>{{ intent.sourceHistoryDigest }}</code>
      </p>
      <p>
        {{ intent.sizeBytes }} 字节 · 原UID/GID {{ intent.originalUid ?? '待预检' }}:{{
          intent.originalGid ?? '待预检'
        }}
      </p>
      <p>
        预检任务：<code>{{ intent.planningJobId }}</code>
      </p>
      <p v-if="intent.executionJobId">
        归档执行：<code>{{ intent.executionJobId }}</code>
      </p>
      <p v-if="intent.readyExpiresAtUtc">
        计划到期：{{ new Date(intent.readyExpiresAtUtc).toLocaleString() }}
      </p>
      <el-alert
        v-if="intent.confirmation"
        :title="intent.confirmation.warning"
        type="warning"
        :closable="false"
      />
      <el-alert
        v-if="stale"
        title="容器实例已变化；只能查询或请求取消原任务，不能确认旧计划。"
        type="warning"
        :closable="false"
      />
      <el-alert
        v-if="expired && intent.state === 'Ready'"
        title="归档预检已过期，不会自动重新登记。"
        type="warning"
        :closable="false"
      />
      <p v-if="intent.resultCode">诊断代码：{{ intent.resultCode }}</p>
      <RouterLink v-if="intent.state === 'Unknown'" to="/files">前往归档恢复异常处置</RouterLink>
      <p v-if="intent.receipt?.state === 'Completed'">
        当前副本绑定：{{
          intent.receipt.bindingCurrent === true
            ? '当前有效'
            : intent.receipt.bindingCurrent === false
              ? '已变化或缺席，保留历史复制成功事实'
              : '尚未核实'
        }}
      </p>
      <p v-if="intent.receipt?.publishedIdentity">
        已发布inode：<code>{{ intent.receipt.publishedIdentity.inode }}</code>
      </p>
      <p>
        来源清理执行：<code>{{ intent.purgeOperationId }}</code>
      </p>
      <p>预留证明：{{ intent.reservationRecorded ? '已记录本操作预留' : '尚未记录' }}</p>
      <p>
        本次复制事实：{{
          intent.archiveCopied === true
            ? '已证明复制恢复完成'
            : intent.archiveCopied === false
              ? '已证明本操作未复制'
              : '尚未证明，不能推断已恢复'
        }}
      </p>
    </div>
    <p v-if="hint && !intent" class="container-archive__path">
      原归档登记引用：<code>{{ hint.key }}</code>
    </p>
    <p v-if="error" role="alert" class="container-archive__error">{{ error }}</p>
    <template #footer>
      <el-button @click="dialog = false">{{ hint ? '关闭' : '返回' }}</el-button>
      <el-button
        v-if="!hint"
        type="primary"
        :loading="busy"
        :disabled="disabled || !source || source.location.instanceId !== target.instanceId"
        @click="prepare()"
        >生成归档恢复预检</el-button
      >
      <el-button v-if="hint" :disabled="busy || confirmingCancel" @click="query"
        >查询原归档恢复任务</el-button
      >
      <el-button v-if="hint && !intent" :disabled="busy || disabled" @click="prepare(true)"
        >重试原归档登记</el-button
      >
      <el-button
        v-if="intent?.state === 'Ready'"
        type="primary"
        :loading="busy"
        :disabled="disabled || !canConfirm"
        @click="confirm"
        >确认归档恢复</el-button
      >
      <el-button
        v-if="intent && !archiveTerminal(intent.state) && intent.state !== 'Unknown'"
        :disabled="busy || confirmingCancel"
        @click="cancel"
        >请求取消归档恢复</el-button
      >
    </template>
  </el-dialog>
</template>
<style scoped>
:global(.container-archive-dialog) {
  display: flex;
  flex-direction: column;
  max-height: calc(100dvh - 24px);
}
:global(.container-archive-dialog .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
:global(.container-archive-dialog .el-dialog__header),
:global(.container-archive-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
@media (max-width: 600px) {
  :global(.container-archive-dialog) {
    margin-block: 12px;
  }
}
.container-archive {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding-block: 8px;
  font-size: 13px;
}
.container-archive__path,
.container-archive__summary,
.container-archive__error {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.container-archive__summary p {
  margin-block: 10px;
}
.container-archive__error {
  color: var(--el-color-danger);
  width: 100%;
}
</style>

<style scoped>
.archive-source-row {
  border-bottom: 1px solid var(--el-border-color);
  padding: 12px 0;
  overflow-wrap: anywhere;
}
.archive-source-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin: 12px 0;
}
</style>
