<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import { filePath, fileQuery } from '@/services/fileManagement'
import type { FileEntry, FileRoot } from '@/services/fileManagement'
import {
  archiveAvailability,
  archiveCaptureState,
  archiveDate,
  archiveEligible,
  archiveLabel,
  archiveState,
  archiveTerminal,
  archiveVersionReady,
} from '@/services/fileArchives'
import type {
  FileArchiveCommand,
  FileArchiveIntent,
  FileArchiveKind,
  FileArchivePlan,
  FileArchiveRegistration,
  FileHistoryPage,
  FileVersion,
  FrozenArchiveRegistration,
} from '@/services/fileArchives'

const props = defineProps<{ roots: FileRoot[]; rootId: string; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; changed: []; failure: [reason: unknown] }>()
const route = useRoute(),
  router = useRouter()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const root = computed(() => props.roots.find((item) => item.id === props.rootId))
const canRead = computed(() =>
  ['files-list', 'files-download', 'files-history'].every((action) =>
    root.value?.actions.includes(action),
  ),
)
const canWrite = computed(
  () =>
    canRead.value &&
    ['files-upload', 'files-mutate', 'files-archive'].every((action) =>
      root.value?.actions.includes(action),
    ),
)
const historyVisible = ref(false),
  historyPath = ref(''),
  historyRows = ref<FileVersion[]>([]),
  historyLoading = ref(false)
const historyCursor = ref(''),
  historyVersion = ref(''),
  historyComplete = ref(true),
  historyInvalid = ref(0),
  historyError = ref('')
const dialog = ref(false),
  preparing = ref(false),
  submitting = ref(false),
  checking = ref(false),
  restored = ref(false)
const command = ref<FileArchiveCommand | null>(null),
  planKey = ref(''),
  planned = ref<FileArchivePlan | null>(null)
const frozen = ref<FrozenArchiveRegistration | null>(null),
  intent = ref<FileArchiveIntent | null>(null),
  unresolvedId = ref('')
const error = ref(''),
  trace = ref('')
const active = computed(() => intent.value !== null && !archiveTerminal(intent.value.state))
const locked = computed(
  () =>
    dialog.value ||
    preparing.value ||
    submitting.value ||
    !!frozen.value ||
    !!unresolvedId.value ||
    active.value,
)
let generation = 0,
  listGeneration = 0,
  disposed = false
let pending: AbortController | null = null,
  listRequest: AbortController | null = null,
  statusRequest: AbortController | null = null
