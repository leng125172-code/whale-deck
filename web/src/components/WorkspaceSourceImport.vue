<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, shallowRef } from 'vue'
import { ElCard, ElCheckbox, ElInput, ElOption, ElProgress, ElSelect } from 'element-plus'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { workspaceError } from '@/services/developmentWorkspaces'
import {
  sameSourcePlan,
  sameSourceOrigin,
  sourceStates,
  validGitRef,
  workspaceSources as api,
  type SourceImport,
  type GitSourceInput,
} from '@/services/developmentWorkspaceSources'
import {
  identifySource,
  sameSourceArchive,
  sourceChunkBytes,
  sourceFormat,
  SourceInputError,
  type SourceArchive,
} from '@/services/workspaceSourcePayload'
import { packWorkspaceSources } from '@/services/workspaceSourceTar'

type Selection =
  | { kind: 'archive'; file: File; format: SourceArchive['format'] }
  | { kind: 'files' | 'directory'; files: File[] }

const props = defineProps<{ workspaceId: string }>()
const lifecycle = new AbortController()
const current = shallowRef<SourceImport | null>(null)
const selected = shallowRef<Selection | null>(null)
const fileInput = ref<HTMLInputElement>()
const rawInput = ref<HTMLInputElement>()
const directoryInput = ref<HTMLInputElement>()
const directorySupported = 'webkitdirectory' in document.createElement('input')
const busy = ref<'refresh' | 'upload' | 'git' | 'publish' | 'cancel' | 'history' | ''>('')
const error = ref('')
const confirmed = ref(false)
const synchronized = ref(false)
const denied = ref(false)
const hashInProgress = ref(false)
const repositories = shallowRef<string[]>([])
const repository = ref('')
const gitRef = ref('HEAD')
const history = shallowRef<SourceImport[] | null>(null)
const before = ref<string | null>(null)
let transfer: AbortController | undefined
let pending: { id: string; archive: SourceArchive } | undefined
let pendingGit: (GitSourceInput & { id: string }) | undefined
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
    ['Uploading', 'Acquiring', 'Ready', 'Staged'].includes(current.value.state),
)
const canStartGit = computed(
  () => !denied.value && (!current.value || ['Canceled', 'Failed'].includes(current.value.state)),
)
const percentage = computed(() =>
  current.value?.archive.archiveBytes
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
      ['Acquiring', 'Ready', 'Publishing', 'Canceling'].includes(current.value.state)
    )
      void run('refresh', load)
  }, 2500)
})
onUnmounted(() => {
  lifecycle.abort()
  if (timer !== undefined) window.clearInterval(timer)
})

function accept(result: SourceImport | null, signal: AbortSignal) {
  signal.throwIfAborted()
  if (
    current.value &&
    result?.importId === current.value.importId &&
    !sameSourceOrigin(current.value, result)
  )
    throw new SourceInputError('导入来源或已固定的快照与服务器回执不一致。')
  if (
    current.value?.importId !== result?.importId ||
    !sameSourcePlan(current.value?.plan ?? null, result?.plan ?? null) ||
    result?.state !== 'Staged'
  )
    confirmed.value = false
  current.value = result
  if (result && pending?.id === result.importId) pending = undefined
  if (result && pendingGit?.id === result.importId) pendingGit = undefined
  denied.value = false
  synchronized.value = true
}

function clearFile(except?: HTMLInputElement) {
  selected.value = null
  for (const input of [fileInput.value, rawInput.value, directoryInput.value])
    if (input && input !== except) input.value = ''
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
      pendingGit = undefined
      repositories.value = []
      repository.value = ''
      history.value = null
      before.value = null
      clearFile()
    }
    error.value = failure instanceof SourceInputError ? failure.message : workspaceError(failure)
  } finally {
    if (!signal.aborted) busy.value = ''
  }
}

function refresh() {
  void run('refresh', async (signal) => {
    await load(signal)
    repositories.value = []
    const list = await api.gitRepositories(props.workspaceId, signal).catch((failure): string[] => {
      if (failure instanceof ApiError && failure.status === 404) return []
      throw failure
    })
    signal.throwIfAborted()
    repositories.value = list
    if (!list.includes(repository.value)) repository.value = list[0] ?? ''
  })
}

function startGit() {
  if (
    !canStartGit.value ||
    !repositories.value.includes(repository.value) ||
    !validGitRef(gitRef.value)
  )
    return
  const input = { repositoryId: repository.value, ref: gitRef.value }
  void run('git', async (signal) => {
    await load(signal)
    if (!canStartGit.value) return
    if (pendingGit?.repositoryId !== input.repositoryId || pendingGit.ref !== input.ref)
      pendingGit = { ...input, id: createIdempotencyKey() }
    synchronized.value = false
    accept(await api.beginGit(props.workspaceId, pendingGit.id, input, signal), signal)
    history.value = null
    clearFile()
  })
}

