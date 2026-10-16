<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ElTree } from 'element-plus'
import type { RenderContentContext } from 'element-plus'
import { apiRequest } from '@/services/apiClient'
import { filePath, fileQuery } from '@/services/fileManagement'
import type { FilePage } from '@/services/fileManagement'
const props = defineProps<{ rootId: string; name: string; disabled: boolean; revision: number }>()
const emit = defineEmits<{ navigate: [path: string]; failure: [reason: unknown] }>()
interface Branch {
  key: string
  label: string
  path: string
  leaf: boolean
  after?: string
  version?: string
  more?: boolean
}
type LazyNode = RenderContentContext['node']
const tree = ref<InstanceType<typeof ElTree>>(),
  expanded = ref(false),
  epoch = ref(0)
const root = computed<Branch[]>(() =>
  props.rootId ? [{ key: ':', label: props.name, path: '', leaf: false }] : [],
)
const requests = new Set<AbortController>(),
  branches = new Map<string, Branch[]>(),
  paging = new Set<string>()
function stop() {
  for (const request of requests) request.abort()
  requests.clear()
  branches.clear()
  paging.clear()
  epoch.value++
}
async function children(path: string, after = '', version = '') {
  if (requests.size >= 4) throw new Error('目录加载较多，请稍后再展开')
  const request = new AbortController()
  requests.add(request)
  try {
    const page = await apiRequest<FilePage>(
      `files/directories?${fileQuery(props.rootId, path, { after, take: '100' })}`,
      { signal: request.signal },
    )
    if (request.signal.aborted) return null
    if (version && page.version !== version) throw new Error('目录已变化，请刷新目录树后重试')
    const rows: Branch[] = page.entries.map((entry) => ({
      key: ':' + filePath(path, entry.name),
      label: entry.name,
      path: filePath(path, entry.name),
      leaf: false,
    }))
    if (page.nextCursor)
      rows.push({
        key: ':more:' + path,
        label: '加载更多目录',
        path,
        after: page.nextCursor,
        version: page.version,
        more: true,
        leaf: true,
      })
    return rows
  } catch (reason) {
    if (request.signal.aborted) return null
    throw reason
  } finally {
    requests.delete(request)
  }
}
async function load(node: LazyNode, resolve: (rows: Branch[]) => void, reject: () => void) {
  if (node.level === 0) {
    resolve(root.value)
    return
  }
  if (props.disabled) {
    reject()
    return
  }
  try {
    const rows = await children(node.data.path)
    if (rows) {
      if ([...branches.values()].reduce((sum, value) => sum + value.length, 0) + rows.length > 5000)
        throw new Error('目录树已达到 5,000 项上限，请刷新或使用搜索定位')
      branches.set(node.data.key, rows)
      resolve(rows)
    } else reject()
  } catch (reason) {
    reject()
    emit('failure', reason)
  }
}
async function choose(branch: Branch, node: LazyNode) {
  if (props.disabled) return
  if (!branch.more) {
    emit('navigate', branch.path)
    return
  }
  if (paging.has(branch.key)) return
  paging.add(branch.key)
  try {
    const rows = await children(branch.path, branch.after, branch.version)
    if (!rows) return
    const key = node.parent?.data.key
    if (typeof key !== 'string') return
    const merged = [...(branches.get(key) || []).filter((item) => !item.more), ...rows]
    if (merged.length > 2000)
      throw new Error('单个目录树分支最多展示 2,000 项，请使用文件列表分页或缩小搜索范围')
    if (
      [...branches.values()].reduce((sum, value) => sum + value.length, 0) -
        (branches.get(key)?.length || 0) +
        merged.length >
      5000
    )
      throw new Error('目录树已达到 5,000 项上限，请刷新或使用搜索定位')
    branches.set(key, merged)
    tree.value?.updateKeyChildren(key, merged)
  } catch (reason) {
    emit('failure', reason)
  } finally {
    paging.delete(branch.key)
  }
}
watch(() => [props.rootId, props.disabled, props.revision], stop)
onBeforeUnmount(stop)
</script>
<template>
  <aside class="file-directory-tree" aria-label="目录树">
    <el-button class="tree-toggle" :aria-expanded="expanded" @click="expanded = !expanded">{{
      expanded ? '收起目录树' : '展开目录树'
    }}</el-button>
    <div v-if="expanded" class="tree-body">
      <el-button link type="primary" :disabled="disabled" @click="stop">刷新目录树</el-button>
      <el-tree
        v-if="rootId"
        ref="tree"
        :key="`${rootId}:${epoch}`"
        lazy
        :load="load"
        node-key="key"
        :props="{ label: 'label', isLeaf: 'leaf' }"
        :expand-on-click-node="false"
        @node-click="choose"
      >
        <template #default="{ data }"
          ><span class="tree-label" :title="data.path || name">{{ data.label }}</span></template
        >
      </el-tree>
    </div>
  </aside>
</template>
<style scoped>
.file-directory-tree {
  min-width: 0;
}
.tree-toggle {
  width: 100%;
}
.tree-body {
  margin-top: 8px;
  max-height: 440px;
  overflow: auto;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 8px;
}
.tree-label {
  display: block;
  max-width: 170px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
}
</style>
