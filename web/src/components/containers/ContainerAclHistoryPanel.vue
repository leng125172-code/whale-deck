<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { authSession } from '@/services/authSession'
import { ApiError } from '@/services/apiClient'
import { filePath } from '@/services/fileManagement'
import type { FileEntry } from '@/services/fileManagement'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  aclBits,
  aclEntryLabel,
  aclModeLabel,
  canAclContainerEntry,
} from '@/services/containerAcls'
import type { ContainerAclEntry } from '@/services/containerAcls'
import { listContainerAclHistory, readContainerAclHistory } from '@/services/containerAclHistory'
import type {
  ContainerAclHistoryEntry,
  ContainerAclHistorySnapshot,
} from '@/services/containerAclHistory'

const props = defineProps<{
  target: ContainerFileTarget
  path: string
  disabled: boolean
  restoreDisabled?: boolean
}>()
const emit = defineEmits<{
  restore: [value: ContainerAclHistorySnapshot, currentVersion: string]
}>()
const sourceVersion = ref('')
const sourceCanRestore = ref(false)
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const opened = ref(false),
  busy = ref(false),
  error = ref(''),
  scope = ref('')
const rows = ref<ContainerAclHistoryEntry[]>([]),
  cursor = ref(''),
  version = ref('')
const loaded = ref(false),
  scanned = ref(0),
  invalid = ref(0)
