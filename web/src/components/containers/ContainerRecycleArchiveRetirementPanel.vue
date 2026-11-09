<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { validDirectoryId } from '@/services/containerDirectories'
import {
  acknowledgeRetirement,
  assertRetirement,
  assertRetirementAdvance,
  cancelRetirement,
  confirmRetirement,
  getRetirement,
  queueRetirement,
  retirementState,
  retirementTerminal,
  type RetirementIntent,
} from '@/services/containerRecycleArchiveRetirements'
import {
  removeRetirementHint,
  retirementHint,
  saveRetirementHint,
  type RetirementHint,
} from '@/services/containerRecycleArchiveRetirementHints'

const props = defineProps<{
  containerId: string
  suggestedArchiveId?: string
  existingId?: string
  recoveryId?: string
  disabled?: boolean
}>()
const emit = defineEmits<{ locked: [value: boolean]; changed: [] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const open = ref(false)
const busy = ref(false)
const error = ref('')
const note = ref('')
const archiveId = ref('')
const retainDays = ref<number>()
const hint = ref<RetirementHint | null>(null)
const row = ref<RetirementIntent | null>(null)
const recovery = ref(false)
const uncertain = ref(false)
const absent = ref(false)
const checked = ref(false)
const action = ref<'confirm' | 'cancel' | 'acknowledge' | null>(null)
const originalId = computed(() => props.recoveryId ?? props.existingId)
const now = ref(Date.now())
const terminal = computed(() => !!row.value && retirementTerminal(row.value.state))
const canQueue = computed(
  () =>
    validDirectoryId(archiveId.value) &&
    typeof retainDays.value === 'number' &&
    Number.isInteger(retainDays.value) &&
    retainDays.value >= 1 &&
    retainDays.value <= 3650,
)
const canConfirm = computed(
  () =>
    !recovery.value &&
    !uncertain.value &&
    row.value?.state === 'Ready' &&
    !!row.value.confirmation &&
    Date.parse(row.value.confirmation.expiresAtUtc) > now.value &&
    row.value.notBeforeUnixMs !== null &&
    row.value.notBeforeUnixMs <= now.value,
)
const canCancel = computed(
  () =>
    !recovery.value &&
    !uncertain.value &&
    row.value !== null &&
    !terminal.value &&
    row.value.state !== 'Unknown',
)
const canAcknowledge = computed(
  () => recovery.value && !uncertain.value && row.value?.state === 'Unknown',
)
const due = computed(() =>
  row.value?.notBeforeUnixMs == null
    ? '尚无到期证明'
    : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }).format(
        row.value.notBeforeUnixMs,
      ),
)
let generation = 0
let controller: AbortController | null = null
let timer: number | null = null
let clockTimer: number | undefined

function stop() {
  generation++
  controller?.abort()
  controller = null
  busy.value = false
  if (timer !== null) window.clearTimeout(timer)
  timer = null
}
function clear() {
  stop()
  open.value = false
  row.value = null
  hint.value = null
  error.value = ''
  note.value = ''
  archiveId.value = ''
  retainDays.value = undefined
  recovery.value = !!props.recoveryId
  uncertain.value = false
  absent.value = false
  action.value = null
  checked.value = false
}
function schedule() {
  if (timer !== null) window.clearTimeout(timer)
  timer = null
  if (
    !open.value ||
    !row.value ||
    terminal.value ||
    row.value.state === 'Unknown' ||
    action.value ||
    document.hidden ||
    uncertain.value
  )
    return
  timer = window.setTimeout(() => {
    void query()
  }, 5000)
}
function accept(value: RetirementIntent) {
  if (hint.value)
    assertRetirement(
      value,
      hint.value.id ?? undefined,
      hint.value.archiveIntentId,
      hint.value.retainDays,
      recovery.value,
    )
  assertRetirementAdvance(row.value, value)
  row.value = value
  now.value = Date.now()
  uncertain.value = false
  absent.value = false
  if (hint.value) {
    const saved = { ...hint.value, id: value.id }
    hint.value = saved
    if (retirementTerminal(value.state)) removeRetirementHint(saved.subject, saved.containerId)
    else {
      try {
        saveRetirementHint(saved)
      } catch {
        note.value = '任务已核实，本地仍保留原登记键。'
      }
    }
  }
}

