<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ApiError } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import type { ContainerFileTarget } from '@/services/containerFiles'
import { readContainerText, prepareContainerText } from '@/services/containerText'
import type { ContainerTextSnapshot, PreparedContainerText } from '@/services/containerText'
import { filePath } from '@/services/fileManagement'
import type { FileEntry } from '@/services/fileManagement'
import { normalizedText, textBytes, textEligible } from '@/services/fileText'
import type { TextLineEnding } from '@/services/fileText'
import { formatBytes } from '@/services/displayUnits'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{
  locked: [value: boolean]
  prepared: [value: PreparedContainerText]
  failure: [reason: unknown]
}>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const visible = ref(false),
  loading = ref(false),
  snapshot = ref<ContainerTextSnapshot | null>(null)
const name = ref(''),
  text = ref(''),
  bom = ref(false),
  ending = ref<TextLineEnding>('LF'),
  error = ref('')
const discardVisible = ref(false)
let request: AbortController | null = null,
  generation = 0
let discard: ((confirmed: boolean) => void) | null = null
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
    !!snapshot.value &&
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
function settleDiscard(value: boolean) {
  const complete = discard
  discard = null
  discardVisible.value = false
  complete?.(value)
}
function clear() {
  generation++
  request?.abort()
  request = null
  settleDiscard(false)
  visible.value = false
  loading.value = false
  snapshot.value = null
  name.value = ''
  text.value = ''
  bom.value = false
  ending.value = 'LF'
  error.value = ''
  emit('locked', false)
}
async function close(done?: () => void) {
  const token = generation
  if (dirty.value) {
    settleDiscard(false)
    discardVisible.value = true
    const approved = await new Promise<boolean>((resolve) => {
      discard = resolve
    })
    if (!approved || token !== generation) return
  }
  clear()
  done?.()
}
async function open(entry: FileEntry) {
  if (!allowed.value || props.disabled || !textEligible(entry)) return
  clear()
  const token = generation
  const local = new AbortController()
  request = local
  name.value = entry.name
  visible.value = true
  loading.value = true
  emit('locked', true)
  try {
    const result = await readContainerText(
      { ...props.target },
      filePath(props.path, entry.name),
      { ...entry },
      local.signal,
    )
    if (local.signal.aborted || token !== generation || !allowed.value) return
    snapshot.value = result
    text.value = normalizedText(result.text)
    bom.value = result.utf8Bom
    ending.value = originalEnding.value
  } catch (reason) {
    if (local.signal.aborted || token !== generation) return
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
      clear()
      emit('failure', reason)
      return
    }
    error.value = reason instanceof Error ? reason.message : '容器文本读取失败，请重新打开'
  } finally {
    if (token === generation) {
      loading.value = false
      request = null
    }
  }
}
function restore(value: PreparedContainerText) {
  if (
    !allowed.value ||
    props.disabled ||
    value.location.containerId !== props.target.containerId ||
    value.location.instanceId !== props.target.instanceId ||
    value.location.path !== filePath(props.path, value.file.name) ||
    value.draft.snapshot.version !== value.expectedVersion
  )
    return
  const draft = value.draft
  try {
    prepareContainerText(draft.snapshot, draft.text, draft.ending, draft.bom)
  } catch {
    return
  }
  clear()
  name.value = value.file.name
  snapshot.value = draft.snapshot
  text.value = draft.text
  ending.value = draft.ending
  bom.value = draft.bom
  visible.value = true
  emit('locked', true)
}
function save() {
  if (
    !snapshot.value ||
    !dirty.value ||
    props.disabled ||
    loading.value ||
    !!encoded.value.error ||
    !allowed.value
  )
    return
  try {
    const prepared = prepareContainerText(snapshot.value, text.value, ending.value, bom.value)
    clear()
    emit('prepared', prepared)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '文本尚不能保存'
  }
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
  () => props.disabled,
  (value) => {
    if (value && visible.value) clear()
  },
)
onBeforeUnmount(clear)
defineExpose({ open, restore })
</script>

