<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { authSession } from '@/services/authSession'
import { ApiError } from '@/services/apiClient'
import { readContainerRecursiveAclHistory } from '@/services/containerRecursiveAclHistory'
import type {
  ContainerRecursiveAclHistorySummary,
  ContainerRecursiveAclHistorySnapshot,
} from '@/services/containerRecursiveAclHistory'
import type { ContainerUploadLocation } from '@/services/containerUploads'
import AclEntryList from './AclEntryList.vue'
const props = defineProps<{
  location: ContainerUploadLocation
  summary: ContainerRecursiveAclHistorySummary
}>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const page = ref<ContainerRecursiveAclHistorySnapshot | null>(null),
  previous = ref<number[]>([]),
  busy = ref(false),
  error = ref('')
let generation = 0,
  controller: AbortController | null = null
function clear() {
  generation++
  controller?.abort()
  controller = null
  busy.value = false
  page.value = null
  previous.value = []
  error.value = ''
}
async function load(offset = 0, stack: number[] = []) {
  if (!allowed.value || busy.value) return
  const loc = { ...props.location },
    summary = { ...props.summary }
  clear()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  try {
    const value = await readContainerRecursiveAclHistory(
      loc,
      loc.path,
      summary,
      offset,
      local.signal,
    )
    if (token !== generation || local.signal.aborted || !allowed.value) return
    page.value = value
    previous.value = stack
  } catch (reason) {
    if (token === generation && !local.signal.aborted) {
      page.value = null
      error.value =
        reason instanceof ApiError && [401, 403].includes(reason.status)
          ? '当前登录无权读取恢复来源。'
          : '恢复来源无法核实；历史可能已变化，请查询原任务，不自动换来源。'
    }
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
function next() {
  if (page.value?.nextOffset !== null && page.value)
    void load(page.value.nextOffset, [...previous.value, page.value.offset])
}
function back() {
  const stack = [...previous.value],
    offset = stack.pop()
  if (offset !== undefined) void load(offset, stack)
}
watch(
  [
    () => props.location.containerId,
    () => props.location.instanceId,
    () => props.location.path,
    () => props.summary,
    allowed,
    () => authSession.user.value?.subject,
  ],
  clear,
  { flush: 'sync' },
)
onBeforeUnmount(clear)
</script>
<template>
  <section v-if="allowed" class="restore-source" aria-label="逐项恢复前像">
    <el-button :loading="busy" @click="load()">查看逐项恢复目标ACL</el-button>
    <p>
      每页最多25个对象；仅恢复来源原修改的{{ summary.changedAclType }}，不把另一类历史ACL作为目标。
    </p>
    <template v-if="page">
      <p role="status">
        前像 {{ page.offset + 1 }}–{{ page.offset + page.entries.length }} /
        {{ summary.entryCount }}；{{ page.complete ? '已到最后一页' : '后续对象尚未展示' }}
      </p>
      <article
        v-for="entry in page.entries"
        :key="entry.relativePath"
        class="restore-source__entry"
      >
        <strong
          >{{ entry.relativePath || '（根目录）' }} ·
          {{ entry.kind === 'Directory' ? '目录' : '文件' }}</strong
        >
        <AclEntryList
          v-if="summary.changedAclType === 'access' || entry.kind === 'Directory'"
          :entries="summary.changedAclType === 'access' ? entry.accessAcl : entry.defaultAcl"
          :label="`恢复目标 ${summary.changedAclType} ACL`"
        />
        <p v-else>文件不受default恢复影响。</p>
      </article>
      <div class="restore-source__actions">
        <el-button v-if="previous.length" :disabled="busy" @click="back">上一页恢复前像</el-button
        ><el-button v-if="!page.complete" :disabled="busy" @click="next">下一页恢复前像</el-button>
      </div>
    </template>
    <p v-if="error" role="alert">{{ error }}</p>
  </section>
</template>
<style scoped>
.restore-source {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.restore-source__entry {
  padding-block: 10px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.restore-source__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.restore-source__actions .el-button {
  margin: 0;
}
[role='alert'] {
  color: var(--el-color-danger);
}
</style>
