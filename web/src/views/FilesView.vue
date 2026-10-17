<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Download, FolderOpened, RefreshRight, Upload } from '@element-plus/icons-vue'
import { ElBreadcrumb, ElBreadcrumbItem, ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import {
  downloadFile,
  filePath,
  fileQuery,
  fileResult,
  fileState,
  fileTerminal,
  hashFile,
  maximumFileBytes,
  uploadFile,
} from '@/services/fileManagement'
import type { FileEntry, FileIntent, FilePage, FilePlan, FileRoot } from '@/services/fileManagement'
import FileMutationPanel from '@/components/files/FileMutationPanel.vue'
import FileDirectoryTree from '@/components/files/FileDirectoryTree.vue'
import FileSearchPanel from '@/components/files/FileSearchPanel.vue'
import FileTextEditor from '@/components/files/FileTextEditor.vue'
import FileArchivePanel from '@/components/files/FileArchivePanel.vue'
import FileTreeCopyPanel from '@/components/files/FileTreeCopyPanel.vue'
import FileTreeMovePanel from '@/components/files/FileTreeMovePanel.vue'
import FileRecoveryPanel from '@/components/files/FileRecoveryPanel.vue'
import { canCopyDirectory } from '@/services/fileTreeCopies'
import { canMoveDirectory } from '@/services/fileTreeMoves'
import { archiveEligible } from '@/services/fileArchives'
import { textEligible } from '@/services/fileText'
import type { PreparedFileText } from '@/services/fileText'
import { mutationEligible } from '@/services/fileMutations'
import type { FileMutationKind } from '@/services/fileMutations'

const mutationPanel = ref<InstanceType<typeof FileMutationPanel> | null>(null),
  mutationOperationLocked = ref(false),
  editorLocked = ref(false),
  archiveOperationLocked = ref(false),
  treeCopyLocked = ref(false),
  treeMoveLocked = ref(false)
const archivePanel = ref<InstanceType<typeof FileArchivePanel> | null>(null)
const treeCopyPanel = ref<InstanceType<typeof FileTreeCopyPanel> | null>(null)
const treeMovePanel = ref<InstanceType<typeof FileTreeMovePanel> | null>(null)
const mutationLocked = computed(
  () =>
    mutationOperationLocked.value ||
    editorLocked.value ||
    archiveOperationLocked.value ||
    treeCopyLocked.value ||
    treeMoveLocked.value,
)
function canTreeCopy(entry: FileEntry) {
  return canCopyDirectory(
    roots.value.find((root) => root.id === rootId.value),
    entry,
  )
}
function canTreeMove(entry: FileEntry) {
  return canMoveDirectory(
    roots.value.find((root) => root.id === rootId.value),
    entry,
  )
}
const canHistory = computed(() =>
  ['files-list', 'files-download', 'files-history'].every((action) =>
    roots.value.find((root) => root.id === rootId.value)?.actions.includes(action),
  ),
)
const canArchive = computed(
  () =>
    canHistory.value &&
    ['files-upload', 'files-mutate', 'files-archive'].every((action) =>
      roots.value.find((root) => root.id === rootId.value)?.actions.includes(action),
    ),
)
const textEditor = ref<InstanceType<typeof FileTextEditor> | null>(null),
  editorSource = ref<PreparedFileText | null>(null)
const canEdit = computed(
  () =>
    canUpload.value &&
    roots.value.find((root) => root.id === rootId.value)?.actions.includes('files-download') ===
      true,
)
const treeRevision = ref(0)
const canMutate = computed(
  () =>
    roots.value.find((root) => root.id === rootId.value)?.actions.includes('files-mutate') === true,
)
function openMutation(kind: FileMutationKind, entry?: FileEntry) {
  mutationPanel.value?.open(kind, entry)
}
function fileAction(command: unknown, entry: FileEntry) {
  if (command === 'history' && canHistory.value) archivePanel.value?.openHistory(entry)
  else if (command === 'trash' && canArchive.value) archivePanel.value?.trash(entry)
  else if (command === 'edit' && canEdit.value) void textEditor.value?.open(entry)
  else if (command === 'copy' || command === 'move' || command === 'chmod')
    openMutation(command, entry)
}
function acceptText(prepared: PreparedFileText) {
  if (
    !authorized.value ||
    busy.value ||
    activeIntent.value ||
    frozen.value ||
    mutationLocked.value ||
    !canEdit.value
  )
    return
  if (
    prepared.rootId !== rootId.value ||
    prepared.path !== filePath(path.value, prepared.file.name)
  )
    return
  chosen.value = prepared.file
  editorSource.value = prepared
  void start()
}

const roots = ref<FileRoot[]>([]),
  rootId = ref(''),
  path = ref(''),
  entries = ref<FileEntry[]>([])
const cursors = ref(['']),
  page = ref(0),
  nextCursor = ref(''),
  folderVersion = ref('')
const loading = ref(false),
  busy = ref(false),
  error = ref(''),
  trace = ref(''),
  phase = ref(''),
  progress = ref(0)
const chosen = ref<File | null>(null),
  fileInput = ref<HTMLInputElement | null>(null),
  intent = ref<FileIntent | null>(null)
const frozen = ref<{ planHash: string; confirmed: true; idempotencyKey: string } | null>(null)
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const activeIntent = computed(() => intent.value !== null && !fileTerminal(intent.value.state))
const canUpload = computed(
  () =>
    roots.value.find((root) => root.id === rootId.value)?.actions.includes('files-upload') === true,
)
const crumbs = computed(() => (path.value ? path.value.split('/') : []))
let listing: AbortController | null = null,
  transfer: AbortController | null = null,
  poll: number | null = null
let generation = 0
let pollingGeneration: number | null = null
function message(reason: unknown) {
  error.value =
    reason instanceof ApiError
      ? (
          {
            401: '登录已失效，请重新登录',
            403: '权限不足或此文件受到保护',
            404: '文件或任务不存在，请刷新',
            409: '版本或操作状态已变化，请查询原任务并刷新',
            429: '传输或存储预算已用完，请稍后再试',
            503: 'Agent 暂时不可用',
          } as Record<number, string>
        )[reason.status] || reason.message
      : reason instanceof Error
        ? reason.message
        : '文件操作失败'
  trace.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
  if (
    reason instanceof ApiError &&
    (reason.status === 401 ||
      (reason.status === 403 && reason.problem.code !== 'AGENT_OPERATION_FORBIDDEN'))
  ) {
    entries.value = []
    roots.value = []
    rootId.value = ''
    stop()
    chosen.value = null
    editorSource.value = null
    revokeDrafts()
    frozen.value = null
    intent.value = null
  }
}
function browserFailure(reason: unknown) {
  if (reason instanceof ApiError && (reason.status === 409 || reason.status === 429)) {
    error.value =
      reason.status === 409
        ? '目录已变化或搜索续页已过期，请刷新目录树或重新搜索。'
        : '文件服务当前繁忙，请稍后重新搜索或展开目录。'
    trace.value = reason.problem.traceId || ''
  } else message(reason)
}
function stop() {
  listing?.abort()
  transfer?.abort()
  if (poll !== null) window.clearTimeout(poll)
  poll = null
  generation++
  busy.value = false
}
async function load(reset = false) {
  if (!authorized.value || !rootId.value) return
  listing?.abort()
  listing = new AbortController()
  const request = listing
  if (reset) {
    page.value = 0
    cursors.value = ['']
    folderVersion.value = ''
  }
  entries.value = []
  loading.value = true
  error.value = ''
  trace.value = ''
  try {
    const result = await apiRequest<FilePage>(
      `files/entries?${fileQuery(rootId.value, path.value, { after: cursors.value[page.value] || '', take: '100' })}`,
      { signal: request.signal },
    )
    if (request !== listing || request.signal.aborted) return
    if (page.value > 0 && folderVersion.value !== result.version) {
      nextCursor.value = ''
      throw new Error('目录内容已变化，请刷新后重新分页')
    }
    entries.value = result.entries
    nextCursor.value = result.nextCursor
    folderVersion.value = result.version
  } catch (reason) {
    if (!request.signal.aborted && request === listing) message(reason)
  } finally {
    if (request === listing) loading.value = false
  }
}
function navigate(value: string) {
  path.value = value
  void load(true)
}
function previousPage() {
  page.value--
  void load()
}
function nextPage() {
  cursors.value[page.value + 1] = nextCursor.value
  page.value++
  void load()
}
function selectFile(event: Event) {
  editorSource.value = null
  chosen.value = (event.target as HTMLInputElement).files?.[0] || null
  if (fileInput.value) fileInput.value.value = ''
}
const draftUrls = new Set<string>()
function revokeDrafts() {
  for (const url of draftUrls) URL.revokeObjectURL(url)
  draftUrls.clear()
}
function downloadDraft() {
  if (!authorized.value || !editorSource.value || !chosen.value) return
  const url = URL.createObjectURL(editorSource.value.file),
    anchor = document.createElement('a')
  draftUrls.add(url)
  anchor.href = url
  anchor.download = editorSource.value.file.name + '.draft'
  anchor.click()
  window.setTimeout(() => {
    URL.revokeObjectURL(url)
    draftUrls.delete(url)
  }, 1000)
}
async function discardDraft() {
  if (busy.value || activeIntent.value || frozen.value || !editorSource.value) return
  try {
    await ElMessageBox.confirm('仅丢弃本页尚未提交的文本草稿，不会修改服务器文件。', '放弃草稿？', {
      type: 'warning',
      confirmButtonText: '放弃草稿',
      cancelButtonText: '保留草稿',
    })
  } catch {
    return
  }
  chosen.value = null
  editorSource.value = null
  phase.value = intent.value ? fileState(intent.value.state) : ''
  revokeDrafts()
}
async function createIntent(signal: AbortSignal) {
  if (!frozen.value) throw new Error('没有待登记的请求')
  const result = await apiRequest<FileIntent>('files/uploads', {
    method: 'POST',
    signal,
    headers: { 'Idempotency-Key': frozen.value.idempotencyKey },
    body: JSON.stringify(frozen.value),
  })
  intent.value = result
  frozen.value = null
  return result
}
async function start(retry = false) {
  if (
    !authorized.value ||
    !chosen.value ||
    busy.value ||
    activeIntent.value ||
    mutationLocked.value
  )
    return
  if (chosen.value.size > maximumFileBytes) {
    error.value = '单文件最多 1 GiB'
    return
  }
  const file = chosen.value
  const source = editorSource.value
  transfer = new AbortController()
  const request = transfer
  const token = ++generation
  busy.value = true
  error.value = ''
  progress.value = 0
  try {
    if (!retry) {
      intent.value = null
      phase.value = '计算 SHA-256'
      const digest = await hashFile(file, request.signal, (value) => {
        if (token === generation) progress.value = value
      })
      const targetPath = filePath(path.value, file.name)
      if (
        source &&
        (source.file !== file || source.rootId !== rootId.value || source.path !== targetPath)
      )
        throw new Error('编辑目录上下文已变化，请重新打开文件')
      let existing: FileEntry | null = null
      try {
        existing = await apiRequest<FileEntry>(
          `files/entry?${fileQuery(rootId.value, targetPath)}`,
          { signal: request.signal },
        )
      } catch (reason) {
        if (!(reason instanceof ApiError && reason.status === 404)) throw reason
      }
      if (existing && (existing.kind !== 'File' || existing.isProtected))
        throw new Error('不能覆盖目录或受保护文件')
      if (source && existing?.version !== source.expectedVersion)
        throw new Error('文件版本已变化，未保存；请重新打开后编辑')
      const plan = await apiRequest<FilePlan>('files/uploads/plan', {
        method: 'POST',
        signal: request.signal,
        body: JSON.stringify({
          rootId: rootId.value,
          path: targetPath,
          sizeBytes: file.size,
          sha256: digest,
          expectedVersion: source?.expectedVersion || existing?.version || 'ABSENT',
        }),
      })
      phase.value = '等待确认'
      await ElMessageBox.confirm(
        `${targetPath}\n${formatBytes(file.size)}\n${plan.warning}`,
        plan.overwrites ? '确认覆盖文件' : '确认上传文件',
        { type: 'warning', confirmButtonText: '确认并上传', cancelButtonText: '取消' },
      )
      if (request.signal.aborted || token !== generation) return
      frozen.value = {
        planHash: plan.planHash,
        confirmed: true,
        idempotencyKey: createIdempotencyKey(),
      }
    }
    phase.value = '登记上传任务'
    const created = await createIntent(request.signal)
    if (token !== generation) return
    if (created.state === 'AwaitingUpload') {
      phase.value = '传输中'
      progress.value = 0
      try {
        intent.value = await uploadFile(created, file, request.signal, (value) => {
          if (token === generation) progress.value = value
        })
      } catch (reason) {
        if (!request.signal.aborted) message(reason)
      }
    }
    if (!request.signal.aborted && token === generation) {
      phase.value = fileState(intent.value!.state)
      await checkIntent(token)
    }
  } catch (reason) {
    if (source && (reason === 'cancel' || reason === 'close')) phase.value = '编辑草稿尚未提交'
    if (!request.signal.aborted && reason !== 'cancel' && reason !== 'close') message(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      chosen.value =
        frozen.value ||
        (source && intent.value?.state !== 'Succeeded' && intent.value?.state !== 'Superseded')
          ? file
          : null
      if (!chosen.value) editorSource.value = null
    }
  }
}
async function checkIntent(token = generation) {
  if (!intent.value || !authorized.value || pollingGeneration === token) return
  if (poll !== null) window.clearTimeout(poll)
  poll = null
  pollingGeneration = token
  try {
    const result = await apiRequest<FileIntent>(`files/uploads/${intent.value.id}`)
    if (token !== generation) return
    intent.value = result
    phase.value = fileState(result.state)
    if (fileTerminal(result.state)) {
      if (result.state === 'Succeeded' || result.state === 'Superseded') {
        chosen.value = null
        editorSource.value = null
      }
      if (result.state === 'Succeeded') {
        ElMessage.success('文件已校验并发布')
        void load(true)
      }
      return
    }
    if (result.state !== 'Unknown') poll = window.setTimeout(() => void checkIntent(token), 2000)
  } catch (reason) {
    if (token === generation) message(reason)
  } finally {
    if (pollingGeneration === token) pollingGeneration = null
  }
}
async function cancel() {
  const current = intent.value
  stop()
  frozen.value = null
  chosen.value = null
  editorSource.value = null
  if (current && !fileTerminal(current.state)) {
    try {
      await apiRequest(`files/uploads/${current.id}/cancel`, {
        method: 'POST',
        headers: { 'Idempotency-Key': current.id },
      })
      phase.value = '已请求取消，等待宿主确认'
      void checkIntent()
    } catch (reason) {
      message(reason)
    }
  } else {
    phase.value = ''
    progress.value = 0
  }
}
async function download(entry: FileEntry) {
  if (busy.value || activeIntent.value || mutationLocked.value || entry.isProtected) return
  transfer = new AbortController()
  const request = transfer
  const token = ++generation
  busy.value = true
  phase.value = '下载中'
  progress.value = 0
  error.value = ''
  try {
    const blob = await downloadFile(
      rootId.value,
      filePath(path.value, entry.name),
      entry,
      request.signal,
      (value) => {
        if (token === generation) progress.value = value
      },
    )
    if (request.signal.aborted || token !== generation) return
    const url = URL.createObjectURL(blob),
      anchor = document.createElement('a')
    anchor.href = url
    anchor.download = entry.name
    anchor.click()
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
    phase.value = '文件已校验，已交给浏览器保存'
  } catch (reason) {
    if (!request.signal.aborted) message(reason)
  } finally {
    if (token === generation) busy.value = false
  }
}
watch(authorized, (value) => {
  if (!value) {
    revokeDrafts()
    stop()
    entries.value = []
    roots.value = []
    rootId.value = ''
    chosen.value = null
    editorSource.value = null
    frozen.value = null
    intent.value = null
  }
})
async function loadRoots() {
  if (!authorized.value) return
  loading.value = true
  listing = new AbortController()
  const request = listing
  try {
    const result = await apiRequest<FileRoot[]>('files/roots', { signal: request.signal })
    if (!request.signal.aborted) {
      roots.value = result
      rootId.value = result[0]?.id || ''
      if (rootId.value) await load(true)
      else loading.value = false
    }
  } catch (reason) {
    if (!request.signal.aborted) {
      loading.value = false
      message(reason)
    }
  }
}
function refresh() {
  treeRevision.value++
  if (roots.value.length === 0) void loadRoots()
  else void load(true)
}
function afterMutation() {
  treeRevision.value++
  void load(true)
}
onMounted(loadRoots)
onBeforeUnmount(() => {
  revokeDrafts()
  const current = intent.value
  const cancelReceiving = busy.value && current?.state === 'AwaitingUpload'
  stop()
  if (current && cancelReceiving)
    void apiRequest(`files/uploads/${current.id}/cancel`, {
      method: 'POST',
      headers: { 'Idempotency-Key': current.id },
    }).catch(() => {})
})
</script>

<template>
  <div class="files-page" data-testid="files-page" v-if="authorized">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">FILES</span>
        <h1>文件管理</h1>
        <p>仅访问登记目录；数据库、系统配置和密钥使用专用管理入口。</p>
      </div>
      <div class="page-heading__actions">
        <FileRecoveryPanel @changed="afterMutation" @failure="message" />
        <el-button :loading="loading" @click="refresh"
          ><el-icon><RefreshRight /></el-icon>刷新</el-button
        >
      </div>
    </header>
    <el-alert
      v-if="error"
      :title="error"
      :description="trace ? `追踪号：${trace}` : undefined"
      type="error"
      show-icon
      :closable="false"
    />
    <section class="panel files-panel">
      <div class="files-toolbar">
        <el-select
          v-model="rootId"
          aria-label="文件根目录"
          :disabled="busy || !!frozen || mutationLocked"
          @change="navigate('')"
          ><el-option v-for="root in roots" :key="root.id" :value="root.id" :label="root.name"
        /></el-select>
        <div class="files-upload">
          <el-button
            v-if="canHistory"
            :disabled="busy || activeIntent || !!frozen || mutationOperationLocked || editorLocked"
            @click="archivePanel?.openHistory()"
            >历史与回收站</el-button
          >
          <el-button
            v-if="canMutate"
            :disabled="busy || activeIntent || !!frozen || mutationLocked"
            @click="openMutation('mkdir')"
            >新建目录</el-button
          >
          <input
            ref="fileInput"
            type="file"
            class="file-input"
            aria-label="选择上传文件"
            :disabled="busy || activeIntent || !!frozen || !canUpload || mutationLocked"
            @change="selectFile"
          />
          <el-button
            :disabled="busy || activeIntent || !!frozen || !canUpload || mutationLocked"
            @click="fileInput?.click()"
            ><el-icon><Upload /></el-icon>选择文件</el-button
          >
          <el-button
            v-if="chosen"
            type="primary"
            :disabled="busy || activeIntent || mutationLocked"
            @click="start(!!frozen)"
            >{{ frozen ? '重试原登记' : editorSource ? '校验并保存' : '校验并上传' }}</el-button
          >
          <el-button
            v-if="chosen && editorSource"
            :disabled="busy || mutationLocked"
            @click="downloadDraft"
            >下载草稿</el-button
          >
          <el-button
            v-if="chosen && editorSource && !activeIntent && !frozen"
            :disabled="busy || mutationLocked"
            @click="discardDraft"
            >放弃草稿</el-button
          >
        </div>
      </div>
      <FileMutationPanel
        ref="mutationPanel"
        :roots="roots"
        :root-id="rootId"
        :path="path"
        :disabled="
          busy ||
          activeIntent ||
          !!frozen ||
          editorLocked ||
          archiveOperationLocked ||
          treeCopyLocked ||
          treeMoveLocked
        "
        @locked="mutationOperationLocked = $event"
        @changed="afterMutation"
        @failure="message"
      />
      <FileTextEditor
        ref="textEditor"
        :root-id="rootId"
        :path="path"
        :disabled="
          busy ||
          activeIntent ||
          !!frozen ||
          mutationOperationLocked ||
          archiveOperationLocked ||
          treeCopyLocked ||
          treeMoveLocked
        "
        @locked="editorLocked = $event"
        @prepared="acceptText"
        @failure="message"
      />
      <FileArchivePanel
        ref="archivePanel"
        :roots="roots"
        :root-id="rootId"
        :path="path"
        :disabled="
          busy ||
          activeIntent ||
          !!frozen ||
          mutationOperationLocked ||
          editorLocked ||
          treeCopyLocked ||
          treeMoveLocked
        "
        @locked="archiveOperationLocked = $event"
        @changed="afterMutation"
        @failure="message"
      />
      <FileTreeCopyPanel
        ref="treeCopyPanel"
        :roots="roots"
        :root-id="rootId"
        :path="path"
        :disabled="
          busy ||
          activeIntent ||
          !!frozen ||
          mutationOperationLocked ||
          editorLocked ||
          archiveOperationLocked ||
          treeMoveLocked
        "
        @locked="treeCopyLocked = $event"
        @changed="afterMutation"
        @failure="message"
      />
      <FileTreeMovePanel
        ref="treeMovePanel"
        :roots="roots"
        :root-id="rootId"
        :path="path"
        :disabled="
          busy ||
          activeIntent ||
          !!frozen ||
          mutationOperationLocked ||
          editorLocked ||
          archiveOperationLocked ||
          treeCopyLocked
        "
        @locked="treeMoveLocked = $event"
        @changed="afterMutation"
        @failure="message"
      />
      <el-breadcrumb class="file-crumbs"
        ><el-breadcrumb-item
          ><button
            class="file-link"
            :disabled="busy || !!frozen || mutationLocked"
            @click="navigate('')"
          >
            根目录
          </button></el-breadcrumb-item
        >
        <el-breadcrumb-item v-for="(part, index) in crumbs" :key="index"
          ><button
            class="file-link"
            :disabled="busy || !!frozen || mutationLocked"
            @click="navigate(crumbs.slice(0, index + 1).join('/'))"
          >
            {{ part }}
          </button></el-breadcrumb-item
        ></el-breadcrumb
      >
      <div class="file-browser-tools">
        <FileDirectoryTree
          :root-id="rootId"
          :name="roots.find((root) => root.id === rootId)?.name || '根目录'"
          :disabled="busy || !!frozen || mutationLocked"
          :revision="treeRevision"
          @navigate="navigate"
          @failure="browserFailure"
        />
        <FileSearchPanel
          :root-id="rootId"
          :path="path"
          :disabled="busy || !!frozen || mutationLocked"
          @navigate="navigate"
          @failure="browserFailure"
        />
      </div>
      <div v-if="chosen" class="file-picked" :title="chosen.name">
        {{ chosen.name }} · {{ formatBytes(chosen.size) }}
      </div>
      <div v-if="phase || intent || frozen" class="file-transfer" aria-live="polite">
        <div class="transfer-line">
          <span>{{ phase }}</span
          ><el-button
            v-if="busy || frozen || (intent && !fileTerminal(intent.state))"
            size="small"
            @click="cancel"
            >取消</el-button
          >
        </div>
        <el-progress v-if="busy" :percentage="Math.round(progress)" :stroke-width="6" />
        <template v-if="intent"
          ><p class="file-target">{{ intent.rootId }} / {{ intent.path }}</p>
          <p>
            任务 <router-link :to="`/jobs?jobId=${intent.jobId}`">{{ intent.jobId }}</router-link>
          </p>
          <p v-if="intent.resultCode" :title="intent.resultCode">
            {{ fileResult(intent.resultCode) }}
          </p>
          <el-alert
            v-if="intent.state === 'Unknown'"
            title="结果未知：可能已经发布，请勿重新上传覆盖。保留现场，待回执核实。"
            type="warning"
            :closable="false"
          />
          <el-button v-if="!fileTerminal(intent.state)" size="small" @click="checkIntent()"
            >查询原任务</el-button
          ></template
        >
        <p v-if="frozen">登记响应可能丢失，原请求已冻结。可重试原登记，不会生成第二个任务。</p>
      </div>
      <div class="files-table" v-loading="loading">
        <el-table :data="entries" empty-text="此目录暂无可显示的文件" table-layout="fixed">
          <el-table-column label="名称" min-width="220"
            ><template #default="{ row }"
              ><button
                v-if="row.kind === 'Directory' && !row.isProtected"
                class="file-link file-name"
                :title="row.name"
                :disabled="busy || !!frozen || mutationLocked"
                @click="navigate(filePath(path, row.name))"
              >
                <el-icon><FolderOpened /></el-icon
                ><span class="file-name-label">{{ row.name }}</span></button
              ><span v-else class="file-name" :title="row.name">{{ row.name }}</span></template
            ></el-table-column
          >
          <el-table-column label="大小" width="110"
            ><template #default="{ row }">{{
              row.kind === 'File' ? formatBytes(row.sizeBytes) : '—'
            }}</template></el-table-column
          >
          <el-table-column label="修改时间" width="180"
            ><template #default="{ row }">{{
              new Date(row.modifiedAtUnixMs).toLocaleString()
            }}</template></el-table-column
          >
          <el-table-column prop="mode" label="权限" width="80" />
          <el-table-column label="操作" width="160" fixed="right"
            ><template #default="{ row }"
              ><el-tag v-if="row.isProtected" type="info" size="small">受保护</el-tag>
              <div v-else-if="row.kind === 'File'" class="files-row-actions">
                <el-button
                  link
                  type="primary"
                  :disabled="busy || activeIntent || mutationLocked"
                  @click="download(row)"
                  ><el-icon><Download /></el-icon>下载</el-button
                >
                <el-dropdown
                  v-if="
                    canHistory || ((canMutate || canEdit || canArchive) && mutationEligible(row))
                  "
                  trigger="click"
                  :disabled="busy || activeIntent || !!frozen || mutationLocked"
                  @command="fileAction($event, row)"
                >
                  <el-button
                    link
                    type="primary"
                    :disabled="busy || activeIntent || !!frozen || mutationLocked"
                    :aria-label="`更多文件操作 ${row.name}`"
                    >更多</el-button
                  >
                  <template #dropdown
                    ><el-dropdown-menu
                      ><el-dropdown-item
                        v-if="canEdit"
                        command="edit"
                        :disabled="!textEligible(row)"
                        >编辑文本</el-dropdown-item
                      ><el-dropdown-item v-if="canHistory" command="history"
                        >历史版本</el-dropdown-item
                      ><el-dropdown-item v-if="canMutate" command="copy">复制文件</el-dropdown-item
                      ><el-dropdown-item v-if="canMutate" command="move">移动文件</el-dropdown-item
                      ><el-dropdown-item v-if="canMutate" command="chmod">修改权限</el-dropdown-item
                      ><el-dropdown-item
                        v-if="canArchive"
                        command="trash"
                        :disabled="!archiveEligible(row)"
                        divided
                        >移入回收站</el-dropdown-item
                      ></el-dropdown-menu
                    ></template
                  >
                </el-dropdown>
              </div>
              <div v-else-if="canTreeCopy(row) || canTreeMove(row)" class="file-directory-actions">
                <el-button
                  v-if="canTreeCopy(row)"
                  link
                  type="primary"
                  :disabled="busy || activeIntent || !!frozen || mutationLocked"
                  @click="treeCopyPanel?.open(row)"
                  >复制目录</el-button
                >
                <el-button
                  v-if="canTreeMove(row)"
                  link
                  type="primary"
                  :disabled="busy || activeIntent || !!frozen || mutationLocked"
                  @click="treeMovePanel?.open(row)"
                  >移动目录</el-button
                >
              </div>
              <span v-else>目录</span></template
            ></el-table-column
          >
        </el-table>
      </div>
      <div class="files-pagination">
        <span>第 {{ page + 1 }} 页 · 每页最多 100 项</span>
        <div>
          <el-button :disabled="page === 0 || loading" @click="previousPage">上一页</el-button
          ><el-button :disabled="!nextCursor || loading || page >= 9999" @click="nextPage"
            >下一页</el-button
          >
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.file-directory-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.file-directory-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}
.files-page {
  min-width: 0;
  display: grid;
  gap: 16px;
}
.file-browser-tools {
  display: grid;
  grid-template-columns: 200px minmax(0, 1fr);
  gap: 16px;
  margin: 16px 0;
  align-items: start;
}
@media (max-width: 1100px) {
  .file-browser-tools {
    grid-template-columns: minmax(0, 1fr);
  }
}
.files-row-actions {
  display: flex;
  gap: 12px;
  align-items: center;
  flex-wrap: nowrap;
}
.files-panel {
  min-width: 0;
  padding: 20px;
}
.files-toolbar,
.files-pagination,
.transfer-line {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.files-toolbar .el-select {
  width: 240px;
  max-width: 100%;
}
.files-upload {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.files-upload .el-button + .el-button {
  margin-left: 0;
}
.file-input {
  display: none;
}
.file-crumbs {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 0;
  margin: 20px 0;
}
.file-link {
  padding: 0;
  background: transparent;
  border: 0;
  color: var(--el-color-primary);
  font: inherit;
  cursor: pointer;
  text-align: left;
}
.file-link:disabled {
  opacity: 0.5;
  cursor: default;
}
.file-name {
  display: flex;
  align-items: center;
  gap: 8px;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-name:not(button) {
  display: block;
}
.file-name > .el-icon {
  flex-shrink: 0;
}
.file-name-label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-crumbs .file-link {
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-transfer {
  margin: 14px 0;
  padding: 14px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
  overflow-wrap: anywhere;
}
.file-transfer p {
  margin: 8px 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.file-target,
.file-picked {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-picked {
  margin-bottom: 12px;
  font-size: 13px;
}
.files-table {
  width: 100%;
  min-width: 0;
  overflow: hidden;
}
.files-pagination {
  margin-top: 16px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
@media (max-width: 600px) {
  /* Keep the whole directory link's hit target inside the area left of the
     fixed action column; a long label's center must not sit underneath it. */
  button.file-name {
    max-width: min(100%, calc(100vw - 240px));
  }
  .files-panel {
    padding: 12px;
  }
  .files-toolbar .el-select {
    width: 100%;
  }
  .file-crumbs .file-link {
    max-width: 110px;
  }
}
</style>
