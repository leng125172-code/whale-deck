<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { apiRequest } from '@/services/apiClient'
import { parentDirectory, searchSummary, validFileSearch } from '@/services/fileBrowser'
import type { FileSearchHit, FileSearchPage } from '@/services/fileBrowser'
const props = defineProps<{ rootId: string; path: string; disabled: boolean }>()
const emit = defineEmits<{ navigate: [path: string]; failure: [reason: unknown] }>()
const query = ref(''),
  kind = ref('All'),
  matchCase = ref(false),
  busy = ref(false)
const hits = ref<FileSearchHit[]>([]),
  result = ref<FileSearchPage | null>(null),
  scanned = ref(0),
  canceled = ref(false)
const canSearch = computed(() => !props.disabled && !!props.rootId && validFileSearch(query.value))
let controller: AbortController | null = null
function stop(clear = false) {
  controller?.abort()
  controller = null
  busy.value = false
  if (clear) {
    hits.value = []
    result.value = null
    scanned.value = 0
    canceled.value = false
  } else canceled.value = true
}
async function search(more = false) {
  if (
    !canSearch.value ||
    busy.value ||
    (more && (!result.value?.nextCursor || hits.value.length >= 1000))
  )
    return
  if (!more) stop(true)
  const request = new AbortController()
  controller = request
  busy.value = true
  canceled.value = false
  try {
    const page = await apiRequest<FileSearchPage>('files/search', {
      method: 'POST',
      signal: request.signal,
      body: JSON.stringify({
        rootId: props.rootId,
        path: props.path,
        query: query.value.trim(),
        kind: kind.value,
        matchCase: matchCase.value,
        cursor: more ? result.value?.nextCursor : '',
        take: more ? Math.min(50, 1000 - hits.value.length) : 50,
      }),
    })
    if (request !== controller || request.signal.aborted) return
    hits.value = more ? [...hits.value, ...page.entries] : page.entries
    result.value = page
    scanned.value += page.scannedEntries
  } catch (reason) {
    if (request === controller && !request.signal.aborted) {
      hits.value = []
      result.value = null
      scanned.value = 0
      emit('failure', reason)
    }
  } finally {
    if (request === controller) busy.value = false
  }
}
function clearSearch() {
  query.value = ''
  stop(true)
}
watch(
  () => [props.rootId, props.path, props.disabled, query.value, kind.value, matchCase.value],
  () => stop(true),
)
onBeforeUnmount(() => stop(true))
</script>
<template>
  <section class="file-search" aria-label="递归文件名搜索">
    <form class="file-search__form" @submit.prevent="search()">
      <el-input
        v-model="query"
        aria-label="搜索文件名"
        placeholder="搜索当前目录及子目录的名称"
        clearable
        :disabled="disabled"
      />
      <el-select v-model="kind" aria-label="搜索类型" :disabled="disabled"
        ><el-option value="All" label="文件和目录" /><el-option
          value="File"
          label="仅文件" /><el-option value="Directory" label="仅目录"
      /></el-select>
      <el-checkbox v-model="matchCase" :disabled="disabled">区分大小写</el-checkbox>
      <el-button type="primary" native-type="submit" :disabled="!canSearch || busy">搜索</el-button>
      <el-button v-if="busy" @click="stop()">停止搜索</el-button>
      <el-button v-if="result || canceled" @click="clearSearch">清除搜索</el-button>
    </form>
    <p v-if="query && !validFileSearch(query)" class="file-search__hint">
      名称最多 256 UTF-8 字节，不支持路径、控制字符；按字面匹配，不解释通配符。
    </p>
    <div
      v-if="busy || result || canceled"
      class="file-search__results"
      aria-live="polite"
      :aria-busy="busy"
    >
      <p>
        {{
          busy
            ? '正在搜索…'
            : canceled
              ? result?.nextCursor
                ? '搜索已停止；可继续上一次扫描或重新搜索'
                : '搜索已停止，可重新搜索'
              : result
                ? searchSummary(result)
                : ''
        }}
      </p>
      <p>已检查 {{ scanned.toLocaleString() }} 项 · 匹配 {{ hits.length }} 项</p>
      <ul v-if="hits.length">
        <li v-for="hit in hits" :key="hit.path">
          <div>
            <strong :title="hit.entry.name">{{ hit.entry.name }}</strong
            ><small :title="hit.path">{{ hit.path }}</small>
          </div>
          <el-button
            link
            type="primary"
            :disabled="disabled"
            @click="
              emit(
                'navigate',
                hit.entry.kind === 'Directory' ? hit.path : parentDirectory(hit.path),
              )
            "
            >{{ hit.entry.kind === 'Directory' ? '进入目录' : '打开所在目录' }}</el-button
          >
        </li>
      </ul>
      <p v-else-if="result?.complete">没有匹配的名称</p>
      <el-button
        v-if="result?.nextCursor"
        :disabled="busy || disabled || hits.length >= 1000"
        @click="search(true)"
        >继续扫描</el-button
      >
      <p v-if="hits.length >= 1000">已达到页面 1,000 条结果上限，请缩小目录或使用更具体的名称。</p>
    </div>
  </section>
</template>
<style scoped>
.file-search {
  min-width: 0;
}
.file-search__form {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.file-search__form .el-input {
  flex: 1 1 240px;
  min-width: 0;
}
.file-search__form .el-select {
  width: 140px;
  max-width: 100%;
}
.file-search__form .el-button + .el-button {
  margin-left: 0;
}
.file-search__results {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  margin-top: 12px;
  padding: 12px;
}
.file-search__results p,
.file-search__hint {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  overflow-wrap: anywhere;
}
.file-search__results ul {
  list-style: none;
  padding: 0;
  max-height: 380px;
  overflow: auto;
}
.file-search__results li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 8px 0;
}
.file-search__results li div {
  min-width: 0;
}
.file-search__results strong,
.file-search__results small {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-search__results strong {
  font-size: 13px;
}
.file-search__results small {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.file-search__results .el-button {
  flex-shrink: 0;
}
</style>