function loadHistory(more = false) {
  void run('history', async (signal) => {
    const page = await api.history(props.workspaceId, more ? before.value : null, signal)
    signal.throwIfAborted()
    if (
      more &&
      page.items.some((item) => history.value?.some((seen) => seen.importId === item.importId))
    )
      throw new SourceInputError('导入历史返回了重复记录。请刷新历史。')
    history.value = more ? [...(history.value ?? []), ...page.items] : page.items
    before.value = page.nextBefore
  })
}

function selectFile(event: Event, kind: Selection['kind']) {
  const input = event.target as HTMLInputElement
  const files = Array.from(input.files ?? [])
  clearFile(input)
  error.value = ''
  const file = files[0]
  if (!file) return
  try {
    selected.value =
      kind === 'archive' ? { kind, file, format: sourceFormat(file) } : { kind, files }
  } catch (failure) {
    clearFile()
    error.value = failure instanceof SourceInputError ? failure.message : workspaceError(failure)
  }
}

function upload() {
  const input = selected.value
  if (!input || !canUpload.value) return
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
      const payload =
        input.kind === 'archive'
          ? input.file
          : packWorkspaceSources(input.files, input.kind === 'directory')
      const archive = await identifySource(
        payload,
        input.kind === 'archive' ? input.format : 'tar',
        uploadSignal,
      )
      hashInProgress.value = false
      let result = current.value
      if (result?.state === 'Uploading') {
        if (!sameSourceArchive(result.archive, archive))
          throw new SourceInputError(
            '所选文件与待续传归档的格式、大小或摘要不一致。请重新选择原内容。',
          )
      } else {
        if (!pending || !sameSourceArchive(pending.archive, archive))
          pending = { id: createIdempotencyKey(), archive }
        synchronized.value = false
        result = await api.begin(props.workspaceId, pending.id, archive, uploadSignal)
        accept(result, uploadSignal)
      }
      while (result.state === 'Uploading') {
        const offset = result.receivedBytes
        const body = payload.slice(offset, offset + sourceChunkBytes)
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
    history.value = null
  })
}

function cancel() {
  const result = current.value
  if (!result || !canCancel.value) return
  void run('cancel', async (signal) => {
    confirmed.value = false
    synchronized.value = false
    accept(await api.cancel(props.workspaceId, result, signal), signal)
    history.value = null
    clearFile()
  })
}
</script>