<template>
  <el-drawer
    v-model="visible"
    :title="`编辑容器文本 · ${name}`"
    size="min(900px, 100vw)"
    class="container-text-editor"
    :before-close="close"
    :close-on-click-modal="false"
    append-to-body
  >
    <template #header="{ titleId, titleClass }"
      ><span :id="titleId" :class="titleClass" class="container-text-editor__title" :title="name"
        >编辑容器文本 · {{ name }}</span
      ></template
    >
    <div v-loading="loading" class="container-text-editor__body">
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <template v-if="snapshot">
        <el-alert
          title="保存前需再次确认，覆盖前会留存旧版本。若文件已被其他程序修改，本次保存会被拒绝。"
          type="info"
          :closable="false"
        />
        <el-alert
          v-if="snapshot.lineEnding === 'Mixed'"
          title="原文件含混合换行；修改正文后请明确选择统一格式。"
          type="warning"
          :closable="false"
        />
        <div class="container-text-editor__formats">
          <el-select v-model="ending" aria-label="容器文本换行格式">
            <el-option v-if="snapshot.lineEnding === 'Mixed'" label="保留混合换行" value="Keep" />
            <el-option label="LF" value="LF" /><el-option label="CRLF" value="CRLF" /><el-option
              label="CR"
              value="CR"
            />
          </el-select>
          <el-checkbox v-model="bom" aria-label="容器文本 UTF-8 BOM">UTF-8 BOM</el-checkbox>
          <span>{{ dirty ? '未保存' : '未修改' }}</span>
        </div>
        <el-input
          v-model="text"
          type="textarea"
          :rows="18"
          resize="none"
          wrap="off"
          spellcheck="false"
          aria-label="容器文件文本内容"
          class="container-text-editor__input"
        />
        <el-alert v-if="encoded.error" :title="encoded.error" type="warning" :closable="false" />
      </template>
    </div>
    <template #footer
      ><div class="container-text-editor__footer">
        <span role="status">{{
          snapshot
            ? encoded.error
              ? '内容暂不可保存'
              : `${formatBytes(encoded.size)} / 1 MiB`
            : loading
              ? '读取并校验中'
              : '未加载文本'
        }}</span>
        <el-button @click="close()">取消</el-button>
        <el-button
          type="primary"
          :disabled="!snapshot || !dirty || loading || !!encoded.error || disabled"
          @click="save"
          >校验并保存</el-button
        >
      </div></template
    >
  </el-drawer>
  <el-dialog
    v-model="discardVisible"
    title="放弃容器文本编辑？"
    width="min(480px, calc(100vw - 32px))"
    append-to-body
    :close-on-click-modal="false"
    @closed="settleDiscard(false)"
  >
    <p>未保存的编辑内容将被丢弃。</p>
    <template #footer
      ><el-button @click="settleDiscard(false)">继续编辑</el-button
      ><el-button type="primary" @click="settleDiscard(true)">放弃编辑</el-button></template
    >
  </el-dialog>
</template>

<style scoped>
.container-text-editor__body {
  display: grid;
  gap: 14px;
  min-width: 0;
  min-height: 120px;
}
.container-text-editor__title {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.container-text-editor__body :deep(.el-alert__title) {
  color: var(--el-text-color-regular);
  line-height: 1.7;
}
.container-text-editor__formats,
.container-text-editor__footer {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  min-width: 0;
}
.container-text-editor__formats .el-select {
  width: 150px;
  max-width: 100%;
}
.container-text-editor__formats > span,
.container-text-editor__footer > span {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.container-text-editor__footer > span {
  flex: 1;
}
.container-text-editor__footer .el-button + .el-button {
  margin-left: 0;
}
.container-text-editor__input :deep(textarea) {
  font-family: inherit;
  font-size: 13px;
  line-height: 1.7;
  white-space: pre;
  tab-size: 2;
  min-height: 240px;
}
@media (max-width: 600px) {
  .container-text-editor__footer > span {
    flex-basis: 100%;
  }
}
</style>