const snapshot = ref<ContainerAclHistorySnapshot | null>(null)
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
  sourceVersion.value = ''
  sourceCanRestore.value = false
  rows.value = []
  cursor.value = ''
  version.value = ''
  snapshot.value = null
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
            : 'ACL历史无法核实，请核对后重新读取。'
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
  // Once inventory changes, only a fresh request may start another sequence.
  if (!more) {
    rows.value = []
    cursor.value = ''
    version.value = ''
    loaded.value = false
    scanned.value = 0
    invalid.value = 0
  }
  try {
    const page = await listContainerAclHistory(
      props.target,
      scope.value,
      more ? cursor.value : '',
      more ? version.value : '',
      local.signal,
    )
    if (token !== generation || local.signal.aborted) return
    const next = more ? [...rows.value, ...page.entries] : page.entries
    if (next.length > 1000 || new Set(next.map((v) => v.captureId)).size !== next.length)
      throw new Error('history limit')
    rows.value = next
    cursor.value = page.nextAfter
    version.value = page.version
    loaded.value = true
    scanned.value += page.scannedCount
    invalid.value += page.invalidCount
  } catch (reason) {
    if (token === generation && !local.signal.aborted) {
      rows.value = []
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
async function select(entry: ContainerAclHistoryEntry) {
  if (!allowed.value || !opened.value || busy.value) return
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  snapshot.value = null
  try {
    const value = await readContainerAclHistory(props.target, scope.value, entry, local.signal)
    if (token !== generation || local.signal.aborted) return
    snapshot.value = value
  } catch (reason) {
    if (token === generation && !local.signal.aborted) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
async function open(entry: FileEntry) {
  if (
    !allowed.value ||
    props.disabled ||
    entry.isProtected ||
    !['File', 'Directory'].includes(entry.kind)
  )
    return
  clear()
  sourceCanRestore.value = canAclContainerEntry(entry)
  sourceVersion.value = entry.version
  scope.value = filePath(props.path, entry.name)
  opened.value = true
  await load()
}
function restore() {
  if (
    !allowed.value ||
    busy.value ||
    props.restoreDisabled ||
    !snapshot.value ||
    !sourceVersion.value
  )
    return
  emit('restore', JSON.parse(JSON.stringify(snapshot.value)), sourceVersion.value)
  clear()
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
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}
watch(
  [
    () => props.target.containerId,
    () => props.target.instanceId,
    () => props.path,
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
    title="ACL历史"
    width="920px"
    class="container-acl-history-dialog"
    append-to-body
    :close-on-click-modal="false"
    @update:model-value="clear"
  >
    <p class="acl-history__scope" aria-label="ACL历史路径">{{ scope }}</p>
    <el-alert
      type="info"
      :closable="false"
      title="显示修改前留存的原ACL；历史快照不代表当前权限，查看不会恢复或修改对象。"
    />
    <p v-if="error" role="alert" class="acl-history__error">{{ error }}</p>
    <p v-if="busy" role="status">正在从HDD核验ACL历史…</p>
    <template v-if="snapshot">
      <section aria-label="ACL历史快照" class="acl-history__snapshot">
        <h3>原ACL快照 · {{ snapshot.entry.changedAclType }}</h3>
        <dl>
          <dt>捕获标识</dt>
          <dd>{{ snapshot.entry.captureId }}</dd>
          <dt>元数据摘要</dt>
          <dd>{{ snapshot.entry.historyDigest }}</dd>
          <dt>留存时间</dt>
          <dd>{{ date(snapshot.entry.createdAtUnixMs) }}</dd>
          <dt>原启动实例</dt>
          <dd>{{ snapshot.entry.sourceInstanceId }}</dd>
          <dt>原普通模式</dt>
          <dd>{{ aclModeLabel(snapshot.entry.originalMode) }}</dd>
        </dl>
        <section
          v-for="kind in ['access', 'default'] as const"
          :key="kind"
          :aria-label="`历史${kind} ACL`"
          class="acl-history__acl"
        >
          <h4>{{ kind === 'access' ? 'access 访问ACL' : 'default 默认继承ACL' }}</h4>
          <p v-if="!snapshot[kind === 'access' ? 'accessAcl' : 'defaultAcl'].length">
            空：未设置默认继承ACL。
          </p>
          <ul v-else>
            <li
              v-for="entry in snapshot[kind === 'access' ? 'accessAcl' : 'defaultAcl']"
              :key="`${entry.tag}:${entry.id}`"
            >
              <span>{{ aclEntryLabel(entry) }}</span>
              <span
                >权限 {{ entry.permissions }} · {{ aclBits(entry.permissions) }}；有效
                {{
                  effective(entry, snapshot[kind === 'access' ? 'accessAcl' : 'defaultAcl'])
                }}</span
              >
            </li>
          </ul>
        </section>
      </section>
    </template>
    <template v-else>
      <p v-if="loaded && !rows.length && !cursor" role="status">此路径暂无可核实的ACL历史。</p>
      <ul v-if="rows.length" class="acl-history__records" aria-label="ACL历史记录">
        <li v-for="entry in rows" :key="entry.captureId">
          <div>
            <strong>{{ entry.changedAclType === 'access' ? '访问ACL' : '默认继承ACL' }}</strong> ·
            {{ date(entry.createdAtUnixMs) }} · {{ aclModeLabel(entry.originalMode) }}
          </div>
          <span class="acl-history__capture">{{ entry.captureId }}</span>
          <span
            >access {{ entry.accessEntryCount }} 项 · default {{ entry.defaultEntryCount }} 项</span
          >
          <el-button
            :disabled="busy"
            :aria-label="`查看原ACL ${entry.captureId}`"
            @click="select(entry)"
            >查看原ACL</el-button
          >
        </li>
      </ul>
      <p v-if="loaded" class="acl-history__note">
        本次累计扫描 {{ scanned }} 份库存元数据，{{ invalid }} 份无法核实；只展示此路径的有效记录。
      </p>
      <p v-if="cursor" role="status">仍有库存需要扫描；当前列表尚未完整。</p>
    </template>
    <template #footer>
      <el-button @click="clear">{{ busy ? '取消读取并关闭' : '关闭' }}</el-button>
      <el-button v-if="snapshot" :disabled="busy" @click="snapshot = null">返回历史列表</el-button>
      <el-button
        v-if="snapshot"
        type="primary"
        :disabled="busy || restoreDisabled || !sourceCanRestore"
        @click="restore"
        >从此历史生成恢复预检</el-button
      >
      <el-button :disabled="busy" @click="load()">重新读取</el-button>
      <el-button
        v-if="!snapshot && cursor"
        :disabled="busy || rows.length >= 1000"
        @click="load(true)"
        >继续扫描</el-button
      >
    </template>
  </el-dialog>
</template>

<style scoped>
:global(.container-acl-history-dialog) {
  max-width: calc(100vw - 24px);
  max-height: calc(100dvh - 24px);
  margin: 12px auto !important;
  display: flex;
  flex-direction: column;
}
:global(.container-acl-history-dialog .el-dialog__body) {
  min-height: 0;
  overflow: auto;
}
:global(.container-acl-history-dialog .el-dialog__header),
:global(.container-acl-history-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
:global(.container-acl-history-dialog .el-dialog__footer) {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
:global(.container-acl-history-dialog .el-dialog__footer .el-button) {
  margin: 0;
}
.acl-history__scope,
.acl-history__capture,
dd {
  overflow-wrap: anywhere;
  word-break: break-word;
}
.acl-history__scope {
  margin-top: 0;
}
.acl-history__records,
.acl-history__acl ul {
  list-style: none;
  margin: 12px 0;
  padding: 0;
}
.acl-history__records > li {
  display: grid;
  gap: 6px;
  padding: 12px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  margin-bottom: 8px;
}
.acl-history__records .el-button {
  justify-self: start;
}
.acl-history__capture,
.acl-history__note {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.acl-history__snapshot dl {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 6px 12px;
}
dd {
  margin: 0;
}
.acl-history__acl li {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 4px 12px;
  padding: 8px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.acl-history__error {
  color: var(--el-color-danger);
  overflow-wrap: anywhere;
}
@media (max-width: 600px) {
  .acl-history__snapshot dl {
    grid-template-columns: minmax(0, 1fr);
  }
  dt {
    color: var(--el-text-color-secondary);
  }
}
</style>