let timer: number | null = null
watch(locked, (value) => emit('locked', value), { immediate: true })
function stopTimer() {
  if (timer !== null) window.clearTimeout(timer)
  timer = null
}
function reset() {
  generation++
  listGeneration++
  pending?.abort()
  listRequest?.abort()
  statusRequest?.abort()
  stopTimer()
  historyRows.value = []
  historyVisible.value = false
  historyLoading.value = false
  historyError.value = ''
  dialog.value = false
  preparing.value = false
  submitting.value = false
  checking.value = false
  planned.value = null
  command.value = null
  planKey.value = ''
  frozen.value = null
  intent.value = null
  unresolvedId.value = ''
  error.value = ''
  trace.value = ''
}
function fail(reason: unknown) {
  if (
    reason instanceof ApiError &&
    (reason.status === 401 ||
      (reason.status === 403 && reason.problem.code !== 'AGENT_OPERATION_FORBIDDEN'))
  )
    reset()
  error.value =
    reason instanceof ApiError
      ? (
          {
            401: '登录已失效',
            403: '没有权限或文件受到保护',
            404: '原计划、任务或版本不存在，请核实任务历史',
            409: '原版本、计划或路径锁已变化，请查询原登记，不要重新执行',
            429: '文件服务繁忙或已达操作预算',
            503: '服务暂时不可用，请保留并查询原登记',
          } as Record<number, string>
        )[reason.status] || reason.message
      : reason instanceof Error
        ? reason.message
        : '历史版本操作失败'
  trace.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
  emit('failure', reason)
}
watch(authorized, (value) => {
  if (!value) reset()
})
watch(
  () => props.rootId,
  () => {
    listGeneration++
    listRequest?.abort()
    historyRows.value = []
    historyVisible.value = false
    historyLoading.value = false
  },
)
async function list(resetPage = true) {
  if (!authorized.value || !canRead.value || historyLoading.value) return
  listRequest?.abort()
  listRequest = new AbortController()
  const request = listRequest,
    token = ++listGeneration
  historyLoading.value = true
  historyError.value = ''
  if (resetPage) {
    historyRows.value = []
    historyCursor.value = ''
    historyVersion.value = ''
    historyComplete.value = true
    historyInvalid.value = 0
  }
  try {
    const value = await apiRequest<FileHistoryPage>(
      `files/archives?${fileQuery(props.rootId, historyPath.value, { after: historyCursor.value, take: '30', expectedVersion: historyVersion.value })}`,
      { signal: request.signal },
    )
    if (token !== listGeneration || request.signal.aborted || disposed) return
    historyRows.value = resetPage ? value.entries : [...historyRows.value, ...value.entries]
    historyCursor.value = value.nextCursor
    historyVersion.value = value.version
    historyComplete.value = value.complete
    historyInvalid.value += value.invalidRecords
  } catch (reason) {
    if (token === listGeneration && !request.signal.aborted) {
      historyError.value =
        reason instanceof ApiError && reason.status === 409
          ? '历史目录已变化，请刷新后重新浏览。'
          : '历史读取失败，请稍后刷新。'
      if (reason instanceof ApiError && (reason.status === 401 || reason.status === 403))
        fail(reason)
    }
  } finally {
    if (token === listGeneration) historyLoading.value = false
  }
}
function openHistory(entry?: FileEntry) {
  if (!authorized.value || !canRead.value || props.disabled) return
  listGeneration++
  listRequest?.abort()
  historyLoading.value = false
  historyPath.value = entry ? filePath(props.path, entry.name) : ''
  historyVisible.value = true
  void list()
}
function showAllHistory() {
  historyPath.value = ''
  void list()
}
async function prepare(action: FileArchiveKind, entry?: FileEntry, version?: FileVersion) {
  if (!authorized.value || !canWrite.value || props.disabled || locked.value) return
  if (action === 'trash' && (!entry || !archiveEligible(entry))) return
  if (
    action !== 'trash' &&
    (!version || !archiveVersionReady(version) || version.rootId !== props.rootId)
  )
    return
  intent.value = null
  planned.value = null
  frozen.value = null
  unresolvedId.value = ''
  restored.value = false
  error.value = ''
  trace.value = ''
  historyVisible.value = false
  dialog.value = true
  preparing.value = true
  planKey.value = createIdempotencyKey()
  pending?.abort()
  pending = new AbortController()
  const request = pending,
    token = ++generation
  try {
    let expected = action === 'trash' ? entry!.version : 'ABSENT'
    const location = {
      rootId: props.rootId,
      path: action === 'trash' ? filePath(props.path, entry!.name) : version!.path,
    }
    if (action === 'restore') {
      try {
        expected = (
          await apiRequest<FileEntry>(`files/entry?${fileQuery(location.rootId, location.path)}`, {
            signal: request.signal,
          })
        ).version
      } catch (reason) {
        if (!(reason instanceof ApiError && reason.status === 404)) throw reason
      }
    }
    if (token !== generation || request.signal.aborted) return
    command.value = {
      kind: action,
      location,
      versionId: action === 'trash' ? null : version!.id,
      expectedVersion: expected,
    }
    await requestPlan(request, token)
  } catch (reason) {
    if (token === generation && !request.signal.aborted) fail(reason)
  } finally {
    if (token === generation) preparing.value = false
  }
}
async function requestPlan(request: AbortController, token: number) {
  const value = await apiRequest<FileArchivePlan>('files/archives/plan', {
    method: 'POST',
    headers: { 'Idempotency-Key': planKey.value },
    body: JSON.stringify(command.value),
    signal: request.signal,
  })
  if (token !== generation || request.signal.aborted || disposed) return
  planned.value = value
  await router.replace({ query: { ...route.query, archiveId: value.operationId } })
}
async function retryPlan() {
  if (!command.value || !authorized.value || preparing.value || submitting.value) return
  pending?.abort()
  pending = new AbortController()
  const request = pending,
    token = generation
  preparing.value = true
  error.value = ''
  try {
    await requestPlan(request, token)
  } catch (reason) {
    if (token === generation && !request.signal.aborted) fail(reason)
  } finally {
    if (token === generation) preparing.value = false
  }
}
function adopt(value: FileArchiveIntent) {
  const previous = intent.value
  intent.value = value
  planned.value = null
  frozen.value = null
  unresolvedId.value = ''
  dialog.value = false
  restored.value = false
  if (
    archiveTerminal(value.state) &&
    (previous?.id !== value.id || previous.state !== value.state)
  ) {
    emit('changed')
    if (value.state === 'Succeeded') ElMessage.success('历史版本操作已完成')
    if (historyVisible.value) void list()
  }
}
async function submit() {
  if (!authorized.value || submitting.value || preparing.value || (!planned.value && !frozen.value))
    return
  frozen.value ??= {
    planHash: planned.value!.planHash,
    confirmed: true,
    idempotencyKey: planned.value!.idempotencyKey,
  }
  pending?.abort()
  pending = new AbortController()
  const request = pending,
    token = generation
  submitting.value = true
  dialog.value = false
  error.value = ''
  try {
    const result = await apiRequest<FileArchiveIntent>('files/archives', {
      method: 'POST',
      headers: { 'Idempotency-Key': frozen.value.idempotencyKey },
      body: JSON.stringify(frozen.value),
      signal: request.signal,
    })
    if (token !== generation || request.signal.aborted || disposed) return
    adopt(result)
    await router.replace({ query: { ...route.query, archiveId: result.id } })
    if (!archiveTerminal(result.state)) void check()
  } catch (reason) {
    if (token === generation && !request.signal.aborted) fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
}
async function reference(id: string) {
  if (!authorized.value || submitting.value || preparing.value || checking.value) return
  statusRequest?.abort()
  statusRequest = new AbortController()
  const request = statusRequest,
    token = generation
  checking.value = true
  error.value = ''
  try {
    const value = await apiRequest<FileArchiveRegistration>(`files/archives/registrations/${id}`, {
      signal: request.signal,
    })
    if (token !== generation || request.signal.aborted || disposed) return
    unresolvedId.value = ''
    if (value.state === 'Registered' && value.intent) {
      adopt(value.intent)
      if (!archiveTerminal(value.intent.state) && value.intent.state !== 'Unknown')
        timer = window.setTimeout(() => void check(), 2000)
    } else if (value.state === 'Issued' && value.plan) {
      planned.value = value.plan
      planKey.value = value.plan.idempotencyKey
      command.value = value.plan.command
      restored.value = true
      dialog.value = true
    } else if (value.state === 'Expired' || value.state === 'Canceled')
      await releaseReference(
        value.state === 'Expired'
          ? '原计划已到期且未登记，可以重新选择操作'
          : '原计划已取消，迟到登记已被阻止',
      )
    else throw new Error('计划引用状态无法核实，请查询任务历史')
  } catch (reason) {
    if (token === generation && !request.signal.aborted) {
      unresolvedId.value = id
      fail(reason)
    }
  } finally {
    if (token === generation) checking.value = false
  }
}
async function check() {
  if (!intent.value || !authorized.value || checking.value || disposed) return
  stopTimer()
  statusRequest?.abort()
  statusRequest = new AbortController()
  const request = statusRequest,
    token = generation,
    id = intent.value.id
  checking.value = true
  try {
    const value = await apiRequest<FileArchiveIntent>(`files/archives/${id}`, {
      signal: request.signal,
    })
    if (token !== generation || request.signal.aborted || disposed) return
    adopt(value)
    if (!archiveTerminal(value.state) && value.state !== 'Unknown')
      timer = window.setTimeout(() => void check(), 2000)
  } catch (reason) {
    if (token === generation && !request.signal.aborted) fail(reason)
  } finally {
    if (token === generation) checking.value = false
  }
}
async function releaseReference(message: string) {
  planned.value = null
  frozen.value = null
  command.value = null
  unresolvedId.value = ''
  dialog.value = false
  error.value = ''
  const query = { ...route.query }
  delete query.archiveId
  await router.replace({ query })
  ElMessage.info(message)
}
async function closePlan() {
  // Element Plus can emit update:modelValue after a programmatic close has
  // finished its transition. That is not a human request to cancel a plan.
  // Do not let the late event abort an accepted job's poll or fence a retry.
  if (!dialog.value || submitting.value) return
  if (!planned.value) {
    generation++
    pending?.abort()
    dialog.value = false
    preparing.value = false
    command.value = null
    return
  }
  const id = planned.value.operationId,
    token = generation
  submitting.value = true
  try {
    const value = await apiRequest<FileArchiveRegistration>(
      `files/archives/registrations/${id}/cancel`,
      { method: 'POST', headers: { 'Idempotency-Key': id } },
    )
    if (token !== generation || disposed) return
    if (value.state === 'Registered' && value.intent) {
      adopt(value.intent)
      ElMessage.info('原任务已登记；如需停止，请使用请求取消任务')
      void check()
    } else if (value.state === 'Canceled' || value.state === 'Expired')
      await releaseReference('原计划已作废，没有重新执行任何操作')
    else throw new Error('取消结果无法核实，请保留并查询原计划')
  } catch (reason) {
    if (token === generation) fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
}
async function cancelTask() {
  const current = intent.value
  if (!current || submitting.value || !authorized.value) return
  const token = generation
  submitting.value = true
  try {
    await apiRequest(`files/archives/${current.id}/cancel`, {
      method: 'POST',
      headers: { 'Idempotency-Key': current.id },
    })
    if (token === generation) void check()
  } catch (reason) {
    if (token === generation) fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
}
async function acknowledge() {
  const current = intent.value
  if (!current || current.state !== 'Unknown' || !authorized.value || submitting.value) return
  const token = generation
  try {
    await ElMessageBox.confirm(
      '请先核对原文件、HDD版本与任务记录。这里只接受未知结果并释放路径锁，不回滚、不重做、不删除证据，也不把任务标为成功。',
      '人工确认未知结果',
      {
        type: 'warning',
        confirmButtonText: '已核实，接受未知结果',
        cancelButtonText: '保留待调查',
      },
    )
    if (token !== generation || !authorized.value || disposed) return
    submitting.value = true
    await apiRequest(`files/archives/${current.id}/acknowledge`, {
      method: 'POST',
      headers: { 'Idempotency-Key': current.id },
      body: JSON.stringify({ version: current.version, confirmed: true }),
    })
    if (token === generation) void check()
  } catch (reason) {
    if (token === generation && reason !== 'cancel' && reason !== 'close') fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
}
async function dismissUnknownReference() {
  const token = generation
  try {
    await ElMessageBox.confirm(
      '无法查询不等于旧任务未执行。请先核实任务历史；关闭只移除此页提示，不取消、不重做任何任务。',
      '关闭无法查询的引用',
      { confirmButtonText: '已核实，仅关闭提示', cancelButtonText: '继续保留' },
    )
    if (token === generation && authorized.value)
      await releaseReference('仅关闭提示，没有改变旧任务')
  } catch {
    /* Dialog cancellation does not alter a reference. */
  }
}
watch(
  [authorized, () => route.query.archiveId],
  ([allowed, id]) => {
    if (!allowed) return
    if (typeof id !== 'string' || !/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/.test(id)) return
    // Our own replace keeps the already frozen plan/job. Browser history can
    // select another reference, but must never carry its predecessor's token.
    if (id === planned.value?.operationId || id === intent.value?.id || id === unresolvedId.value)
      return
    reset()
    unresolvedId.value = id
    void reference(id)
  },
  { immediate: true },
)
onBeforeUnmount(() => {
  disposed = true
  reset()
  emit('locked', false)
})
defineExpose({ openHistory, trash: (entry: FileEntry) => void prepare('trash', entry) })
</script>

<template>
  <section
    v-if="authorized && (intent || frozen || unresolvedId)"
    class="archive-status"
    data-testid="file-archive-status"
    aria-live="polite"
  >
    <div class="archive-line">
      <strong>{{ intent ? archiveLabel(intent.command.kind) : '原计划登记状态' }}</strong
      ><el-tag
        v-if="intent"
        :type="
          intent.state === 'Succeeded' ? 'success' : intent.state === 'Unknown' ? 'warning' : 'info'
        "
        >{{ archiveState(intent.state) }}</el-tag
      >
    </div>
    <p v-if="intent" class="archive-path">
      {{ intent.command.location.rootId }} / {{ intent.command.location.path }}
    </p>
    <p v-if="intent">
      任务 <router-link :to="`/jobs?jobId=${intent.jobId}`">{{ intent.jobId }}</router-link
      >；关闭页面不会撤销已登记任务。
    </p>
    <p v-if="intent?.sourceRetained">
      原文件已私有保留，完整 HDD 副本由任务回执核实；同一物理盘上的副本不属于异盘容灾。
    </p>
    <el-alert
      v-if="intent?.state === 'Unknown'"
      title="结果未知，禁止自动重做。请先检查原文件与备份，再人工处理。"
      type="warning"
      :closable="false"
    />
    <p v-if="frozen">
      登记响应未核实，已冻结原计划与幂等键。重试只发送同一登记；刷新可找回原计划或任务。
    </p>
    <p v-if="unresolvedId">原引用暂时无法查询；不能据此断定没有执行。请保留引用并核实任务历史。</p>
    <el-alert
      v-if="error"
      :title="error"
      :description="trace ? `追踪号：${trace}` : undefined"
      type="error"
      :closable="false"
    />
    <div class="archive-actions">
      <el-button v-if="frozen" :loading="submitting" @click="submit">重试原登记</el-button>
      <el-button
        v-if="frozen || unresolvedId"
        :loading="checking"
        :disabled="submitting"
        @click="
          reference(unresolvedId || planned?.operationId || String(route.query.archiveId || ''))
        "
        >查询原登记</el-button
      >
      <el-button
        v-if="unresolvedId"
        :disabled="submitting || checking"
        @click="dismissUnknownReference"
        >关闭引用提示</el-button
      >
      <el-button v-if="intent && !archiveTerminal(intent.state)" :loading="checking" @click="check"
        >查询原任务</el-button
      >
      <el-button
        v-if="intent && !archiveTerminal(intent.state) && intent.state !== 'Unknown'"
        :loading="submitting"
        @click="cancelTask"
        >请求取消任务</el-button
      >
      <el-button
        v-if="intent?.state === 'Unknown'"
        type="warning"
        :loading="submitting"
        @click="acknowledge"
        >人工确认未知结果</el-button
      >
    </div>
  </section>
  <el-drawer
    v-if="authorized"
    v-model="historyVisible"
    title="历史版本与回收站"
    size="min(760px, 100vw)"
    append-to-body
    data-testid="file-history-drawer"
  >
    <div class="archive-history">
      <p class="archive-path">{{ root?.name }} / {{ historyPath || '此根目录全部版本' }}</p>
      <p class="archive-note">
        “副本存在”仅表示元数据与文件状态正常；恢复或清理前会重新完整校验。HDD
        与原文件同盘时不属于异盘容灾。
      </p>
      <div class="archive-actions">
        <el-button :loading="historyLoading" @click="list()">刷新历史</el-button
        ><el-button v-if="historyPath" :disabled="historyLoading" @click="showAllHistory"
          >查看此根全部版本</el-button
        >
      </div>
      <el-alert v-if="historyError" :title="historyError" type="error" :closable="false" />
      <el-alert
        v-if="historyInvalid"
        :title="`有 ${historyInvalid} 条无效元数据被跳过，请检查备份存储。`"
        type="warning"
        :closable="false"
      />
      <el-empty
        v-if="!historyLoading && !historyError && !historyRows.length"
        description="尚无可显示的历史版本"
      />
      <article
        v-for="version in historyRows"
        :key="version.id"
        class="archive-version"
        :data-version-id="version.id"
      >
        <div class="archive-line">
          <strong class="archive-truncate" :title="version.path">{{ version.path }}</strong
          ><el-tag :type="version.captureState === 'Trashed' ? 'warning' : 'info'">{{
            archiveCaptureState(version.captureState)
          }}</el-tag>
        </div>
        <p class="archive-note">
          {{ archiveDate(version.capturedAtUtc) }} · {{ formatBytes(version.sizeBytes) }}
        </p>
        <div class="archive-line">
          <el-tag :type="version.availability === 'Present' ? 'success' : 'danger'">{{
            archiveAvailability(version.availability)
          }}</el-tag
          ><span class="archive-note">{{
            version.differentSourceDevice ? '与原文件不同设备' : '同设备保留，不是异盘容灾'
          }}</span>
        </div>
        <p class="archive-note archive-truncate" :title="version.id">版本 {{ version.id }}</p>
        <div class="archive-actions">
          <el-button
            type="primary"
            plain
            :disabled="!canWrite || props.disabled || locked || !archiveVersionReady(version)"
            @click="prepare('restore', undefined, version)"
            >恢复此版本</el-button
          ><el-button
            type="danger"
            plain
            :disabled="!canWrite || props.disabled || locked || !archiveVersionReady(version)"
            @click="prepare('purge', undefined, version)"
            >清理此 HDD 副本</el-button
          >
        </div>
      </article>
      <el-button
        v-if="!historyComplete"
        :loading="historyLoading"
        class="archive-more"
        @click="list(false)"
        >加载更多历史</el-button
      >
      <p v-if="historyLoading" role="status">正在读取历史…</p>
    </div>
  </el-drawer>
  <el-dialog
    v-if="authorized"
    :model-value="dialog"
    :title="command ? archiveLabel(command.kind) : '核对文件版本'"
    width="min(600px, calc(100vw - 24px))"
    append-to-body
    :close-on-click-modal="false"
    :close-on-press-escape="!submitting"
    :show-close="!submitting"
    @update:model-value="closePlan"
  >
    <p v-if="preparing" role="status">正在检查文件版本、备份磁盘与容量…</p>
    <template v-if="planned"
      ><el-alert :title="planned.warning" type="warning" :closable="false" /><el-alert
        v-if="restored"
        title="已找回原计划；不代表已确认执行。只能重新审阅同一计划、查询原登记或取消计划，不能自动生成新操作。"
        type="info"
        :closable="false"
      />
      <dl class="archive-summary">
        <dt>对象</dt>
        <dd>{{ planned.command.location.rootId }} / {{ planned.command.location.path }}</dd>
        <dt>大小</dt>
        <dd>{{ formatBytes(planned.sizeBytes) }}</dd>
        <dt>当前目标</dt>
        <dd>
          {{
            planned.command.expectedVersion === 'ABSENT'
              ? planned.command.kind === 'purge'
                ? '仅清理所选 HDD 副本'
                : '不存在；不会覆盖其他文件'
              : planned.command.kind === 'restore'
                ? '已存在；覆盖前必须先完整备份'
                : '严格绑定当前文件版本'
          }}
        </dd>
        <template v-if="planned.command.versionId"
          ><dt>历史版本</dt>
          <dd>{{ planned.command.versionId }}</dd></template
        >
        <dt>计划到期</dt>
        <dd>{{ archiveDate(planned.expiresAtUtc) }}</dd>
      </dl>
    </template>
    <el-alert
      v-if="error"
      :title="error"
      :description="trace ? `追踪号：${trace}` : undefined"
      type="error"
      :closable="false"
    />
    <template #footer
      ><div class="archive-actions">
        <el-button :disabled="submitting" @click="closePlan">取消计划</el-button
        ><el-button
          v-if="planned"
          :type="planned.command.kind === 'purge' ? 'danger' : 'primary'"
          :loading="submitting"
          :disabled="preparing || checking"
          @click="submit"
          >确认并登记</el-button
        ><el-button v-else-if="command" type="primary" :loading="preparing" @click="retryPlan"
          >重试生成计划</el-button
        >
      </div></template
    >
  </el-dialog>
</template>

<style scoped>
.archive-status,
.archive-version {
  min-width: 0;
  padding: 16px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 10px;
  background: var(--el-fill-color-extra-light);
}
.archive-status {
  margin: 16px 0;
}
.archive-line,
.archive-actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}
.archive-actions {
  justify-content: flex-end;
  margin: 12px 0;
}
.archive-actions .el-button + .el-button {
  margin-left: 0;
}
.archive-path,
.archive-status p,
.archive-summary dd {
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}
.archive-line strong {
  flex: 1;
  min-width: 0;
}
.archive-truncate {
  min-width: 0;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}
.archive-note {
  font-size: 12px;
  color: var(--el-text-color-regular);
}
.archive-history {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}
.archive-history p {
  margin: 4px 0;
}
.archive-summary {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 10px 14px;
  margin: 18px 0;
}
.archive-summary dt {
  color: var(--el-text-color-regular);
}
.archive-summary dd {
  margin: 0;
  min-width: 0;
}
.archive-more {
  align-self: center;
  max-width: 100%;
}
@media (max-width: 480px) {
  .archive-status,
  .archive-version {
    padding: 12px;
  }
  .archive-actions {
    gap: 8px;
  }
  .archive-actions .el-button {
    max-width: 100%;
    white-space: normal;
    height: auto;
    min-height: 32px;
  }
  .archive-summary {
    grid-template-columns: minmax(0, 1fr);
    gap: 5px;
  }
  .archive-summary dd {
    margin-bottom: 6px;
  }
}
</style>
