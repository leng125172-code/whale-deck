<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, shallowRef } from 'vue'
import { ElCard, ElCheckbox, ElProgress } from 'element-plus'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { workspaceError } from '@/services/developmentWorkspaces'
import {
  identifySource,
  sameSourceArchive,
  sameSourcePlan,
  sourceChunkBytes,
  sourceFormat,
  SourceInputError,
  sourceStates,
  workspaceSources as api,
  type SourceArchive,
  type SourceImport,
} from '@/services/developmentWorkspaceSources'

const props = defineProps<{ workspaceId: string }>()
const lifecycle = new AbortController()
const current = shallowRef<SourceImport | null>(null)
const selected = shallowRef<File | null>(null)
const fileInput = ref<HTMLInputElement>()
const busy = ref<'refresh' | 'upload' | 'publish' | 'cancel' | ''>('')
const error = ref('')
const confirmed = ref(false)
const synchronized = ref(false)
const denied = ref(false)
const hashInProgress = ref(false)
let transfer: AbortController | undefined
let pending: { id: string; archive: SourceArchive } | undefined
let timer: number | undefined

const canUpload = computed(
  () =>
    !denied.value &&
    (!current.value || ['Uploading', 'Canceled', 'Failed'].includes(current.value.state)),
)
const canCancel = computed(
  () =>
    synchronized.value &&
    !!current.value &&
    ['Uploading', 'Ready', 'Staged'].includes(current.value.state),
)
const percentage = computed(() =>
  current.value
    ? Math.floor((100 * current.value.receivedBytes) / current.value.archive.archiveBytes)
    : 0,
)
const byteLabel = (bytes: number) => new Intl.NumberFormat().format(bytes) + ' 字节'

onMounted(() => {
  refresh()
  timer = window.setInterval(() => {
    if (
      document.visibilityState === 'visible' &&
      !denied.value &&
      !busy.value &&
      current.value &&
      ['Ready', 'Publishing', 'Canceling'].includes(current.value.state)
    )
      refresh()
  }, 2500)
})
onUnmounted(() => {
  lifecycle.abort()
  if (timer !== undefined) window.clearInterval(timer)
})

function accept(result: SourceImport | null, signal: AbortSignal) {
  signal.throwIfAborted()
  if (
    current.value?.importId !== result?.importId ||
    !sameSourcePlan(current.value?.plan ?? null, result?.plan ?? null) ||
    result?.state !== 'Staged'
  )
    confirmed.value = false
  current.value = result
  if (result && pending?.id === result.importId) pending = undefined
  denied.value = false
  synchronized.value = true
}

function clearFile() {
  selected.value = null
  if (fileInput.value) fileInput.value.value = ''
}

async function load(signal: AbortSignal) {
  synchronized.value = false
  accept(await api.current(props.workspaceId, signal), signal)
}

async function run(action: typeof busy.value, work: (signal: AbortSignal) => Promise<void>) {
  if (busy.value) return
  const signal = lifecycle.signal
  busy.value = action
  error.value = ''
  try {
    await work(signal)
  } catch (failure) {
    if (signal.aborted) return
    if (failure instanceof ApiError && [401, 403, 404].includes(failure.status)) {
      denied.value = true
      current.value = null
      confirmed.value = false
      synchronized.value = false
      pending = undefined
      clearFile()
    }
    error.value = failure instanceof SourceInputError ? failure.message : workspaceError(failure)
  } finally {
    if (!signal.aborted) busy.value = ''
  }
}

function refresh() {
  void run('refresh', load)
}

function selectFile(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  selected.value = null
  error.value = ''
  if (!file) return
  try {
    sourceFormat(file)
    selected.value = file
  } catch (failure) {
    clearFile()
    error.value = failure instanceof SourceInputError ? failure.message : workspaceError(failure)
  }
}

function upload() {
  const file = selected.value
  if (!file || !canUpload.value) return
  void run('upload', async (signal) => {
    const controller = new AbortController()
    transfer = controller
    const uploadSignal = AbortSignal.any([signal, controller.signal])
    try {
      await load(uploadSignal)
      if (!canUpload.value) {
        clearFile()
        return
      }
      hashInProgress.value = true
      const archive = await identifySource(file, uploadSignal)
      hashInProgress.value = false
      let result = current.value
      if (result?.state === 'Uploading') {
        if (!sameSourceArchive(result.archive, archive))
          throw new SourceInputError('所选文件与待续传归档的格式、大小或摘要不一致。')
      } else {
        if (!pending || !sameSourceArchive(pending.archive, archive))
          pending = { id: createIdempotencyKey(), archive }
        synchronized.value = false
        result = await api.begin(props.workspaceId, pending.id, archive, uploadSignal)
        accept(result, uploadSignal)
      }
      while (result.state === 'Uploading') {
        const offset = result.receivedBytes
        const body = file.slice(offset, offset + sourceChunkBytes)
        synchronized.value = false
        result = await api.chunk(props.workspaceId, result, body, uploadSignal)
        accept(result, uploadSignal)
      }
      clearFile()
    } catch (failure) {
      if (signal.aborted || !controller.signal.aborted) throw failure
      await load(signal)
    } finally {
      transfer = undefined
      hashInProgress.value = false
    }
  })
}

function publish() {
  const result = current.value
  const digest = result?.plan?.planSha256
  if (!confirmed.value || !synchronized.value || result?.state !== 'Staged' || !digest) return
  void run('publish', async (signal) => {
    confirmed.value = false
    synchronized.value = false
    accept(await api.publish(props.workspaceId, result, digest, signal), signal)
  })
}

