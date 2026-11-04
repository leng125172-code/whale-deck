<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { authSession } from '@/services/authSession'
import { ApiError } from '@/services/apiClient'
import { filePath } from '@/services/fileManagement'
import type { FileEntry } from '@/services/fileManagement'
import type { ContainerFileTarget } from '@/services/containerFiles'
import { aclBits, aclEntryLabel, aclModeLabel } from '@/services/containerAcls'
import type { ContainerAclEntry } from '@/services/containerAcls'
import {
  listContainerRecursiveAclHistory,
  readContainerRecursiveAclHistory,
} from '@/services/containerRecursiveAclHistory'
import type {
  ContainerRecursiveAclHistorySummary,
  ContainerRecursiveAclHistorySnapshot,
} from '@/services/containerRecursiveAclHistory'

const props = defineProps<{
  target: ContainerFileTarget
  path: string
  disabled: boolean
  restoreDisabled?: boolean
}>()
const emit = defineEmits<{
  restore: [snapshot: ContainerRecursiveAclHistorySnapshot, version: string]
}>()
const objectVersion = ref('')
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const opened = ref(false),
  busy = ref(false),
  error = ref(''),
  scope = ref('')
const rows = ref<ContainerRecursiveAclHistorySummary[]>([]),
  cursor = ref(''),
  version = ref('')
const inventoryPage = ref(0)
const visibleRows = computed(() =>
  rows.value.slice(inventoryPage.value * 25, (inventoryPage.value + 1) * 25),
)
const loaded = ref(false),
  scanned = ref(0),
  invalid = ref(0)
const snapshot = ref<ContainerRecursiveAclHistorySnapshot | null>(null)
const selected = ref<ContainerRecursiveAclHistorySummary | null>(null)
const previousOffsets = ref<number[]>([])
let generation = 0,
  controller: AbortController | null = null
