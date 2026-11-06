<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { RouterLink } from 'vue-router'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  assertPurgeAdvance,
  assertPurgeIntent,
  cancelPurge,
  confirmPurge,
  purgeState,
  purgeTerminal,
  currentPurgeInstance,
  getPurge,
  queuePurge,
  listPurgeSources,
} from '@/services/containerRecyclePurges'
import type { RecyclePurgeSource, RecyclePurgeIntent } from '@/services/containerRecyclePurges'
import {
  recyclePurgeHint,
  removeRecyclePurgeHint,
  saveRecyclePurgeHint,
} from '@/services/containerRecyclePurgeHints'
import type { ContainerRecyclePurgeHint } from '@/services/containerRecyclePurgeHints'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; refresh: [] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  clock = ref(Date.now()),
  confirmingCancel = ref(false)
const source = ref<RecyclePurgeSource | null>(null)
const rows = ref<RecyclePurgeSource[]>([]),
  next = ref<string | null>(null),
  after = ref<string | null>(null)
const hint = ref<ContainerRecyclePurgeHint | null>(null),
  intent = ref<RecyclePurgeIntent | null>(null)
const uncertain = ref(false)
const active = computed(() => !!hint.value && (!intent.value || !purgeTerminal(intent.value.state)))
const expired = computed(
  () =>
    !!intent.value?.readyExpiresAtUtc && Date.parse(intent.value.readyExpiresAtUtc) <= clock.value,
)
const stale = computed(() => !!intent.value && !currentPurgeInstance(intent.value, props.target))
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
    if (subject) removeRecyclePurgeHint(subject, props.target.containerId)
    clear()
  }
  error.value =
    reason instanceof ApiError && reason.status === 404
      ? '尚未找到原登记，请继续查询同一引用；不会自动重新提交。'
      : reason instanceof Error
        ? reason.message
        : '留存清理结果无法核实，请查询原任务'
}
function accept(value: RecyclePurgeIntent) {
  const original = hint.value
  if (!original) throw new Error('原留存清理登记已失效')
  assertPurgeIntent(
    value,
    original.containerId,
    original.id ?? undefined,
    original.sourceIntentId,
    source.value ?? undefined,
  )
  assertPurgeAdvance(intent.value, value)
  intent.value = value
  hint.value = { ...original, id: value.id }
  clock.value = Date.now()
  try {
    saveRecyclePurgeHint(hint.value)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '原任务引用未能保存'
  }
  if (purgeTerminal(value.state)) {
    removeRecyclePurgeHint(original.subject, original.containerId)
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
  action: (signal: AbortSignal) => Promise<RecyclePurgeIntent>,
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
      getPurge(original.containerId, original.id ?? original.key, signal, original.id === null),
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
    const page = await listPurgeSources(props.target.containerId, local.signal, cursor)
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
      saveRecyclePurgeHint(original)
    } catch (reason) {
      failure(reason)
      return
    }
    hint.value = original
  }
  const original = hint.value!
  await perform((signal) =>
    queuePurge(original.containerId, original.sourceIntentId, original.key, signal),
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
      '确认不可撤销地删除这份留存原件？HDD备份会保留，但当前尚未提供清理后的归档恢复入口。',
      '确认清理留存原件',
      {
        type: 'warning',
        confirmButtonText: '确认删除留存原件',
        cancelButtonText: '暂不清理',
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
    (signal) => confirmPurge(original, signal),
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
    purgeTerminal(original.state) ||
    original.state === 'Unknown'
  )
    return
  stop()
  confirmingCancel.value = true
  try {
    await ElMessageBox.confirm(
      '确认请求取消留存清理？已经完成的留存清理不会自动撤销，不确定结果仍需核实。',
      '请求取消留存清理',
      {
        type: 'warning',
        confirmButtonText: '确认请求取消',
        cancelButtonText: '继续等待',
        closeOnClickModal: false,
      },
    )
    if (!allowed.value || hint.value?.id !== original.id) return
    confirmingCancel.value = false
    await perform((signal) => cancelPurge(original, signal))
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
      hint.value = recyclePurgeHint(subject, props.target.containerId)
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
  <section v-if="allowed" class="container-purge" aria-label="独立留存清理">
    <el-button :disabled="busy || disabled" @click="open">清理留存原件</el-button>
    <template v-if="hint"
      ><span role="status">{{ intent ? purgeState(intent.state) : '原清理登记待核实' }}</span
      ><el-button link :disabled="busy || confirmingCancel" @click="query"
        >查询原留存清理任务</el-button
      ></template
    >
    <p v-if="error && !dialog" role="alert" class="container-purge__error">{{ error }}</p>
  </section>
  <el-dialog
    v-if="allowed"
    v-model="dialog"
    title="清理容器留存原件"
    top="12px"
    width="min(620px, calc(100vw - 24px))"
    class="container-purge-dialog"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <el-alert
      title="来源列表是历史回收摘要。预检将重新核验实际留存文件、HDD摘要及原容器实例；确认后只删除这份留存原件，HDD备份保留；删除不可撤销，当前尚未提供归档恢复入口。"
      type="info"
      :closable="false"
    />
    <div v-if="!hint" aria-label="选择原回收来源">
      <div class="purge-source-actions">
        <el-button :loading="busy" @click="loadSources()">刷新清理来源</el-button
        ><el-button v-if="after" :disabled="busy" @click="loadSources()">返回首批来源</el-button
        ><el-button v-if="next" :disabled="busy" @click="loadSources(next)">下一批来源</el-button>
      </div>
      <p v-if="!rows.length && !busy && !error">暂无可选择的原回收来源</p>
      <div v-for="row in rows" :key="row.sourceIntentId" class="purge-source-row">
        <p>原对象：/{{ row.location.path }}</p>
        <p>{{ row.sizeBytes }} 字节 · 原UID/GID {{ row.originalUid }}:{{ row.originalGid }}</p>
        <p>
          原回收意图：<code>{{ row.sourceIntentId }}</code>
        </p>
        <el-button
          :disabled="busy || row.location.instanceId !== target.instanceId"
          @click="source = row"
          >{{
            source?.sourceIntentId === row.sourceIntentId ? '已选择此来源' : '选择此来源'
          }}</el-button
        >
        <p v-if="row.location.instanceId !== target.instanceId">
          原运行实例已变化，不能由此入口清理。
        </p>
      </div>
    </div>
    <p v-if="location" class="container-purge__path">清理对象：/{{ location.path }}</p>
    <p v-if="source && !hint" class="container-purge__path">
      HDD来源摘要：<code>{{ source.historyDigest }}</code>
    </p>
    <div v-if="intent" class="container-purge__summary">
      <strong role="status">{{ purgeState(intent.state) }}</strong>
      <p>
        原回收意图：<code>{{ intent.sourceIntentId }}</code>
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
        清理执行：<code>{{ intent.executionJobId }}</code>
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
        title="清理预检已过期，不会自动重新登记。"
        type="warning"
        :closable="false"
      />
      <p v-if="intent.resultCode">诊断代码：{{ intent.resultCode }}</p>
      <RouterLink v-if="intent.state === 'Unknown'" to="/files">前往留存清理异常处置</RouterLink>
      <p>预留证明：{{ intent.reservationRecorded ? '已记录本操作预留' : '尚未记录' }}</p>
      <p>
        本次清理事实：{{
          intent.purgeApplied === true
            ? '已证明原件清理完成'
            : intent.purgeApplied === false
              ? '已证明本操作未清理'
              : '尚未证明，不能推断原件仍在'
        }}
      </p>
    </div>
    <p v-if="hint && !intent" class="container-purge__path">
      原清理登记引用：<code>{{ hint.key }}</code>
    </p>
    <p v-if="error" role="alert" class="container-purge__error">{{ error }}</p>
    <template #footer>
      <el-button @click="dialog = false">{{ hint ? '关闭' : '返回' }}</el-button>
      <el-button
        v-if="!hint"
        type="primary"
        :loading="busy"
        :disabled="disabled || !source || source.location.instanceId !== target.instanceId"
        @click="prepare()"
        >生成留存清理预检</el-button
      >
      <el-button v-if="hint" :disabled="busy || confirmingCancel" @click="query"
        >查询原留存清理任务</el-button
      >
      <el-button v-if="hint && !intent" :disabled="busy || disabled" @click="prepare(true)"
        >重试原清理登记</el-button
      >
      <el-button
        v-if="intent?.state === 'Ready'"
        type="primary"
        :loading="busy"
        :disabled="disabled || !canConfirm"
        @click="confirm"
        >确认留存清理</el-button
      >
      <el-button
        v-if="intent && !purgeTerminal(intent.state) && intent.state !== 'Unknown'"
        :disabled="busy || confirmingCancel"
        @click="cancel"
        >请求取消留存清理</el-button
      >
    </template>
  </el-dialog>
</template>
<style scoped>
:global(.container-purge-dialog) {
  display: flex;
  flex-direction: column;
  max-height: calc(100dvh - 24px);
}
:global(.container-purge-dialog .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
:global(.container-purge-dialog .el-dialog__header),
:global(.container-purge-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
@media (max-width: 600px) {
  :global(.container-purge-dialog) {
    margin-block: 12px;
  }
}
.container-purge {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding-block: 8px;
  font-size: 13px;
}
.container-purge__path,
.container-purge__summary,
.container-purge__error {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.container-purge__summary p {
  margin-block: 10px;
}
.container-purge__error {
  color: var(--el-color-danger);
  width: 100%;
}
</style>

<style scoped>
.purge-source-row {
  border-bottom: 1px solid var(--el-border-color);
  padding: 12px 0;
  overflow-wrap: anywhere;
}
.purge-source-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin: 12px 0;
}
</style>