<template>
  <el-card class="source-card" shadow="never">
    <template #header><h2>导入源码</h2></template>
    <div class="source-content">
      <p>可从授权的 Git 仓库获取源码，或选择文件、目录与归档。暂存后核对计划再发布。</p>
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
          <template v-if="current.git">
            <dt>Git 仓库</dt>
            <dd>{{ current.git.repositoryId }}</dd>
            <dt>请求 ref</dt>
            <dd>
              <code>{{ current.git.ref }}</code>
            </dd>
            <template v-if="current.git.snapshot">
              <dt>固定提交</dt>
              <dd>
                <code>{{ current.git.snapshot.commit }}</code>
              </dd>
              <template v-if="current.git.snapshot.resolvedObject !== current.git.snapshot.commit">
                <dt>标签对象</dt>
                <dd>
                  <code>{{ current.git.snapshot.resolvedObject }}</code>
                </dd>
              </template>
            </template>
          </template>
          <template v-if="current.archive.archiveBytes">
            <dt>归档</dt>
            <dd>{{ current.archive.format }} · {{ byteLabel(current.archive.archiveBytes) }}</dd>
            <dt>归档摘要</dt>
            <dd>
              <code>{{ current.archive.archiveSha256 }}</code>
            </dd>
          </template>
          <template v-if="['Uploading', 'Acquiring', 'Ready', 'Staged'].includes(current.state)">
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
        <p v-if="current.state === 'Acquiring' && !current.git?.snapshot" role="status">
          正在解析 ref 并校验 Git 快照。
        </p>
        <template
          v-if="
            current.state === 'Uploading' ||
            busy === 'upload' ||
            (current.state === 'Acquiring' && current.archive.archiveBytes)
          "
        >
          <p id="workspace-source-progress">
            已接收 {{ byteLabel(current.receivedBytes) }} /
            {{ byteLabel(current.archive.archiveBytes) }}
          </p>
          <el-progress
            :percentage="percentage"
            :aria-label="`${current.git ? '获取' : '上传'}进度 ${percentage}%`"
          />
        </template>
        <p v-if="current.state === 'Published'">源码已发布到该空间，可前往运行记录执行开发任务。</p>
      </template>
      <form v-if="canStartGit && repositories.length" class="source-git" @submit.prevent="startGit">
        <div class="source-upload">
          <label for="workspace-git-repository">Git 仓库</label>
          <el-select id="workspace-git-repository" v-model="repository" :disabled="!!busy">
            <el-option v-for="name in repositories" :key="name" :label="name" :value="name" />
          </el-select>
        </div>
        <div class="source-upload">
          <label for="workspace-git-ref">Git ref</label>
          <el-input
            id="workspace-git-ref"
            v-model.trim="gitRef"
            maxlength="255"
            :disabled="!!busy"
            aria-describedby="workspace-git-hint"
          />
          <small id="workspace-git-hint"
            >例如 HEAD、refs/heads/main、refs/tags/v1 或完整提交 ID。</small
          >
        </div>
        <div class="source-actions">
          <el-button
            native-type="submit"
            type="primary"
            :loading="busy === 'git'"
            :disabled="!!busy || !synchronized || !validGitRef(gitRef)"
          >
            获取 Git 源码
          </el-button>
        </div>
      </form>
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
          @change="selectFile($event, 'archive')"
        />
        <div
          v-if="!current || current.state !== 'Uploading' || current.archive.format === 'tar'"
          class="source-file-pickers"
        >
          <div class="source-upload">
            <label for="workspace-source-raw">选择源码文件</label>
            <input
              id="workspace-source-raw"
              ref="rawInput"
              type="file"
              multiple
              :disabled="!!busy"
              @change="selectFile($event, 'files')"
            />
          </div>
          <div v-if="directorySupported" class="source-upload">
            <label for="workspace-source-directory">选择源码目录</label>
            <input
              id="workspace-source-directory"
              ref="directoryInput"
              type="file"
              webkitdirectory
              aria-describedby="workspace-source-directory-hint"
              :disabled="!!busy"
              @change="selectFile($event, 'directory')"
            />
            <small id="workspace-source-directory-hint">
              保留所选目录内的相对路径。空目录请通过归档导入。
            </small>
          </div>
        </div>
        <p v-if="selected && selected.kind !== 'archive'" role="status">
          已选 {{ selected.files.length }} 个文件。文件和目录会封装为 tar 后上传。
        </p>
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
        <el-button
          v-if="!denied"
          :disabled="!!busy"
          :loading="busy === 'history'"
          @click="loadHistory()"
        >
          {{ history ? '刷新导入历史' : '查看导入历史' }}
        </el-button>
      </div>
      <section v-if="history" class="source-history" aria-labelledby="workspace-source-history">
        <h3 id="workspace-source-history">导入历史</h3>
        <p v-if="!history.length">暂无已结束的导入。</p>
        <ul v-else>
          <li v-for="entry in history" :key="entry.importId">
            <el-tag effect="plain">{{ sourceStates[entry.state] }}</el-tag>
            <span v-if="entry.git"
              >{{ entry.git.repositoryId }} · <code>{{ entry.git.ref }}</code></span
            >
            <span v-else
              >{{ entry.archive.format }} · {{ byteLabel(entry.archive.archiveBytes) }}</span
            >
            <code v-if="entry.git?.snapshot">{{ entry.git.snapshot.commit }}</code>
            <small
              ><code>{{ entry.importId }}</code></small
            >
            <time v-if="entry.finishedAtUtc" :datetime="entry.finishedAtUtc">{{
              new Date(entry.finishedAtUtc).toLocaleString()
            }}</time>
          </li>
        </ul>
        <el-button v-if="before" :disabled="!!busy" @click="loadHistory(true)"
          >加载更多历史</el-button
        >
      </section>
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
.source-file-pickers {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 250px), 1fr));
  gap: 12px;
}
.source-content dl {
  display: grid;
  grid-template-columns: minmax(4.5rem, max-content) minmax(0, 1fr);
  gap: 8px 12px;
}
.source-content dt {
  color: var(--el-text-color-secondary);
}
.source-upload small {
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
.source-git,
.source-history {
  display: grid;
  gap: 12px;
  min-width: 0;
  padding-block: 12px;
  border-block-start: 1px solid var(--el-border-color-light);
}
.source-git :deep(.el-select),
.source-git :deep(.el-input) {
  width: 100%;
  max-width: 28rem;
}
.source-history h3,
.source-history ul {
  margin: 0;
}
.source-history ul {
  display: grid;
  gap: 8px;
  padding: 0;
  list-style: none;
}
.source-history li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 12px;
  overflow-wrap: anywhere;
}
.source-history small {
  color: var(--el-text-color-secondary);
}
</style>