function stop() {
  generation++
  controller?.abort()
  controller = null
  busy.value = false
}
function clear() {
  stop()
  opened.value = false
  scope.value = ''
  objectVersion.value = ''
  rows.value = []
  inventoryPage.value = 0
  cursor.value = ''
  version.value = ''
  snapshot.value = null
  selected.value = null
  previousOffsets.value = []
  loaded.value = false
  scanned.value = 0
  invalid.value = 0
  error.value = ''
}
function failure(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    clear()
    return
  }
  error.value =
    reason instanceof ApiError && [409, 412].includes(reason.status)
      ? '容器、对象或历史版本已变化，请重新读取。'
      : reason instanceof ApiError && reason.status === 404
        ? '当前容器、对象或历史已不存在，请核对后重新读取。'
        : reason instanceof ApiError && reason.status === 503
          ? 'Agent或HDD暂不可用，请稍后手动重试。'
          : reason instanceof ApiError && reason.status === 429
            ? '读取预算已用尽，请稍后手动重试。'
            : '递归ACL历史无法核实，请核对后重新读取。'
}
async function load(more = false) {
  if (!allowed.value || !opened.value || busy.value || (more && !cursor.value)) return
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  snapshot.value = null
  selected.value = null
  previousOffsets.value = []
  // Once inventory changes, only a fresh request may start another sequence.
  if (!more) {
    rows.value = []
    inventoryPage.value = 0
    cursor.value = ''
    version.value = ''
    loaded.value = false
    scanned.value = 0
    invalid.value = 0
  }
  try {
    const page = await listContainerRecursiveAclHistory(
      props.target,
      scope.value,
      more ? cursor.value : '',
      more ? version.value : '',
      local.signal,
    )
    if (token !== generation || local.signal.aborted) return
    const next = more ? [...rows.value, ...page.entries] : page.entries
    if (next.length > 10000 || new Set(next.map((v) => v.captureId)).size !== next.length)
      throw new Error('history limit')
    rows.value = next
    inventoryPage.value = more ? Math.max(0, Math.ceil(next.length / 25) - 1) : 0
    cursor.value = page.nextAfter
    version.value = page.version
    loaded.value = true
    scanned.value += page.scannedCount
    invalid.value += page.invalidCount
  } catch (reason) {
    if (token === generation && !local.signal.aborted) {
      rows.value = []
      inventoryPage.value = 0
      cursor.value = ''
      version.value = ''
      loaded.value = false
      failure(reason)
    }
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
async function select(
  entry: ContainerRecursiveAclHistorySummary,
  offset = 0,
  previous: number[] = [],
) {
  if (!allowed.value || !opened.value || busy.value) return
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  snapshot.value = null
  selected.value = { ...entry }
  previousOffsets.value = []
  try {
    const value = await readContainerRecursiveAclHistory(
      props.target,
      scope.value,
      entry,
      offset,
      local.signal,
    )
    if (token !== generation || local.signal.aborted) return
    snapshot.value = value
    previousOffsets.value = previous
  } catch (reason) {
    if (token === generation && !local.signal.aborted) {
      selected.value = null
      if (reason instanceof ApiError && [404, 409, 412].includes(reason.status)) {
        rows.value = []
        inventoryPage.value = 0
        cursor.value = ''
        version.value = ''
        loaded.value = false
      }
      failure(reason)
    }
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
function nextEntries() {
  if (selected.value && snapshot.value && snapshot.value.nextOffset !== null)
    void select(selected.value, snapshot.value.nextOffset, [
      ...previousOffsets.value,
      snapshot.value.offset,
    ])
}
function previousEntries() {
  const previous = [...previousOffsets.value],
    offset = previous.pop()
  if (selected.value && offset !== undefined) void select(selected.value, offset, previous)
}
function inventory() {
  stop()
  selected.value = null
  snapshot.value = null
  previousOffsets.value = []
  error.value = ''
}
async function open(entry: FileEntry) {
  if (!allowed.value || props.disabled || entry.isProtected || entry.kind !== 'Directory') return
  clear()
  scope.value = filePath(props.path, entry.name)
  objectVersion.value = entry.version
  opened.value = true
  await load()
}
function restore() {
  if (
    !allowed.value ||
    busy.value ||
    props.restoreDisabled ||
    !snapshot.value ||
    !/^[A-F0-9]{64}$/.test(objectVersion.value)
  )
    return
  const value = snapshot.value,
    version = objectVersion.value
  clear()
  emit('restore', value, version)
}
function effective(entry: ContainerAclEntry, entries: ContainerAclEntry[]) {
  const mask = entries.find((v) => v.tag === 'mask')?.permissions ?? 7
  return aclBits(
    ['user', 'group', 'group_obj'].includes(entry.tag)
      ? entry.permissions & mask
      : entry.permissions,
  )
}
function date(value: number) {
  const parsed = new Date(value)
  return Number.isFinite(parsed.getTime())
    ? parsed.toLocaleString('zh-CN', { hour12: false })
    : `时间值 ${value}`
}
watch(
  [
    () => props.target.containerId,
    () => props.target.instanceId,
    () => props.path,
    () => props.disabled,
    allowed,
    () => authSession.user.value?.subject,
  ],
  clear,
  { flush: 'sync' },
)
function hidden() {
  if (document.hidden) clear()
}
onMounted(() => document.addEventListener('visibilitychange', hidden))
onBeforeUnmount(() => {
  document.removeEventListener('visibilitychange', hidden)
  clear()
})
defineExpose({ open })
</script>

<template>
  <el-dialog
    v-if="allowed"
    :model-value="opened"
    title="递归ACL历史"
    width="920px"
    class="container-recursive-acl-history-dialog"
    append-to-body
    :close-on-click-modal="false"
    @update:model-value="clear"
  >
    <p class="recursive-history__scope" aria-label="递归ACL历史路径">{{ scope }}</p>
    <el-alert
      type="info"
      :closable="false"
      title="显示整份捕获的修改前ACL，不代表当前权限；计数来自完整原计划，不表示实际执行完成或修改数量。"
    />
    <p v-if="error" role="alert" class="recursive-history__error">{{ error }}</p>
    <p v-if="busy" role="status">正在从HDD核验递归ACL历史…</p>
    <section v-if="selected" aria-label="递归ACL历史详情" class="recursive-history__snapshot">
      <h3>原计划与完整前像</h3>
      <dl>
        <dt>捕获标识</dt>
        <dd>{{ selected.captureId }}</dd>
        <dt>元数据摘要</dt>
        <dd>{{ selected.historyDigest }}</dd>
        <dt>清单摘要</dt>
        <dd>{{ selected.manifestDigest }}</dd>
        <dt>留存时间</dt>
        <dd>{{ date(selected.createdAtUnixMs) }}</dd>
        <dt>原启动实例</dt>
        <dd>{{ selected.sourceInstanceId }}</dd>
        <dt>完整原计划</dt>
        <dd>
          {{ selected.entryCount }}项 · 文件{{ selected.fileCount }} · 目录{{
            selected.directoryCount
          }}
          · 原计划变更{{ selected.changeCount }}
        </dd>
      </dl>
      <template v-if="snapshot">
        <p role="status">
          已核验第{{ snapshot.offset + 1 }}–{{ snapshot.offset + snapshot.entries.length }}项，共{{
            selected.entryCount
          }}项{{ snapshot.complete ? '（到达末页）' : '（仍有前像未读取）' }}
        </p>
        <article
          v-for="item in snapshot.entries"
          :key="item.relativePath"
          class="recursive-history__entry"
          :aria-label="`原ACL ${item.relativePath || '根目录'}`"
        >
          <h4>{{ item.relativePath || '根目录' }}</h4>
          <p>
            {{ item.kind === 'File' ? '文件' : '目录' }} · 原模式
            {{ aclModeLabel(item.originalMode) }}
          </p>
          <section
            v-for="kind in ['access', 'default'] as const"
            :key="kind"
            :aria-label="`历史${kind} ACL`"
            class="recursive-history__acl"
          >
            <h5>{{ kind === 'access' ? 'access 访问ACL' : 'default 默认继承ACL' }}</h5>
            <p v-if="!item[kind === 'access' ? 'accessAcl' : 'defaultAcl'].length">
              空：未设置默认继承ACL。
            </p>
            <ul v-else>
              <li
                v-for="acl in item[kind === 'access' ? 'accessAcl' : 'defaultAcl']"
                :key="`${acl.tag}:${acl.id}`"
              >
                <span>{{ aclEntryLabel(acl) }}</span
                ><span
                  >权限 {{ acl.permissions }} · {{ aclBits(acl.permissions) }}；有效
                  {{ effective(acl, item[kind === 'access' ? 'accessAcl' : 'defaultAcl']) }}</span
                >
              </li>
            </ul>
          </section>
        </article>
      </template>
    </section>
    <template v-else>
      <p v-if="loaded && !rows.length && !cursor" role="status">此目录暂无可核实的递归ACL历史。</p>
      <ul v-if="rows.length" class="recursive-history__records" aria-label="递归ACL历史记录">
        <li v-for="entry in visibleRows" :key="entry.captureId">
          <div>
            <strong>{{ entry.changedAclType === 'access' ? '访问ACL' : '默认继承ACL' }}</strong> ·
            {{ date(entry.createdAtUnixMs) }}
          </div>
          <span class="recursive-history__capture">{{ entry.captureId }}</span>
          <span
            >原计划{{ entry.entryCount }}项 · 文件{{ entry.fileCount }} · 目录{{
              entry.directoryCount
            }}
            · 原计划变更{{ entry.changeCount }}</span
          >
          <el-button
            :disabled="busy"
            :aria-label="`查看逐项前像 ${entry.captureId}`"
            @click="select(entry)"
            >查看逐项前像</el-button
          >
        </li>
      </ul>
      <p v-if="loaded" class="recursive-history__note">
        已读取 {{ rows.length }} 条记录，本页 {{ visibleRows.length }} 条。 累计扫描
        {{ scanned }} 份库存元数据，{{ invalid }} 份无法核实；仅展示此目录的有效捕获。
      </p>
      <p v-if="cursor" role="status">仍有库存需要扫描；当前列表尚未完整。</p>
    </template>
    <template #footer>
      <el-button v-if="snapshot" type="warning" :disabled="busy || restoreDisabled" @click="restore"
        >预检恢复此递归ACL历史</el-button
      >
      <el-button @click="clear">{{ busy ? '取消读取并关闭' : '关闭' }}</el-button>
      <el-button v-if="selected" :disabled="busy" @click="inventory">返回历史列表</el-button>
      <el-button v-if="snapshot && previousOffsets.length" :disabled="busy" @click="previousEntries"
        >上一页前像</el-button
      >
      <el-button v-if="snapshot && !snapshot.complete" :disabled="busy" @click="nextEntries"
        >下一页前像</el-button
      >
      <el-button :disabled="busy" @click="load()">重新读取</el-button>
      <el-button v-if="!selected && inventoryPage > 0" :disabled="busy" @click="inventoryPage--"
        >上一页历史</el-button
      >
      <el-button
        v-if="!selected && (inventoryPage + 1) * 25 < rows.length"
        :disabled="busy"
        @click="inventoryPage++"
        >下一页已读历史</el-button
      >
      <el-button
        v-if="!selected && cursor"
        :disabled="busy || rows.length >= 10000"
        @click="load(true)"
        >继续扫描</el-button
      >
    </template>
  </el-dialog>
</template>
<style scoped>
:global(.container-recursive-acl-history-dialog) {
  max-width: calc(100vw - 24px);
  max-height: calc(100dvh - 24px);
  margin: 12px auto !important;
  display: flex;
  flex-direction: column;
}
:global(.container-recursive-acl-history-dialog .el-dialog__body) {
  min-height: 0;
  overflow: auto;
}
:global(.container-recursive-acl-history-dialog .el-dialog__header),
:global(.container-recursive-acl-history-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
:global(.container-recursive-acl-history-dialog .el-dialog__footer) {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
:global(.container-recursive-acl-history-dialog .el-dialog__footer .el-button) {
  margin: 0;
}
.recursive-history__scope,
.recursive-history__capture,
dd {
  overflow-wrap: anywhere;
  word-break: break-word;
}
.recursive-history__scope {
  margin-top: 0;
}
.recursive-history__records,
.recursive-history__acl ul {
  list-style: none;
  margin: 12px 0;
  padding: 0;
}
.recursive-history__records > li {
  display: grid;
  gap: 6px;
  padding: 12px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  margin-bottom: 8px;
}
.recursive-history__records .el-button {
  justify-self: start;
}
.recursive-history__capture,
.recursive-history__note {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.recursive-history__snapshot dl {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 6px 12px;
}
dd {
  margin: 0;
}
.recursive-history__acl li {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 4px 12px;
  padding: 8px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.recursive-history__error {
  color: var(--el-color-danger);
  overflow-wrap: anywhere;
}
@media (max-width: 600px) {
  .recursive-history__snapshot dl {
    grid-template-columns: minmax(0, 1fr);
  }
  dt {
    color: var(--el-text-color-secondary);
  }
}
.recursive-history__entry {
  border-top: 1px solid var(--el-border-color-lighter);
  padding-block: 12px;
  overflow-wrap: anywhere;
}
</style>
