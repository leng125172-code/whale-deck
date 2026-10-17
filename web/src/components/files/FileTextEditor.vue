<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import { filePath, fileQuery } from '@/services/fileManagement'
import type { FileEntry } from '@/services/fileManagement'
import {
  normalizedText,
  prepareText,
  textBytes,
  textEligible,
  validateTextSnapshot,
} from '@/services/fileText'
import type { FileTextSnapshot, PreparedFileText, TextLineEnding } from '@/services/fileText'

const props = defineProps<{ rootId: string; path: string; disabled: boolean }>()
const emit = defineEmits<{
  locked: [value: boolean]
  prepared: [value: PreparedFileText]
  failure: [reason: unknown]
}>()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const visible = ref(false),
  loading = ref(false),
  snapshot = ref<FileTextSnapshot | null>(null),
  entry = ref<FileEntry | null>(null)
const text = ref(''),
  ending = ref<TextLineEnding>('LF'),
  bom = ref(false),
  error = ref('')
let request: AbortController | null = null
const originalEnding = computed<TextLineEnding>(() =>
  snapshot.value?.lineEnding === 'Mixed'
    ? 'Keep'
    : snapshot.value?.lineEnding === 'CRLF'
      ? 'CRLF'
      : snapshot.value?.lineEnding === 'CR'
        ? 'CR'
        : 'LF',
)
const dirty = computed(
  () =>
    snapshot.value !== null &&
    (text.value !== normalizedText(snapshot.value.text) ||
      bom.value !== snapshot.value.utf8Bom ||
      ending.value !== originalEnding.value),
)
const encoded = computed(() => {
  if (!snapshot.value) return { size: 0, error: '' }
  try {
    return {
      size: textBytes(snapshot.value, text.value, ending.value, bom.value).byteLength,
      error: '',
    }
  } catch (reason) {
    return { size: 0, error: reason instanceof Error ? reason.message : '文本无法编码' }
  }
})
function clear() {
  request?.abort()
  request = null
  visible.value = false
  loading.value = false
  snapshot.value = null
  entry.value = null
  text.value = ''
  bom.value = false
  ending.value = 'LF'
  error.value = ''
  emit('locked', false)
}
async function close(done?: () => void) {
  if (dirty.value) {
    try {
      await ElMessageBox.confirm('未保存的编辑内容将被丢弃。', '放弃编辑？', {
        type: 'warning',
        confirmButtonText: '放弃编辑',
        cancelButtonText: '继续编辑',
      })
    } catch {
      return
    }
  }
  clear()
  done?.()
}
async function open(value: FileEntry) {
  if (!authorized.value || props.disabled || !textEligible(value)) return
  clear()
  entry.value = value
  visible.value = true
  loading.value = true
  emit('locked', true)
  request = new AbortController()
  const current = request
  const root = props.rootId,
    path = filePath(props.path, value.name)
  try {
    const result = await apiRequest<FileTextSnapshot>(
      `files/text?${fileQuery(root, path, { expectedVersion: value.version })}`,
      { signal: current.signal },
    )
    if (request !== current || current.signal.aborted || !authorized.value) return
    snapshot.value = validateTextSnapshot(result, root, path, value.version)
    text.value = normalizedText(result.text)
    bom.value = result.utf8Bom
    ending.value = originalEnding.value
  } catch (reason) {
    if (current.signal.aborted || request !== current) return
    snapshot.value = null
    text.value = ''
    error.value =
      reason instanceof ApiError && reason.status === 400
        ? '文件不是可编辑的 UTF-8 文本，或超过 1 MiB；请下载后在本地编辑。'
        : reason instanceof ApiError && reason.status === 409
          ? '文件版本已变化，请关闭后重新打开。'
          : reason instanceof Error
            ? reason.message
            : '读取文本失败'
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
      clear()
      emit('failure', reason)
    }
  } finally {
    if (request === current) loading.value = false
  }
}
function save() {
  if (!snapshot.value || !dirty.value || encoded.value.error || props.disabled || !authorized.value)
    return
  try {
    const prepared = prepareText(snapshot.value, text.value, ending.value, bom.value)
    clear()
    emit('prepared', prepared)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '准备保存失败'
  }
}
watch(() => [props.rootId, props.path, authorized.value], clear)
watch(
  () => props.disabled,
  (value) => {
    if (value && visible.value) clear()
  },
)
onBeforeUnmount(clear)
defineExpose({ open })
</script>