async function run(call: (signal: AbortSignal) => Promise<RetirementIntent>, mutation = false) {
  if (!open.value || !allowed.value || busy.value || document.hidden) return
  stop()
  const current = generation
  const abort = new AbortController()
  controller = abort
  busy.value = true
  error.value = ''
  absent.value = false
  if (mutation) uncertain.value = true
  try {
    const value = await call(abort.signal)
    if (current !== generation || abort.signal.aborted || !allowed.value) return
    accept(value)
  } catch (reason) {
    if (current !== generation || abort.signal.aborted) return
    uncertain.value = true
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
      const original = hint.value
      if (original) removeRetirementHint(original.subject, original.containerId)
      clear()
      error.value = '当前会话无权查看或继续归档退役。'
      return
    }
    absent.value =
      !recovery.value &&
      hint.value?.id === null &&
      reason instanceof ApiError &&
      reason.status === 404
    error.value =
      reason instanceof ApiError && reason.status === 503
        ? 'Agent 退役能力暂不可用。请保留原引用，恢复后查询。'
        : absent.value
          ? '尚未找到原登记。可继续查询，或手动以原键重新登记。'
          : '当前结果未核实。请查询原任务，系统不会自动重发写入。'
  } finally {
    if (current === generation) {
      controller = null
      busy.value = false
      schedule()
    }
  }
}
async function query() {
  const id = originalId.value ?? row.value?.id ?? hint.value?.id ?? hint.value?.key
  if (!id) return
  const mode = recovery.value
    ? 'recovery'
    : originalId.value || row.value || hint.value?.id
      ? 'id'
      : 'key'
  await run((signal) => getRetirement(props.containerId, id, signal, mode))
}
function show() {
  if (!allowed.value || props.disabled || busy.value) return
  open.value = true
  archiveId.value = props.suggestedArchiveId ?? ''
  if (hint.value || originalId.value) void query()
}
async function register(retry = false) {
  const subject = authSession.user.value?.subject
  if (!subject || !allowed.value || busy.value || recovery.value || originalId.value) return
  if (retry) {
    if (!absent.value || !hint.value || hint.value.id !== null) return
  } else {
    if (hint.value || row.value || !canQueue.value || typeof retainDays.value !== 'number') return
    const original = {
      subject,
      containerId: props.containerId,
      archiveIntentId: archiveId.value,
      retainDays: retainDays.value,
      key: createIdempotencyKey(),
      id: null,
    }
    try {
      saveRetirementHint(original)
    } catch {
      error.value = '无法保存原登记键。请检查浏览器会话存储。'
      return
    }
    hint.value = original
  }
  const original = hint.value
  if (!original) return
  await run(
    (signal) =>
      queueRetirement(
        original.containerId,
        original.archiveIntentId,
        original.retainDays,
        original.key,
        signal,
      ),
    true,
  )
}
function choose(value: typeof action.value) {
  action.value = value
  checked.value = false
  if (timer !== null) window.clearTimeout(timer)
  timer = null
  if (value === null) schedule()
}
async function submit() {
  const current = row.value
  const selected = action.value
  if (
    !checked.value ||
    !current ||
    busy.value ||
    !selected ||
    (selected === 'confirm' && !canConfirm.value) ||
    (selected === 'cancel' && !canCancel.value) ||
    (selected === 'acknowledge' && !canAcknowledge.value)
  )
    return
  choose(null)
  note.value = ''
  if (selected === 'confirm') {
    await run((signal) => confirmRetirement(props.containerId, current, signal), true)
    if (open.value && row.value?.executionJobId) emit('changed')
    return
  }
  let accepted = false
  const container = props.containerId
  const mode = recovery.value ? 'recovery' : 'id'
  await run(async (signal) => {
    if (selected === 'cancel') await cancelRetirement(container, current.id, signal)
    else await acknowledgeRetirement(container, current, signal)
    signal.throwIfAborted()
    accepted = true
    return getRetirement(container, current.id, signal, mode)
  }, true)
  if (open.value && allowed.value && accepted && row.value?.id === current.id) {
    if (selected === 'cancel' && !terminal.value) note.value = '取消请求已提交，等待原任务更新。'
    emit('changed')
  }
}
function prepareRecovery() {
  if (row.value?.state !== 'Unknown' || busy.value) return
  recovery.value = true
  choose(null)
  void query()
}
function startNew() {
  if (!terminal.value || recovery.value || busy.value || originalId.value) return
  hint.value = null
  row.value = null
  note.value = ''
  archiveId.value = props.suggestedArchiveId ?? ''
  retainDays.value = undefined
}
function visibility() {
  if (document.hidden) stop()
  else if (open.value) void query()
}
watch(
  [
    allowed,
    () => authSession.user.value?.subject,
    () => props.containerId,
    originalId,
    () => props.recoveryId,
  ],
  () => {
    clear()
    const subject = authSession.user.value?.subject
    if (allowed.value && subject && !recovery.value && !originalId.value)
      hint.value = retirementHint(subject, props.containerId)
  },
  { immediate: true },
)
watch(open, (value) => {
  window.clearInterval(clockTimer)
  if (value) {
    now.value = Date.now()
    clockTimer = window.setInterval(() => {
      now.value = Date.now()
    }, 1000)
  }
  if (!value) {
    stop()
    action.value = null
    checked.value = false
    if (row.value?.confirmation) row.value = { ...row.value, confirmation: null }
  }
  emit('locked', value)
})
onMounted(() => document.addEventListener('visibilitychange', visibility))
onBeforeUnmount(() => {
  stop()
  window.clearInterval(clockTimer)
  document.removeEventListener('visibilitychange', visibility)
  emit('locked', false)
})
</script>

