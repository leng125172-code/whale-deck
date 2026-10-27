<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ApiError } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  cancelContainerRestore,
  createContainerRestore,
  getContainerRestore,
  listContainerHistory,
  planContainerRestore,
} from '@/services/containerHistory'
import type { ContainerHistoryEntry } from '@/services/containerHistory'
import {
  containerUploadFollows,
  containerUploadTerminal,
  inspectContainerUploadEntry,
  newContainerUploadRegistration,
  validContainerUploadLocation,
} from '@/services/containerUploads'
import type {
  ContainerUploadIntent,
  ContainerUploadRegistration,
} from '@/services/containerUploads'
import type { FilePlan } from '@/services/fileManagement'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ refresh: []; locked: [value: boolean] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const visible = ref(false),
  busy = ref(false),
  error = ref(''),
  scope = ref(''),
  cursor = ref(''),
  version = ref('')
const entries = ref<ContainerHistoryEntry[]>([])
const incomplete = ref(0)
const destinationVisible = ref(false),
  destination = ref(''),
  restorePath = ref('')
const destinationSource = ref<ContainerHistoryEntry | null>(null)
const selected = ref<ContainerHistoryEntry | null>(null),
  plan = ref<FilePlan | null>(null)
const frozen = ref<ContainerUploadRegistration | null>(null),
  intent = ref<ContainerUploadIntent | null>(null)
const confirming = ref(false),
  cancelConfirm = ref(false)
const active = computed(() => !!intent.value && !containerUploadTerminal(intent.value.state))
const status = computed(
  () =>
    ({
      AwaitingUpload: '等待从 HDD 读取',
      Receiving: '正在校验并暂存历史内容',
      Ready: '已暂存，等待发布',
      Publishing: '正在恢复文件',
      Unknown: '结果未知，已停止自动操作',
      Succeeded: '已恢复',
      Superseded: '已恢复，但当前目标随后变化',
      Failed: '恢复失败',
      Canceled: '已取消',
    })[intent.value?.state ?? ''] ?? '',
)
let generation = 0,
  controller: AbortController | null = null,
  timer: number | null = null,
  refreshed = ''
