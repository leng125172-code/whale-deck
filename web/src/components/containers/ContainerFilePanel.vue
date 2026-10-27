<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { Document, Folder, Download, RefreshRight } from '@element-plus/icons-vue'
import { ApiError } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import { filePath } from '@/services/fileManagement'
import type { FileEntry } from '@/services/fileManagement'
import ContainerUploadPanel from '@/components/containers/ContainerUploadPanel.vue'
import {
  canDownloadContainerFile,
  downloadContainerFile,
  inspectContainerFileTarget,
  listContainerFiles,
} from '@/services/containerFiles'
import type { ContainerFileTarget } from '@/services/containerFiles'

const props = defineProps<{ containerId: string }>()
const target = ref<ContainerFileTarget | null>(null)
const path = ref(''),
  pathInput = ref(''),
  entries = ref<FileEntry[]>([]),
  cursor = ref(''),
  version = ref('')
const busy = ref(''),
  error = ref(''),
  progress = ref(0),
  completed = ref('')
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const breadcrumbs = computed(() => (path.value ? path.value.split('/') : []))
let request: AbortController | null = null
let generation = 0
const urls = new Set<string>()
function stop(clear = false) {
  generation++
  request?.abort()
  request = null
  busy.value = ''
  if (clear) {
    target.value = null
    path.value = ''
    pathInput.value = ''
    entries.value = []
    cursor.value = ''
    version.value = ''
    error.value = ''
    progress.value = 0
    completed.value = ''
  }
}
function failure(reason: unknown) {
  if (reason instanceof ApiError) {
    if ([401, 403].includes(reason.status)) stop(true)
    if ([409, 412].includes(reason.status)) {
      target.value = null
      entries.value = []
      cursor.value = ''
    }
    error.value =
      (
        {
          401: '登录已失效，请重新登录',
          403: '当前无权读取，或目标受保护',
          404: '目标已不存在，请刷新',
          409: '容器实例或文件已变化，请重新连接',
          412: '容器实例已变化，请重新连接',
          503: 'Agent 暂不可用，请稍后重试',
        } as Record<number, string>
      )[reason.status] ?? reason.message
  } else error.value = reason instanceof Error ? reason.message : '读取失败，请稍后重试'
}
async function connect() {
  if (!allowed.value) return
  stop(true)
  const controller = new AbortController()
  request = controller
  const current = generation
  busy.value = '连接中'
  try {
    const result = await inspectContainerFileTarget(props.containerId, controller.signal)
    if (current !== generation || !allowed.value) return
    if (result.containerId !== props.containerId) throw new Error('容器目标不匹配')
    target.value = result
  } catch (reason) {
    if (!controller.signal.aborted && current === generation) failure(reason)
  } finally {
    if (current === generation) {
      busy.value = ''
      request = null
    }
  }
  if (current === generation && target.value) await load('')
}
async function load(destination = path.value, more = false) {
  if (
    !allowed.value ||
    !target.value ||
    busy.value ||
    (more && (!cursor.value || entries.value.length >= 1000))
  )
    return
  stop()
  const controller = new AbortController()
  request = controller
  const current = generation
  busy.value = '读取中'
  error.value = ''
  completed.value = ''
  const selected = target.value
  try {
    const page = await listContainerFiles(
      selected,
      destination,
      more ? cursor.value : '',
      controller.signal,
    )
    if (current !== generation || !allowed.value) return
    if (more && page.version !== version.value) throw new Error('目录已变化，请刷新后重新加载')
    entries.value = more ? [...entries.value, ...page.entries] : page.entries
    path.value = destination
    pathInput.value = destination
    cursor.value = page.nextCursor
    version.value = page.version
  } catch (reason) {
    if (!controller.signal.aborted && current === generation) failure(reason)
  } finally {
    if (current === generation) {
      busy.value = ''
      request = null
    }
  }
}
async function download(entry: FileEntry) {
  if (!allowed.value || !target.value || busy.value || !canDownloadContainerFile(entry)) return
  stop()
  const controller = new AbortController()
  request = controller
  const current = generation
  busy.value = '下载中'
  progress.value = 0
  error.value = ''
  completed.value = ''
  try {
    const blob = await downloadContainerFile(
      target.value,
      filePath(path.value, entry.name),
      entry,
      controller.signal,
      (value) => {
        if (current === generation) progress.value = Math.min(100, Math.floor(value))
      },
    )
    if (current !== generation || !allowed.value || controller.signal.aborted) return
    const url = URL.createObjectURL(blob)
    urls.add(url)
    const link = document.createElement('a')
    link.href = url
    link.download = entry.name
    link.click()
    completed.value = '文件已完整校验，已发起浏览器保存'
    window.setTimeout(() => {
      URL.revokeObjectURL(url)
      urls.delete(url)
    }, 1000)
  } catch (reason) {
    if (!controller.signal.aborted && current === generation) failure(reason)
  } finally {
    if (current === generation) {
      busy.value = ''
      request = null
    }
  }
}
watch(
  () => [props.containerId, authSession.user.value?.subject, allowed.value],
  () => {
    stop(true)
    if (allowed.value) void connect()
  },
  { immediate: true },
)
onBeforeUnmount(() => {
  stop(true)
  urls.forEach((url) => URL.revokeObjectURL(url))
  urls.clear()
})
</script>

