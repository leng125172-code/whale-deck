<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { Clock } from '@element-plus/icons-vue'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import { assertCapturePage } from '@/services/fileTreeRecyclings'
import type { TreeCapturePage, TreeCapture } from '@/services/fileTreeRecyclings'
import type { FileRoot } from '@/services/fileManagement'
import { validTreeLocation } from '@/services/fileTreeCopies'
import { canRestoreCapture, canReceiveRestoring } from '@/services/fileTreeRestorings'

const props = defineProps<{ roots: FileRoot[]; rootId: string; disabled?: boolean }>()
const emit = defineEmits<{ failure: [reason: unknown]; restore: [capture: TreeCapture] }>()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const available = computed(() =>
  props.roots.filter((root) =>
    ['files-list', 'files-download', 'files-history'].every((action) =>
      root.actions.includes(action),
    ),
  ),
)
const dialog = ref(false),
  busy = ref(false),
  error = ref(''),
  selectedRoot = ref(''),
  path = ref('')
const items = ref<TreeCapture[]>([]),
  page = ref(0),
  next = ref<string | null>(null)
let cursors: (string | null)[] = [null],
  generation = 0,
  controller: AbortController | undefined
function reset() {
  generation++
  controller?.abort()
  controller = undefined
  items.value = []
  error.value = ''
  busy.value = false
  next.value = null
  page.value = 0
  cursors = [null]
}
function validScope() {
  return (
    available.value.some((root) => root.id === selectedRoot.value) &&
    (path.value === '' || validTreeLocation({ rootId: selectedRoot.value, path: path.value }))
  )
}
async function load(index = 0) {
  if (!authorized.value || !dialog.value || busy.value || !validScope()) return
  const token = ++generation,
    scope = { rootId: selectedRoot.value, path: path.value }
  const after = cursors[index] ?? null
  busy.value = true
  error.value = ''
  items.value = []
  next.value = null
  controller = new AbortController()
  try {
    const query = new URLSearchParams({ rootId: scope.rootId, path: scope.path, take: '25' })
    if (after) query.set('after', after)
    const result = await apiRequest<TreeCapturePage>(`files/tree-recyclings/captures?${query}`, {
      signal: controller.signal,
    })
    assertCapturePage(result, scope.rootId, scope.path)
    if (result.next && result.next === after)
      throw new Error('分页游标未推进；保留当前范围后重新查询')
    if (
      token !== generation ||
      !authorized.value ||
      scope.rootId !== selectedRoot.value ||
      scope.path !== path.value
    )
      return
    items.value = result.items
    next.value = result.next
    page.value = index
  } catch (reason) {
    if (token !== generation || !authorized.value) return
    error.value = reason instanceof Error ? reason.message : '无法读取目录捕获记录'
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
      reset()
      dialog.value = false
      emit('failure', reason)
    }
  } finally {
    if (token === generation) busy.value = false
  }
}
function open() {
  if (!authorized.value || !available.value.length) return
  reset()
  selectedRoot.value = available.value.some((root) => root.id === props.rootId)
    ? props.rootId
    : available.value[0]!.id
  path.value = ''
  dialog.value = true
  void load()
}
function search() {
  if (busy.value) return
  reset()
  if (!validScope()) {
    error.value = '请选择登记根，或输入合法的完整相对路径；留空查看整根记录'
    return
  }
  void load()
}
function nextPage() {
  if (!next.value || busy.value) return
  cursors[page.value + 1] = next.value
  void load(page.value + 1)
}
function previousPage() {
  if (page.value > 0 && !busy.value) void load(page.value - 1)
}
function restoreDisabledReason(capture: TreeCapture) {
  if (
    !canRestoreCapture(
      props.roots.find((root) => root.id === capture.source.source.rootId),
      capture,
    )
  )
    return '此登记根缺少独立目录恢复授权，或捕获证明无效'
  if (!props.roots.some(canReceiveRestoring)) return '没有具备目录恢复写入授权的目标根'
  if (props.disabled || busy.value) return '请先核实当前文件任务并关闭其引用，再发起新的恢复'
  return ''
}
function restore(capture: TreeCapture) {
  if (!authorized.value || restoreDisabledReason(capture)) return
  // Close the catalogue before opening the next dialog. Vue proxies are never
  // persisted; only the selected verified public capture is handed to the form.
  emit('restore', capture)
  dialog.value = false
}
watch([() => authSession.user.value?.subject, authorized], () => {
  reset()
  dialog.value = false
})
watch(
  [selectedRoot, path],
  () => {
    reset()
  },
  { flush: 'sync' },
)
watch(dialog, (value) => {
  if (!value) reset()
})
onBeforeUnmount(reset)
</script>