<template>
  <el-drawer
    v-model="visible"
    :title="`编辑 · ${entry?.name || '文本文件'}`"
    size="min(900px, 100vw)"
    class="file-text-editor"
    :before-close="close"
    :close-on-click-modal="false"
  >
    <template #header="{ titleId, titleClass }">
      <span :id="titleId" :class="titleClass" :title="`编辑 · ${entry?.name || '文本文件'}`"
        >编辑 · {{ entry?.name || '文本文件' }}</span
      >
    </template>
    <div class="file-text-editor__body" v-loading="loading">
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <template v-if="snapshot">
        <el-alert
          title="保存将复用版本校验、二次确认和任务发布；覆盖前必须完成 HDD 备份。"
          type="info"
          :closable="false"
        />
        <el-alert
          v-if="snapshot.lineEnding === 'Mixed'"
          title="原文件含混合换行；修改正文后必须明确选择统一格式，不会静默转换。"
          type="warning"
          :closable="false"
        />
        <div class="file-text-editor__formats">
          <el-select v-model="ending" aria-label="文本换行格式">
            <el-option v-if="snapshot.lineEnding === 'Mixed'" label="保留混合换行" value="Keep" />
            <el-option label="LF" value="LF" /><el-option label="CRLF" value="CRLF" /><el-option
              label="CR"
              value="CR"
            />
          </el-select>
          <el-checkbox v-model="bom" aria-label="UTF-8 BOM">UTF-8 BOM</el-checkbox>
          <span>{{ dirty ? '未保存' : '未修改' }}</span>
        </div>
        <el-input
          v-model="text"
          type="textarea"
          :rows="18"
          resize="none"
          wrap="off"
          aria-label="文件文本内容"
          class="file-text-editor__input"
          spellcheck="false"
        />
        <el-alert v-if="encoded.error" :title="encoded.error" type="warning" :closable="false" />
      </template>
    </div>
    <template #footer>
      <div class="file-text-editor__footer">
        <span role="status">{{
          snapshot
            ? encoded.error
              ? '内容暂不可保存'
              : `${formatBytes(encoded.size)} / 1 MiB`
            : loading
              ? '正在读取并校验'
              : '未加载文本'
        }}</span>
        <el-button @click="close()">取消</el-button>
        <el-button
          type="primary"
          :disabled="!snapshot || !dirty || loading || !!encoded.error || disabled"
          @click="save"
          >校验并保存</el-button
        >
      </div>
    </template>
  </el-drawer>
</template>

<style>
.file-text-editor .el-drawer__title {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-text-editor__body {
  display: grid;
  gap: 14px;
  min-width: 0;
  min-height: 120px;
}
.file-text-editor__formats {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}
.file-text-editor__formats .el-select {
  width: 150px;
  max-width: 100%;
}
.file-text-editor__formats > span {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.file-text-editor__input textarea {
  font-family: inherit;
  font-size: 13px;
  line-height: 1.7;
  white-space: pre;
  tab-size: 2;
  min-height: 240px;
}
.file-text-editor__footer {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
}
.file-text-editor__footer > span {
  flex: 1;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.file-text-editor__footer .el-button + .el-button {
  margin-left: 0;
}
@media (max-width: 600px) {
  .file-text-editor__footer > span {
    flex-basis: 100%;
    text-align: left;
  }
}
</style>