function cancel() {
  const result = current.value
  if (!result || !canCancel.value) return
  void run('cancel', async (signal) => {
    confirmed.value = false
    synchronized.value = false
    accept(await api.cancel(props.workspaceId, result, signal), signal)
    clearFile()
  })
}
</script>

<template>
  <el-card class="source-card" shadow="never">
    <template #header><h2>导入源码归档</h2></template>
    <div class="source-content">
      <p>支持 ZIP、tar、tar.gz，最多 128 MiB。暂存通过后可核对计划并发布首个工作树。</p>
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <p v-if="!synchronized && !busy && !denied" role="status">
        服务器状态待确认。刷新状态后可继续操作。继续上传时会先核对已接收字节数。
      </p>
      <template v-if="current">
        <div class="source-actions" role="status">
          <el-tag effect="plain">{{ sourceStates[current.state] }}</el-tag>
          <span v-if="current.code"
            ><code>{{ current.code }}</code></span
          >
        </div>
        <dl>
          <dt>导入 ID</dt>
          <dd>
            <code>{{ current.importId }}</code>
          </dd>
          <dt>归档</dt>
          <dd>{{ current.archive.format }} · {{ byteLabel(current.archive.archiveBytes) }}</dd>
          <dt>归档摘要</dt>
          <dd>
            <code>{{ current.archive.archiveSha256 }}</code>
          </dd>
          <template v-if="['Uploading', 'Ready', 'Staged'].includes(current.state)">
            <dt>确认截止</dt>
            <dd>
              <time :datetime="current.expiresAtUtc">{{
                new Date(current.expiresAtUtc).toLocaleString()
              }}</time>
            </dd>
          </template>
          <template v-if="current.plan">
            <dt>内容摘要</dt>
            <dd>
              <code>{{ current.plan.contentSha256 }}</code>
            </dd>
            <dt>暂存内容</dt>
            <dd>{{ current.plan.fileCount }} 个文件 · {{ byteLabel(current.plan.totalBytes) }}</dd>
            <dt>计划摘要</dt>
            <dd>
              <code>{{ current.plan.planSha256 }}</code>
            </dd>
          </template>
        </dl>
        <template v-if="current.state === 'Uploading' || busy === 'upload'">
          <p id="workspace-source-progress">
            已接收 {{ byteLabel(current.receivedBytes) }} /
            {{ byteLabel(current.archive.archiveBytes) }}
          </p>
          <el-progress :percentage="percentage" :aria-label="`上传进度 ${percentage}%`" />
        </template>
        <p v-if="current.state === 'Published'">源码已发布到该空间，可前往运行记录执行开发任务。</p>
      </template>
      <div v-if="canUpload" class="source-upload">
        <label for="workspace-source-file">{{
          current?.state === 'Uploading' ? '重新选择原归档' : '选择归档文件'
        }}</label>
        <input
          id="workspace-source-file"
          ref="fileInput"
          type="file"
          accept=".zip,.tar,.tar.gz,.tgz"
          :disabled="!!busy"
          @change="selectFile"
        />
      </div>
      <div v-if="current?.state === 'Staged'" class="source-confirm">
        <el-checkbox v-model="confirmed" :disabled="!!busy || !synchronized"
          >我已核对暂存计划，确认发布到该空间</el-checkbox
        >
      </div>
      <div class="source-actions">
        <el-button
          v-if="canUpload"
          type="primary"
          :disabled="!!busy || !selected"
          :loading="busy === 'upload'"
          @click="upload"
        >
          {{
            hashInProgress
              ? '核对归档摘要'
              : current?.state === 'Uploading'
                ? '继续上传'
                : '开始上传'
          }}
        </el-button>
        <el-button v-if="busy === 'upload'" @click="transfer?.abort()">暂停传输</el-button>
        <el-button
          v-if="current?.state === 'Staged'"
          type="primary"
          :disabled="!!busy || !synchronized || !confirmed"
          :loading="busy === 'publish'"
          @click="publish"
          >确认发布源码</el-button
        >
        <el-button v-if="canCancel" type="danger" plain :disabled="!!busy" @click="cancel"
          >取消导入</el-button
        >
        <el-button :disabled="!!busy" :loading="busy === 'refresh'" @click="refresh"
          >刷新导入状态</el-button
        >
      </div>
    </div>
  </el-card>
</template>

<style scoped>
.source-card h2 {
  margin: 0;
  font-size: 1.05rem;
}
.source-content,
.source-upload {
  display: grid;
  min-width: 0;
  gap: 12px;
}
.source-content p,
.source-content dl {
  margin: 0;
}
.source-content dl {
  display: grid;
  grid-template-columns: minmax(4.5rem, max-content) minmax(0, 1fr);
  gap: 8px 12px;
}
.source-content dt {
  color: var(--el-text-color-secondary);
}
.source-content dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.source-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
}
.source-actions :deep(.el-button) {
  margin: 0;
}
.source-upload input {
  max-width: 100%;
  color: var(--el-text-color-regular);
}
.source-upload input::file-selector-button {
  margin-inline-end: 8px;
  padding: 6px 10px;
  border: 1px solid var(--el-border-color);
  border-radius: var(--el-border-radius-base);
  background: var(--el-fill-color-light);
  color: var(--el-text-color-primary);
  font: inherit;
}
.source-confirm :deep(.el-checkbox) {
  height: auto;
  white-space: normal;
}
</style>
