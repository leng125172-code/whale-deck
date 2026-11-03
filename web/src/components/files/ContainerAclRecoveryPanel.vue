<script setup lang="ts">
import AclEntryList from '@/components/containers/AclEntryList.vue'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import {
  assertAclIntent,
  assertAclAdvance,
  aclRecoveryBase,
  aclState,
  aclModeLabel,
} from '@/services/containerAcls'
import type { ContainerAclIntent } from '@/services/containerAcls'
import {
  getAclRecoverySource,
  loadAclRecoverySources,
  assertSameAclRecoverySource,
} from '@/services/containerAclRecovery'
import type { ContainerAclRestoreSourceSummary } from '@/services/containerAclRecovery'

const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const opened = ref(false),
  busy = ref(false),
  error = ref(''),
  completed = ref(''),
  uncertain = ref(false)
const rows = ref<ContainerAclIntent[]>([]),
  selected = ref<ContainerAclIntent | null>(null)
const sources = ref<Record<string, ContainerAclRestoreSourceSummary | null>>({})
const selectedSource = computed(() => (selected.value ? sources.value[selected.value.id] : null))
function sourceTime(value: number) {
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}
async function verifySource(row: ContainerAclIntent, signal: AbortSignal) {
  const expected = sources.value[row.id]
  if (expected === undefined) throw new Error('历史ACL恢复来源摘要尚未核实')
  const observed = await getAclRecoverySource(row, signal)
  assertSameAclRecoverySource(expected, observed)
}
let generation = 0,
  controller: AbortController | null = null