<template>
  <section v-if="allowed" class="container-files" aria-label="容器文件">
    <div class="container-files__heading">
      <h3>容器文件 <el-tag size="small" effect="plain">受控读写</el-tag></h3>
      <div class="container-files__buttons">
        <el-button v-if="target" :icon="RefreshRight" :disabled="!!busy" @click="load()"
          >刷新</el-button
        >
        <el-button v-else :disabled="!!busy" @click="connect">重新连接</el-button>
        <el-button v-if="busy" @click="stop()">取消</el-button>
      </div>
    </div>
    <p class="container-files__note">
      绑定当前运行实例；敏感文件、符号链接和核心共享资源不可读写。上传须先校验、规划并确认。
    </p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div v-if="target" class="container-files__location">
      <nav aria-label="容器文件路径" class="container-files__crumbs">
        <el-button link :disabled="!!busy" @click="load('')">/</el-button>
        <template v-for="(part, index) in breadcrumbs" :key="index">
          <span v-if="index > 0">/</span
          ><el-button
            link
            :title="part"
            :disabled="!!busy"
            @click="load(breadcrumbs.slice(0, index + 1).join('/'))"
            >{{ part }}</el-button
          >
        </template>
      </nav>
      <form class="container-files__goto" @submit.prevent="load(pathInput)">
        <el-input
          v-model="pathInput"
          aria-label="容器内相对目录"
          placeholder="相对目录，留空为根目录"
          :disabled="!!busy"
        />
        <el-button native-type="submit" :disabled="!!busy">前往</el-button>
      </form>
    </div>
    <ContainerUploadPanel
      v-if="target"
      :target="target"
      :path="path"
      :disabled="!!busy"
      @refresh="load(path)"
    />
    <div v-if="busy" class="container-files__status" role="status">
      {{ busy }}<span v-if="busy === '下载中'"> · {{ progress }}%</span>
    </div>
    <div
      v-if="entries.length && target"
      class="container-files__list"
      role="list"
      aria-label="容器文件列表"
    >
      <div v-for="entry in entries" :key="entry.name" class="container-files__row" role="listitem">
        <div class="container-files__name">
          <el-icon><component :is="entry.kind === 'Directory' ? Folder : Document" /></el-icon>
          <el-button
            v-if="entry.kind === 'Directory' && !entry.isProtected"
            link
            :title="entry.name"
            :disabled="!!busy"
            @click="load(filePath(path, entry.name))"
            >{{ entry.name }}</el-button
          >
          <span v-else :title="entry.name">{{ entry.name }}</span>
          <el-tag v-if="entry.isProtected" size="small" type="info">受保护</el-tag>
        </div>
        <span class="container-files__size">{{
          entry.kind === 'File'
            ? formatBytes(entry.sizeBytes)
            : entry.kind === 'Directory'
              ? '目录'
              : entry.kind === 'Symlink'
                ? '链接'
                : '特殊'
        }}</span>
        <el-button
          :icon="Download"
          link
          :disabled="!!busy || !canDownloadContainerFile(entry)"
          :aria-label="`下载 ${entry.name}`"
          @click="download(entry)"
          >下载</el-button
        >
      </div>
    </div>
    <el-empty v-else-if="target && !busy && !error" description="当前目录为空" :image-size="56" />
    <div v-if="cursor && target" class="container-files__footer">
      <el-button :disabled="!!busy || entries.length >= 1000" @click="load(path, true)"
        >加载更多</el-button
      >
      <span
        >已加载 {{ entries.length }} 项{{ entries.length >= 1000 ? '，请缩小目录范围' : '' }}</span
      >
    </div>
    <p v-if="completed" class="container-files__status" role="status">{{ completed }}</p>
  </section>
</template>

<style scoped>
.container-files {
  min-width: 0;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  padding: 14px;
}
.container-files__heading,
.container-files__buttons,
.container-files__goto,
.container-files__footer {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.container-files__heading {
  justify-content: space-between;
  flex-wrap: wrap;
}
.container-files h3 {
  font-size: 14px;
  font-weight: 500;
  margin: 0;
}
.container-files__note,
.container-files__footer,
.container-files__status {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.container-files__location {
  margin: 12px 0;
  min-width: 0;
}
.container-files__crumbs {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 5px;
  min-width: 0;
  margin-bottom: 8px;
}
.container-files__crumbs .el-button {
  min-width: 0;
  max-width: 100%;
  margin: 0;
}
.container-files__crumbs :deep(.el-button > span) {
  overflow: hidden;
  text-overflow: ellipsis;
}
.container-files__goto .el-input {
  min-width: 0;
  flex: 1;
}
.container-files__list {
  max-height: min(420px, 50vh);
  overflow-y: auto;
  overflow-x: hidden;
}
.container-files__row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 85px 64px;
  align-items: center;
  gap: 8px;
  padding: 9px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.container-files__name {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  font-size: 13px;
}
.container-files__name > span,
.container-files__name > .el-button {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.container-files__name :deep(.el-button > span) {
  overflow: hidden;
  text-overflow: ellipsis;
}
.container-files__name .el-icon,
.container-files__name .el-tag {
  flex-shrink: 0;
}
.container-files__size {
  font-size: 12px;
  text-align: right;
  color: var(--el-text-color-secondary);
}
.container-files__footer {
  flex-wrap: wrap;
  margin-top: 12px;
}
@media (max-width: 480px) {
  .container-files {
    padding: 10px;
  }
  .container-files__row {
    grid-template-columns: minmax(0, 1fr) 64px;
  }
  .container-files__size {
    grid-column: 1;
    grid-row: auto;
    text-align: left;
    margin-left: 22px;
  }
  .container-files__row > .el-button {
    grid-column: 2;
    grid-row: 1 / span 2;
  }
}
</style>