<template>
  <section v-if="allowed" aria-label="归档退役" class="retirement">
    <el-button :disabled="disabled || busy" @click="show">
      {{ recoveryId ? '核实并处置归档退役' : existingId ? '打开原退役任务' : '管理归档退役' }}
    </el-button>
    <p v-if="error && !open" role="alert">{{ error }}</p>
  </section>
  <el-dialog
    v-if="allowed"
    v-model="open"
    title="归档退役"
    top="12px"
    width="min(620px, calc(100vw - 24px))"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <el-alert
      type="warning"
      :closable="false"
      title="退役会删除符合留存策略的 HDD 归档数据。预检会核实已发布副本。确认执行后，不能再用这份 HDD 数据恢复。"
    />
    <div v-if="!hint && !row && !recovery && !originalId" class="retirement__fields">
      <label
        >成功归档意图 ID
        <el-input
          v-model.trim="archiveId"
          maxlength="36"
          autocomplete="off"
          placeholder="可从归档恢复任务摘要复制"
        />
      </label>
      <label
        >保留天数
        <el-input-number
          v-model="retainDays"
          :min="1"
          :max="3650"
          :precision="0"
          controls-position="right"
        />
      </label>
      <el-button type="primary" :loading="busy" :disabled="!canQueue" @click="register()"
        >登记退役预检</el-button
      >
    </div>
    <div v-else class="retirement__fields">
      <p role="status">{{ row ? retirementState(row.state) : '原任务结果待核实' }}</p>
      <template v-if="row">
        <dl class="retirement__facts">
          <dt>归档意图</dt>
          <dd>{{ row.archiveIntentId }}</dd>
          <dt>保留天数</dt>
          <dd>{{ row.retainDays }} 天</dd>
          <dt>最早退役时间</dt>
          <dd>{{ due }}</dd>
          <dt>归档字节数</dt>
          <dd>{{ row.archiveBytes ?? '待预检' }}</dd>
          <dt>宿主回执</dt>
          <dd>{{ row.rootState ?? '无回执' }}</dd>
          <dt>数据删除</dt>
          <dd>
            {{
              row.retirementApplied === true
                ? '已证实'
                : row.retirementApplied === false && row.rootState === 'Aborted'
                  ? '未执行删除'
                  : '暂无结论'
            }}
          </dd>
        </dl>
        <div class="retirement__actions">
          <RouterLink :to="{ name: 'jobs', query: { jobId: row.planningJobId } }"
            >规划任务</RouterLink
          >
          <RouterLink
            v-if="row.executionJobId"
            :to="{ name: 'jobs', query: { jobId: row.executionJobId } }"
            >执行任务</RouterLink
          >
        </div>
        <p v-if="row.state === 'Ready' && row.retirementEligible === false">
          保留期限未满足。此预检不能用于执行，到期后须重新预检。
        </p>
        <p
          v-if="
            row.state === 'Ready' &&
            row.readyExpiresAtUtc &&
            Date.parse(row.readyExpiresAtUtc) <= now
          "
        >
          确认期限已过。请刷新原退役任务，过期后可重新预检。
        </p>
        <p v-if="row.state === 'Unknown'">保护仍然生效。只有明确处置才能释放共享写入围栏。</p>
      </template>
      <div class="retirement__actions">
        <el-button :disabled="busy" @click="query">查询原退役任务</el-button>
        <el-button v-if="absent" :disabled="busy" @click="register(true)">以原键重新登记</el-button>
        <el-button v-if="canConfirm" type="danger" :disabled="busy" @click="choose('confirm')"
          >准备确认退役</el-button
        >
        <el-button v-if="canCancel" :disabled="busy" @click="choose('cancel')">请求取消</el-button>
        <el-button
          v-if="row?.state === 'Unknown' && !recovery"
          :disabled="busy"
          @click="prepareRecovery"
          >加载处置状态</el-button
        >
        <el-button
          v-if="canAcknowledge"
          type="warning"
          :disabled="busy"
          @click="choose('acknowledge')"
          >准备明确处置</el-button
        >
        <el-button v-if="terminal && !recovery && !originalId" :disabled="busy" @click="startNew"
          >新建退役计划</el-button
        >
      </div>
    </div>
    <div v-if="action" class="retirement__fields" role="group" aria-label="明确操作确认">
      <el-checkbox v-model="checked">
        {{
          action === 'confirm'
            ? '我确认按当前预检和留存策略删除这份 HDD 数据'
            : action === 'cancel'
              ? '我确认只请求取消原任务，保留已经发生的事实'
              : '我确认记录观察结果并释放本任务的共享保护'
        }}
      </el-checkbox>
      <div class="retirement__actions">
        <el-button type="danger" :disabled="!checked || busy" @click="submit"
          >提交明确确认</el-button
        >
        <el-button :disabled="busy" @click="choose(null)">暂不提交</el-button>
      </div>
    </div>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="note" role="status">{{ note }}</p>
  </el-dialog>
</template>

<style scoped>
.retirement,
.retirement__fields {
  display: grid;
  justify-items: start;
  gap: 0.7rem;
  margin-block: 0.75rem;
}
.retirement__fields label {
  display: grid;
  gap: 0.35rem;
  max-width: 100%;
}
.retirement__facts {
  display: grid;
  grid-template-columns: max-content minmax(0, 1fr);
  gap: 0.35rem 0.75rem;
  max-width: 100%;
}
.retirement__facts dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.retirement__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}
.retirement__actions :deep(.el-button + .el-button) {
  margin-left: 0;
}
.retirement__fields :deep(.el-checkbox) {
  height: auto;
  max-width: 100%;
}
.retirement__fields :deep(.el-checkbox__label) {
  white-space: normal;
}
[role='alert'] {
  color: var(--el-color-danger);
  overflow-wrap: anywhere;
}
</style>