function stop(clear = false) {
  generation++
  controller?.abort()
  controller = null
  busy.value = false
  if (clear) {
    opened.value = false
    rows.value = []
    sources.value = {}
    selected.value = null
    error.value = ''
    completed.value = ''
    uncertain.value = false
  }
}
function failure(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) stop(true)
  error.value = reason instanceof Error ? reason.message : '容器ACL修改异常任务无法核实'
}
async function load() {
  if (!allowed.value || busy.value) return
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  selected.value = null
  rows.value = []
  sources.value = {}
  completed.value = ''
  uncertain.value = false
  try {
    const values = await apiRequest<ContainerAclIntent[]>(aclRecoveryBase, {
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    if (!Array.isArray(values) || values.length > 50) throw new Error('异常ACL修改任务清单无法核实')
    for (const value of values) {
      assertAclIntent(value)
      if (value.state !== 'Unknown') throw new Error('异常ACL修改任务状态已变化')
    }
    if (new Set(values.map((value) => value.id)).size !== values.length)
      throw new Error('异常ACL修改任务清单无法核实')
    const summaries = await loadAclRecoverySources(values, local.signal)
    if (token !== generation || local.signal.aborted) return
    sources.value = summaries
    rows.value = values
  } catch (reason) {
    if (token === generation && !local.signal.aborted) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
async function acknowledge() {
  if (!allowed.value || busy.value || uncertain.value || !selected.value) return
  const original = selected.value
  uncertain.value = true
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  try {
    await verifySource(original, local.signal)
    if (token !== generation || local.signal.aborted) return
    await apiRequest(`${aclRecoveryBase}/${original.id}/acknowledge`, {
      method: 'POST',
      headers: { 'Idempotency-Key': original.id },
      body: JSON.stringify({ version: original.version, confirmed: true }),
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    const result = await apiRequest<ContainerAclIntent>(`${aclRecoveryBase}/${original.id}`, {
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    assertAclIntent(result, original.command.location.containerId, original.id, original.command)
    assertAclAdvance(original, result)
    await verifySource(result, local.signal)
    if (token !== generation || local.signal.aborted) return
    if (result.state !== 'Acknowledged' || result.version <= original.version)
      throw new Error('处置回执尚未核实，请刷新原任务，不能推断已成功')
    completed.value =
      '已处置历史未知结果并释放保护；不代表ACL修改成功，不会重复修改或自动恢复ACL，不删除原对象。'
    rows.value = rows.value.filter((v) => v.id !== original.id)
    selected.value = null
    uncertain.value = false
  } catch (reason) {
    if (token === generation && !local.signal.aborted) {
      failure(reason)
      if (token !== generation) return
      error.value += '；请刷新核对原任务，未自动重试处置确认。'
    }
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
async function verifyOriginal() {
  if (!allowed.value || busy.value || !selected.value) return
  const original = selected.value
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  try {
    const result = await apiRequest<ContainerAclIntent>(`${aclRecoveryBase}/${original.id}`, {
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    assertAclIntent(result, original.command.location.containerId, original.id, original.command)
    assertAclAdvance(original, result)
    await verifySource(result, local.signal)
    if (token !== generation || local.signal.aborted) return
    if (result.state === 'Acknowledged' && result.version > original.version) {
      completed.value = '已核实原处置回执；不代表ACL修改成功，没有再次发送确认。'
      rows.value = rows.value.filter((value) => value.id !== original.id)
      selected.value = null
      uncertain.value = false
      error.value = ''
    } else if (result.state === 'Unknown') {
      selected.value = result
      uncertain.value = false
      error.value = '原任务仍为未知；如需处置，请重新检查并明确确认。'
    } else throw new Error('原任务状态已变化，请刷新清单核实')
  } catch (reason) {
    if (token === generation && !local.signal.aborted) failure(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
watch(opened, (value) => {
  if (value) void load()
  else stop(true)
})
watch([allowed, () => authSession.user.value?.subject], () => stop(true))
onBeforeUnmount(() => stop(true))
</script>

<template>
  <el-button v-if="allowed" @click="opened = true">容器ACL修改异常</el-button>
  <el-dialog
    v-if="allowed"
    v-model="opened"
    title="容器ACL修改异常处置"
    width="min(720px, calc(100vw - 24px))"
    class="container-acl-recovery"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <el-alert
      title="仅处置原任务的历史不确定性。确认前会验证根ACL操作已被永久阻止再次执行；不重复修改ACL，也不删除历史证据，也不删除可能存在的HDD元数据。"
      type="warning"
      :closable="false"
    />
    <el-button :loading="busy" @click="load">刷新异常ACL修改任务</el-button>
    <p v-if="!rows.length && !busy && !error">暂无待处置的ACL修改任务</p>
    <div v-for="row in rows" :key="row.id" class="acl-recovery__row">
      <div>
        <strong>{{ aclState(row.state) }}</strong>
        <p>{{ row.command.location.containerId }}</p>
        <p>对象：/{{ row.command.location.path }}</p>
        <section v-if="sources[row.id]" class="acl-recovery__source" aria-label="历史ACL恢复来源">
          <strong>历史ACL恢复来源</strong>
          <p>
            捕获编号：<code>{{ sources[row.id]!.captureId }}</code>
          </p>
          <p>
            {{ sources[row.id]!.changedAclType }} ·
            {{ sources[row.id]!.kind === 'File' ? '文件' : '目录' }} ·
            {{ sourceTime(sources[row.id]!.createdAtUnixMs) }}
          </p>
          <p>
            原模式 {{ aclModeLabel(sources[row.id]!.originalMode) }}；access
            {{ sources[row.id]!.accessEntryCount }} 项，default
            {{ sources[row.id]!.defaultEntryCount }} 项
          </p>
          <p>这是登记时保存的历史来源摘要，不证明当前文件状态或恢复成功。</p>
        </section>
        <p v-else>普通ACL修改（没有历史恢复来源）</p>
        <p>
          原UID/GID：{{ row.originalUid }}:{{ row.originalGid }}，目标{{ row.command.aclType }}模式
          {{ aclModeLabel(row.targetMode) }}。
        </p>
        <AclEntryList :entries="row.originalAccessAcl" label="原access ACL" /><AclEntryList
          :entries="row.originalDefaultAcl"
          label="原default ACL"
        /><AclEntryList :entries="row.command.entries" :label="`目标${row.command.aclType} ACL`" />
        <p>容器执行身份（内核FSUID/FSGID）：{{ row.executorUid }}:{{ row.executorGid }}</p>
        <p>
          ACL变更：{{
            row.aclConfirmed === true
              ? '已确认'
              : row.aclConfirmed === false
                ? '已确认未发生'
                : '尚未确认'
          }}。
        </p>
        <p v-if="row.historyMayExist">
          {{
            row.historyVerified ? '已验证HDD元数据留存' : '可能存在HDD元数据，尚未验证'
          }}；不包含文件内容。原Root回执
          {{ row.receipt?.state ?? '尚未核实' }}；当前对象须另行核实。
        </p>
        <small>{{ row.id }}</small>
      </div>
      <el-button :disabled="busy || uncertain" @click="selected = row">处置历史结果</el-button>
    </div>
    <template #footer>
      <el-alert
        v-if="selected"
        title="确认处置原ACL修改任务？原任务会以失败结束，这不代表ACL修改成功。"
        type="warning"
        :closable="false"
      />
      <p v-if="selected" class="acl-recovery__reference">
        原任务：<code>{{ selected.id }}</code>
        <span v-if="selectedSource"
          ><br />恢复来源：<code>{{ selectedSource.captureId }}</code> ·
          {{ selectedSource.changedAclType }}</span
        >
      </p>
      <div class="acl-recovery__actions">
        <el-button @click="opened = false">关闭</el-button>
        <el-button v-if="selected" :disabled="busy" @click="selected = null">暂不处置</el-button>
        <el-button
          v-if="selected"
          type="warning"
          :loading="busy"
          :disabled="uncertain"
          @click="acknowledge"
          >确认处置并释放保护</el-button
        >
        <el-button v-if="selected && error" :disabled="busy" @click="verifyOriginal"
          >查询原处置</el-button
        >
      </div>
      <p v-if="completed" role="status">{{ completed }}</p>
      <p v-if="uncertain" role="status">处置响应尚未核实，请查询原处置，不会自动重发。</p>
      <p v-if="error" role="alert" class="acl-recovery__error">{{ error }}</p>
    </template>
  </el-dialog>
</template>

<style scoped>
:global(.container-acl-recovery) {
  display: flex;
  flex-direction: column;
  max-height: calc(100dvh - 24px);
  margin-block: 12px;
}
:global(.container-acl-recovery .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
:global(.container-acl-recovery .el-dialog__header),
:global(.container-acl-recovery .el-dialog__footer) {
  flex-shrink: 0;
}
@media (max-width: 600px) {
  :global(.container-acl-recovery) {
    margin-block: 12px;
  }
}
.acl-recovery__row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.acl-recovery__row > div {
  min-width: 0;
  flex: 1 1 240px;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.acl-recovery__row p {
  margin: 4px 0;
}
.acl-recovery__source {
  padding: 8px;
  margin-block: 8px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
}
.acl-recovery__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}
.acl-recovery__reference {
  overflow-wrap: anywhere;
  font-size: 12px;
  text-align: left;
}
.acl-recovery__actions .el-button + .el-button {
  margin-left: 0;
}
.acl-recovery__error {
  color: var(--el-color-danger);
  overflow-wrap: anywhere;
}
</style>