function stop() {
  generation++
  controller?.abort()
  controller = null
  if (timer !== null) window.clearTimeout(timer)
  timer = null
  busy.value = false
}
function clear() {
  stop()
  visible.value = false
  error.value = ''
  entries.value = []
  scope.value = ''
  cursor.value = ''
  version.value = ''
  incomplete.value = 0
  selected.value = null
  destinationSource.value = null
  destinationVisible.value = false
  destination.value = ''
  restorePath.value = ''
  plan.value = null
  frozen.value = null
  intent.value = null
  confirming.value = false
  cancelConfirm.value = false
  refreshed = ''
}
function failure(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    clear()
    return
  }
  error.value =
    reason instanceof ApiError && [409, 412].includes(reason.status)
      ? '容器、文件或历史版本已变化；请重新加载并核对原任务，不会自动重试发布。'
      : reason instanceof Error
        ? reason.message
        : '历史读取失败，请核对后重试'
}
async function load(more = false) {
  if (!allowed.value || busy.value || active.value || frozen.value) return
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  try {
    const page = await listContainerHistory(
      props.target,
      scope.value,
      more ? cursor.value : '',
      more ? version.value : '',
      local.signal,
    )
    if (token !== generation || local.signal.aborted) return
    const next = more ? [...entries.value, ...page.entries] : page.entries
    if (next.length > 1000 || new Set(next.map((item) => item.captureId)).size !== next.length)
      throw new Error('历史过多或分页重复，请按文件筛选')
    entries.value = next
    cursor.value = page.nextCursor
    version.value = page.version
    incomplete.value = (more ? incomplete.value : 0) + page.invalidRecords
  } catch (reason) {
    if (!local.signal.aborted && token === generation) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
async function open(path = '') {
  if (!allowed.value || props.disabled) return
  if (active.value || frozen.value) {
    visible.value = true
    return
  }
  clear()
  scope.value = path
  visible.value = true
  await load()
}
function accept(row: ContainerUploadIntent) {
  if (intent.value && !containerUploadFollows(intent.value, row))
    throw new Error('恢复任务绑定或事实已变化；保留原任务，不重新发布')
  intent.value = row
}
async function query() {
  const row = intent.value,
    source = selected.value
  if (!row || !source || !allowed.value || busy.value) return
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  if (timer !== null) window.clearTimeout(timer)
  timer = null
  try {
    const next = await getContainerRestore(
      props.target,
      source,
      restorePath.value,
      row.id,
      local.signal,
    )
    if (token !== generation || local.signal.aborted) return
    accept(next)
    if (next.state === 'Succeeded' && refreshed !== next.id) {
      refreshed = next.id
      emit('refresh')
    }
    if (!containerUploadTerminal(next.state) && next.state !== 'Unknown')
      timer = window.setTimeout(() => void query(), 2000)
  } catch (reason) {
    if (!local.signal.aborted && token === generation) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
async function prepare(source: ContainerHistoryEntry, path = source.path) {
  if (
    !allowed.value ||
    props.disabled ||
    busy.value ||
    active.value ||
    frozen.value ||
    source.availability !== 'Present'
  )
    return
  const target = { ...props.target }
  if (
    !validContainerUploadLocation({
      containerId: target.containerId,
      instanceId: target.instanceId,
      path,
    })
  ) {
    error.value = '请输入容器内有效的相对文件路径；目标目录必须已存在'
    return
  }
  source = { ...source }
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  intent.value = null
  try {
    const current = await inspectContainerUploadEntry(target, path, local.signal)
    if (token !== generation || local.signal.aborted) return
    if (current && (current.kind !== 'File' || current.isProtected))
      throw new Error('当前目标是目录、链接或受保护文件，无法恢复')
    const result = await planContainerRestore(
      target,
      source,
      path,
      current?.version ?? 'ABSENT',
      local.signal,
    )
    if (token !== generation || local.signal.aborted) return
    selected.value = { ...source }
    restorePath.value = path
    destinationVisible.value = false
    destinationSource.value = null
    plan.value = result
    confirming.value = true
  } catch (reason) {
    if (!local.signal.aborted && token === generation) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
async function submit() {
  if (
    !allowed.value ||
    props.disabled ||
    busy.value ||
    active.value ||
    !selected.value ||
    (!confirming.value && !frozen.value)
  )
    return
  const source = selected.value
  if (!frozen.value) {
    if (!plan.value) return
    frozen.value = newContainerUploadRegistration(plan.value.planHash)
  }
  confirming.value = false
  plan.value = null
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  try {
    const row = await createContainerRestore(
      props.target,
      source,
      restorePath.value,
      frozen.value,
      local.signal,
    )
    if (token !== generation || local.signal.aborted) return
    accept(row)
    frozen.value = null
  } catch (reason) {
    if (!local.signal.aborted && token === generation) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
  if (token === generation && intent.value) await query()
}
async function cancel() {
  if (!allowed.value || busy.value || !intent.value || !cancelConfirm.value) return
  const row = intent.value
  cancelConfirm.value = false
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  try {
    await cancelContainerRestore(props.target, row.id, local.signal)
  } catch (reason) {
    if (!local.signal.aborted && token === generation) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
  if (token === generation) await query()
}
function chooseDestination(source: ContainerHistoryEntry) {
  if (!allowed.value || props.disabled || busy.value || active.value || frozen.value) return
  destinationSource.value = { ...source }
  destination.value = source.path
  error.value = ''
  destinationVisible.value = true
}
function prepareDestination() {
  if (destinationSource.value) void prepare(destinationSource.value, destination.value)
}
function dismissConfirmation() {
  confirming.value = false
  plan.value = null
  selected.value = null
}
watch(
  [
    () => props.target.containerId,
    () => props.target.instanceId,
    () => props.path,
    () => authSession.user.value?.subject,
    allowed,
  ],
  clear,
  { flush: 'sync' },
)
watch(
  () =>
    busy.value || destinationVisible.value || confirming.value || !!frozen.value || active.value,
  (value) => emit('locked', value),
  { immediate: true },
)
onBeforeUnmount(clear)
defineExpose({ open })
</script>

<template>
  <el-dialog
    v-if="allowed"
    v-model="visible"
    title="容器文件历史版本"
    width="min(960px, calc(100vw - 24px))"
    append-to-body
    :close-on-click-modal="false"
  >
    <section class="container-history" aria-label="容器历史版本">
      <p>
        可恢复到同一容器的原路径或其他已存在目录中的文件路径；内容会在任务中完整校验。关闭窗口不会取消已登记任务。
      </p>
      <p v-if="scope" class="container-history__path">{{ scope }}</p>
      <el-alert v-if="error" :title="error" type="error" :closable="false" />
      <div class="container-history__actions">
        <el-button :disabled="busy || active || !!frozen" @click="load()">刷新历史</el-button
        ><span v-if="busy" role="status">正在核对…</span>
      </div>
      <p v-if="incomplete">{{ incomplete }} 条损坏元数据未展示，不会用于恢复。</p>
      <div v-for="entry in entries" :key="entry.captureId" class="container-history__entry">
        <div class="container-history__details">
          <strong :title="entry.path">{{ entry.path }}</strong
          ><span
            >{{ new Date(entry.createdAtUnixMs).toLocaleString() }} ·
            {{ formatBytes(entry.sizeBytes) }}</span
          ><span :title="entry.captureId"
            >{{ entry.captureId.slice(0, 12) }} ·
            {{ entry.differentSourceDevice ? '异盘版本留存' : '同盘版本留存' }}</span
          >
        </div>
        <el-tag size="small" :type="entry.availability === 'Present' ? 'info' : 'warning'">{{
          {
            Present: '待内容校验',
            Incomplete: '捕获未完成',
            Missing: '内容缺失',
            Invalid: '内容异常',
          }[entry.availability]
        }}</el-tag>
        <el-button
          :disabled="disabled || busy || active || !!frozen || entry.availability !== 'Present'"
          :aria-label="`恢复 ${entry.path} ${entry.captureId.slice(0, 12)}`"
          @click="prepare(entry)"
          >恢复</el-button
        ><el-button
          :disabled="disabled || busy || active || !!frozen || entry.availability !== 'Present'"
          :aria-label="`恢复到其他路径 ${entry.path} ${entry.captureId.slice(0, 12)}`"
          @click="chooseDestination(entry)"
          >恢复到…</el-button
        >
      </div>
      <el-empty
        v-if="!entries.length && !busy && !error"
        :description="cursor ? '本页暂无匹配版本，可继续加载历史' : '暂无历史版本'"
        :image-size="48"
      />
      <el-button
        v-if="cursor"
        :disabled="busy || active || !!frozen || entries.length >= 1000"
        @click="load(true)"
        >加载更多历史</el-button
      >
      <div v-if="frozen" class="container-history__task">
        <p>登记回执尚未核实。仅可用原计划和原幂等键核对，不会新建另一份恢复计划。</p>
        <code>{{ frozen.idempotencyKey }}</code
        ><el-button :disabled="busy || disabled" @click="submit">重试登记原任务</el-button>
      </div>
      <div v-if="intent" class="container-history__task" role="status">
        <strong>{{ status }}</strong
        ><code>{{ intent.id }}</code>
        <p v-if="intent.state === 'Unknown'">
          已保留现场与发布保护；请在文件管理的管理员接管中核对，不要重复恢复。
        </p>
        <div class="container-history__actions">
          <el-button :disabled="busy" @click="query">核对恢复任务</el-button
          ><el-button
            v-if="active && intent.state !== 'Unknown'"
            :disabled="busy"
            @click="cancelConfirm = true"
            >请求取消恢复</el-button
          ><router-link to="/jobs">打开任务</router-link>
        </div>
      </div>
    </section>
  </el-dialog>
  <el-dialog
    v-if="allowed"
    v-model="destinationVisible"
    title="选择恢复目标"
    width="min(520px, calc(100vw - 24px))"
    append-to-body
    :close-on-click-modal="false"
    :close-on-press-escape="false"
  >
    <p class="container-history__path">历史来源：{{ destinationSource?.path }}</p>
    <p>填写同一容器内的相对文件路径。目标目录须已存在；下一步核对目标版本并确认覆盖。</p>
    <el-input
      v-model="destination"
      aria-label="恢复目标相对路径"
      :disabled="busy"
      @keyup.enter="prepareDestination"
    />
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <template #footer>
      <el-button :disabled="busy" @click="destinationVisible = false">取消</el-button>
      <el-button type="primary" :disabled="busy || disabled" @click="prepareDestination"
        >核对恢复目标</el-button
      >
    </template>
  </el-dialog>
  <el-dialog
    v-if="allowed"
    v-model="confirming"
    title="确认恢复容器文件"
    width="min(520px, calc(100vw - 24px))"
    append-to-body
    :close-on-click-modal="false"
    :close-on-press-escape="false"
  >
    <p class="container-history__path">历史来源：{{ selected?.path }}</p>
    <p class="container-history__path">恢复目标：{{ restorePath }}</p>
    <p v-if="selected">
      {{ formatBytes(selected.sizeBytes) }} ·
      {{ new Date(selected.createdAtUnixMs).toLocaleString() }}
    </p>
    <p>{{ plan?.warning }}</p>
    <template #footer
      ><el-button @click="dismissConfirmation">取消</el-button
      ><el-button type="primary" :disabled="busy || disabled" @click="submit"
        >确认并恢复</el-button
      ></template
    >
  </el-dialog>
  <el-dialog
    v-if="allowed"
    v-model="cancelConfirm"
    title="取消容器恢复"
    width="min(520px, calc(100vw - 24px))"
    append-to-body
    :close-on-click-modal="false"
    ><p>请求取消原任务；已完成的发布不会被撤销，取消结果以任务回执为准。</p>
    <template #footer
      ><el-button @click="cancelConfirm = false">保留任务</el-button
      ><el-button type="danger" :disabled="busy" @click="cancel">确认取消恢复</el-button></template
    ></el-dialog
  >
</template>

<style scoped>
.container-history,
.container-history__details {
  min-width: 0;
}
.container-history > p,
.container-history__task p {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.container-history__path {
  overflow-wrap: anywhere;
}
.container-history__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin: 12px 0;
}
.container-history__entry {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 12px 0;
  border-bottom: 1px solid var(--el-border-color-light);
}
.container-history__details {
  display: grid;
  gap: 4px;
  flex: 1 1 260px;
}
.container-history__details strong {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
  font-weight: 500;
}
.container-history__details span {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  overflow-wrap: anywhere;
}
.container-history__task {
  display: grid;
  gap: 8px;
  margin-top: 16px;
  padding: 12px;
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
}
.container-history__task code {
  overflow-wrap: anywhere;
  font-size: 12px;
}
.container-history__task .el-button,
.container-history__entry .el-button {
  margin-left: 0;
}
</style>
