<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { apiRequest } from '@/services/apiClient'
import { formatBytes } from '@/services/displayUnits'
import { parentDirectory } from '@/services/fileBrowser'
import {
  contentFileLimit,
  contentSearchSummary,
  validContentQuery,
  validateContentPage,
} from '@/services/fileContentSearch'
import type { FileContentHit, FileContentPage } from '@/services/fileContentSearch'

const props = defineProps<{ rootId: string; path: string; disabled: boolean }>()
const emit = defineEmits<{ navigate: [path: string]; failure: [reason: unknown] }>()
const query = ref(''),
  matchCase = ref(false),
  maxFileBytes = ref(contentFileLimit),
  busy = ref(false),
  canceled = ref(false)
const hits = ref<FileContentHit[]>([]),
  result = ref<FileContentPage | null>(null)
const totals = ref({ files: 0, bytes: 0, binary: 0, oversized: 0, excluded: 0 })
const canSearch = computed(
  () => !props.disabled && !!props.rootId && validContentQuery(query.value),
)
let controller: AbortController | null = null
function stop(clear = false) {
  controller?.abort()
  controller = null
  busy.value = false
  if (clear) {
    hits.value = []
    result.value = null
    canceled.value = false
    totals.value = { files: 0, bytes: 0, binary: 0, oversized: 0, excluded: 0 }
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
    const take = Math.min(50, 1000 - hits.value.length)
    const raw = await apiRequest<unknown>('files/content-search', {
      method: 'POST',
      signal: request.signal,
      body: JSON.stringify({
        rootId: props.rootId,
        path: props.path,
        query: query.value,
        matchCase: matchCase.value,
        maxFileBytes: maxFileBytes.value,
        cursor: more ? result.value?.nextCursor : '',
        take,
      }),
    })
    if (request !== controller || request.signal.aborted) return
    const page = validateContentPage(raw, props.path, take, maxFileBytes.value)
    if (
      more &&
      page.entries.some((row) => hits.value.some((previous) => previous.path === row.path))
    )
      throw new Error('搜索结果发生变化，请重新搜索')
    hits.value = more ? [...hits.value, ...page.entries] : page.entries
    result.value = page
    totals.value.files += page.scannedFiles
    totals.value.bytes += page.readBytes
    totals.value.binary += page.skippedNonTextFiles
    totals.value.oversized += page.skippedOversizedFiles
    totals.value.excluded += page.excludedEntries
  } catch (reason) {
    if (request === controller && !request.signal.aborted) {
      stop(true)
      emit('failure', reason)
    }
  } finally {
    if (request === controller) busy.value = false
  }
}
function clear() {
  query.value = ''
  stop(true)
}
watch(
  () => [
    props.rootId,
    props.path,
    props.disabled,
    query.value,
    matchCase.value,
    maxFileBytes.value,
  ],
  () => stop(true),
)
onBeforeUnmount(() => stop(true))
</script>

<template>
  <section class="content-search" aria-label="文件正文搜索">
    <form class="content-search__form" @submit.prevent="search()">
      <el-input
        v-model="query"
        aria-label="搜索正文"
        placeholder="字面搜索UTF-8正文，不执行正则"
        clearable
        :disabled="disabled"
      />
      <el-select v-model="maxFileBytes" aria-label="单文件搜索上限" :disabled="disabled">
        <el-option
          v-for="size in [1, 4, 16]"
          :key="size"
          :value="size * 1024 * 1024"
          :label="`单文件 ≤ ${size} MiB`"
        />
      </el-select>
      <el-checkbox v-model="matchCase" :disabled="disabled">区分大小写</el-checkbox>
      <el-button type="primary" native-type="submit" :disabled="!canSearch || busy"
        >搜索正文</el-button
      >
      <el-button v-if="busy" @click="stop()">停止搜索</el-button>
      <el-button v-if="result || canceled" @click="clear">清除搜索</el-button>
    </form>
    <p class="content-search__hint">
      每个文件仅显示首个匹配行。跳过二进制、非UTF-8、过大文件、受保护路径及链接/嵌套挂载；按页限时读取，不生成全文索引。
    </p>
    <p v-if="query && !validContentQuery(query)" class="content-search__hint">
      正文关键词最多256 UTF-8字节，不支持控制字符；斜线、反斜线及正则符号均按字面匹配。
    </p>
    <div
      v-if="busy || result || canceled"
      class="content-search__results"
      aria-live="polite"
      :aria-busy="busy"
    >
      <p>
        {{
          busy
            ? '正在搜索正文…'
            : canceled
              ? '搜索已停止；迟到结果不会追加，可重新搜索或继续已返回的游标'
              : result
                ? contentSearchSummary(result)
                : ''
        }}
      </p>
      <p>
        已读取 {{ totals.files.toLocaleString() }} 个文件 · {{ formatBytes(totals.bytes) }} · 匹配
        {{ hits.length }} 个文件
      </p>
      <p>
        跳过：非文本 {{ totals.binary }} · 过大 {{ totals.oversized }} · 已枚举不可访问项
        {{ totals.excluded }}；不枚举受保护子树
      </p>
      <ul v-if="hits.length">
        <li v-for="hit in hits" :key="hit.path">
          <div class="content-search__row">
            <strong :title="hit.path">{{ hit.path }}</strong>
            <el-button
              link
              type="primary"
              :disabled="disabled"
              @click="emit('navigate', parentDirectory(hit.path))"
              >打开所在目录</el-button
            >
          </div>
          <small :title="hit.entry.version"
            >首个匹配：第 {{ hit.lineNumber }} 行 · {{ formatBytes(hit.entry.sizeBytes) }} ·
            文件版本 {{ hit.entry.version.slice(0, 12) }}</small
          >
          <pre><code>{{ hit.excerptTruncated ? '… ' : '' }}{{ hit.excerpt }}{{ hit.excerptTruncated ? ' …' : '' }}</code></pre>
        </li>
      </ul>
      <p v-else-if="result?.complete">已扫描的可读UTF-8文本中没有匹配；跳过项未搜索。</p>
      <el-button
        v-if="result?.nextCursor"
        :disabled="busy || disabled || hits.length >= 1000"
        @click="search(true)"
        >继续扫描</el-button
      >
      <p v-if="hits.length >= 1000">页面已达到1,000个文件上限，请缩小目录或关键词。</p>
    </div>
  </section>
</template>

<style scoped>
.content-search {
  min-width: 0;
}
.content-search__form,
.content-search__row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}
.content-search__form .el-input {
  flex: 1 1 240px;
  min-width: 0;
}
.content-search__form .el-select {
  width: 174px;
  max-width: 100%;
}
.content-search__form .el-button + .el-button {
  margin-left: 0;
}
.content-search__hint,
.content-search__results p,
small {
  font-size: 12px;
  line-height: 1.7;
  color: var(--el-text-color-secondary);
  overflow-wrap: anywhere;
}
.content-search__results {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  margin-top: 12px;
  padding: 12px;
  min-width: 0;
}
ul {
  list-style: none;
  padding: 0;
  max-height: 420px;
  overflow: auto;
}
li {
  min-width: 0;
  padding: 10px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.content-search__row {
  justify-content: space-between;
}
strong {
  min-width: 0;
  font-size: 13px;
  overflow-wrap: anywhere;
}
pre {
  min-width: 0;
  margin: 8px 0 0;
  padding: 8px;
  border-radius: 6px;
  background: var(--el-fill-color-light);
  color: var(--el-text-color-primary);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  word-break: break-word;
  font-size: 12px;
}
</style>