<template>
  <el-button v-if="authorized" :disabled="!available.length" @click="open"
    ><el-icon><Clock /></el-icon>目录回收记录</el-button
  >
  <el-dialog
    v-if="authorized"
    v-model="dialog"
    title="目录回收与 HDD 捕获记录"
    width="min(800px, calc(100vw - 24px))"
    align-center
    :close-on-click-modal="false"
    class="tree-capture-dialog"
  >
    <el-alert
      title="这里展示曾完整验证的目录归档，不等于源目录已移除或当前磁盘仍健康。恢复前会重新验证归档，生成计划后仍需确认；彻底清理尚未接入。"
      type="info"
      :closable="false"
      show-icon
    />
    <el-form class="tree-capture-filter" label-position="top" @submit.prevent="search">
      <el-form-item label="登记根"
        ><el-select v-model="selectedRoot" aria-label="捕获记录登记根" :disabled="busy"
          ><el-option
            v-for="root in available"
            :key="root.id"
            :value="root.id"
            :label="root.name" /></el-select
      ></el-form-item>
      <el-form-item label="完整相对路径（留空查看整根）"
        ><el-input
          v-model="path"
          :disabled="busy"
          :maxlength="4096"
          clearable
          aria-label="捕获记录相对路径"
      /></el-form-item>
      <el-button type="primary" :loading="busy" native-type="submit">查询记录</el-button>
    </el-form>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div v-if="busy" role="status">正在查询目录捕获记录…</div>
    <el-empty
      v-else-if="!items.length && !error"
      description="此范围暂无已验证的目录捕获记录"
      :image-size="64"
    />
    <section
      v-for="item in items"
      :key="item.id"
      class="tree-capture-item"
      :aria-label="`捕获记录 ${item.source.source.path}`"
    >
      <header>
        <strong>{{ item.source.source.path }}</strong
        ><el-tag size="small" type="success">归档曾验证</el-tag>
      </header>
      <p>
        {{ item.entryCount }} 项 · 内容 {{ formatBytes(item.totalBytes) }} · 归档
        {{ formatBytes(item.archiveBytes) }} · {{ new Date(item.createdAtUtc).toLocaleString() }}
      </p>
      <details>
        <summary>查看捕获证明</summary>
        <dl>
          <dt>捕获 ID</dt>
          <dd>{{ item.id }}</dd>
          <dt>原计划</dt>
          <dd>{{ item.intentId }}</dd>
          <dt>源版本</dt>
          <dd>{{ item.source.expectedVersion }}</dd>
          <dt>清单摘要</dt>
          <dd>{{ item.manifestSha256 }}</dd>
          <dt>归档 SHA256</dt>
          <dd>{{ item.archiveSha256 }}</dd>
        </dl>
      </details>
      <div class="tree-capture-restore">
        <span v-if="restoreDisabledReason(item)">{{ restoreDisabledReason(item) }}</span>
        <el-button
          type="primary"
          plain
          :disabled="!!restoreDisabledReason(item)"
          @click="restore(item)"
          >恢复目录</el-button
        >
      </div>
    </section>
    <template #footer
      ><div class="tree-capture-footer">
        <span>第 {{ page + 1 }} 页 · 每页最多 25 项</span>
        <div>
          <el-button :disabled="busy || page === 0" @click="previousPage">上一页</el-button
          ><el-button :disabled="busy || !next" @click="nextPage">下一页</el-button
          ><el-button @click="dialog = false">关闭</el-button>
        </div>
      </div></template
    >
  </el-dialog>
</template>

<style scoped>
.tree-capture-filter {
  display: grid;
  grid-template-columns: minmax(120px, 1fr) minmax(0, 2fr) auto;
  align-items: end;
  gap: 12px;
  margin: 16px 0;
}
.tree-capture-filter :deep(.el-form-item) {
  min-width: 0;
  margin-bottom: 0;
}
.tree-capture-filter :deep(.el-select) {
  width: 100%;
}
.tree-capture-item {
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  padding: 12px;
  margin-top: 12px;
  overflow-wrap: anywhere;
}
.tree-capture-item header {
  display: flex;
  align-items: start;
  flex-wrap: wrap;
  gap: 8px;
}
.tree-capture-item strong {
  min-width: 0;
  flex: 1;
  font-size: 14px;
}
.tree-capture-item p {
  margin: 8px 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.7;
}
.tree-capture-item summary {
  cursor: pointer;
  color: var(--el-color-primary);
  font-size: 13px;
}
.tree-capture-item dl {
  display: grid;
  grid-template-columns: 80px minmax(0, 1fr);
  gap: 8px;
  font-size: 12px;
}
.tree-capture-item dt {
  color: var(--el-text-color-secondary);
}
.tree-capture-item dd {
  margin: 0;
}
.tree-capture-restore {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}
.tree-capture-restore span {
  flex: 1;
  min-width: 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.tree-capture-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
}
.tree-capture-footer > div {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.tree-capture-footer :deep(.el-button + .el-button) {
  margin-left: 0;
}
:global(.tree-capture-dialog .el-dialog__body) {
  max-height: calc(100dvh - 200px);
  overflow-y: auto;
}
@media (max-width: 600px) {
  .tree-capture-filter {
    grid-template-columns: minmax(0, 1fr);
  }
  .tree-capture-item dl {
    grid-template-columns: 64px minmax(0, 1fr);
  }
}
</style>
