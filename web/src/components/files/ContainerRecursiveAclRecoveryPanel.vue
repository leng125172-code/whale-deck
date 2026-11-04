<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import ContainerRecursiveAclFacts from '@/components/containers/ContainerRecursiveAclFacts.vue'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import {
  assertRecursiveAclIntent,
  assertRecursiveAclAdvance,
  recursiveAclRecoveryBase,
  recursiveAclState,
  recursiveAclUnresolved,
} from '@/services/containerRecursiveAcls'
import type { ContainerRecursiveAclIntent } from '@/services/containerRecursiveAcls'

const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const opened = ref(false),
  busy = ref(false),
  error = ref(''),
  completed = ref(''),
  uncertain = ref(false)
const rows = ref<ContainerRecursiveAclIntent[]>([]),
  selected = ref<ContainerRecursiveAclIntent | null>(null)
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
    selected.value = null
    error.value = ''
    completed.value = ''
    uncertain.value = false
  }
}
function failure(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) stop(true)
  error.value = reason instanceof Error ? reason.message : '容器递归ACL修改异常任务无法核实'
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
  uncertain.value = false
  try {
    const values = await apiRequest<ContainerRecursiveAclIntent[]>(recursiveAclRecoveryBase, {
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    if (!Array.isArray(values) || values.length > 50)
      throw new Error('异常递归ACL修改任务清单无法核实')
    for (const value of values) {
      assertRecursiveAclIntent(value)
      if (!recursiveAclUnresolved(value.state)) throw new Error('异常递归ACL修改任务状态已变化')
    }
    if (new Set(values.map((value) => value.id)).size !== values.length)
      throw new Error('异常递归ACL修改任务清单无法核实')
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
    await apiRequest(`${recursiveAclRecoveryBase}/${original.id}/acknowledge`, {
      method: 'POST',
      headers: { 'Idempotency-Key': original.id },
      body: JSON.stringify({ version: original.version, confirmed: true }),
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    const result = await apiRequest<ContainerRecursiveAclIntent>(
      `${recursiveAclRecoveryBase}/${original.id}`,
      {
        signal: local.signal,
      },
    )
    if (token !== generation || local.signal.aborted) return
    assertRecursiveAclIntent(
      result,
      original.command.location.containerId,
      original.id,
      original.command,
    )
    assertRecursiveAclAdvance(original, result)
    if (result.state !== 'Acknowledged' || result.version <= original.version)
      throw new Error('处置回执尚未核实，请刷新原任务，不能推断已成功')
    completed.value =
      '已处置部分修改或未知历史结果并释放保护；不代表整树修改成功，不会续做或自动恢复ACL，不删除原对象。'
    rows.value = rows.value.filter((v) => v.id !== original.id)
    selected.value = null
    uncertain.value = false
  } catch (reason) {
    if (token === generation && !local.signal.aborted) {
      failure(reason)
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
    const result = await apiRequest<ContainerRecursiveAclIntent>(
      `${recursiveAclRecoveryBase}/${original.id}`,
      {
        signal: local.signal,
      },
    )
    if (token !== generation || local.signal.aborted) return
    assertRecursiveAclIntent(
      result,
      original.command.location.containerId,
      original.id,
      original.command,
    )
    assertRecursiveAclAdvance(original, result)
    if (result.state === 'Acknowledged' && result.version > original.version) {
      completed.value = '已核实原处置回执；不代表整树修改成功，没有再次发送确认。'
      rows.value = rows.value.filter((value) => value.id !== original.id)
      selected.value = null
      uncertain.value = false
      error.value = ''
    } else if (recursiveAclUnresolved(result.state)) {
      selected.value = result
      uncertain.value = false
      error.value = '原任务仍为部分修改或未知；如需处置，请重新检查实际计数并明确确认。'
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
  <el-button v-if="allowed" @click="opened = true">容器递归ACL修改异常</el-button>
  <el-dialog
    v-if="allowed"
    v-model="opened"
    title="容器递归ACL修改异常处置"
    width="min(720px, calc(100vw - 24px))"
    class="container-recursive-acl-recovery"
    align-center
    :close-on-click-modal="false"
    destroy-on-close
  >
    <el-alert
      title="仅处置原任务的部分修改或未知历史。确认前会验证原Root递归ACL操作已被永久阻止再次执行；不重复修改ACL，也不删除历史证据，也不删除可能存在的HDD元数据。"
      type="warning"
      :closable="false"
    />
    <el-button :loading="busy" @click="load">刷新异常递归ACL修改任务</el-button>
    <p v-if="!rows.length && !busy && !error">暂无待处置的递归ACL修改任务</p>
    <div v-for="row in rows" :key="row.id" class="recursive-acl-recovery__row">
      <div>
        <strong>{{ recursiveAclState(row.state) }}</strong>
        <p>{{ row.command.location.containerId }}</p>
        <p>对象：/{{ row.command.location.path }}</p>
        <ContainerRecursiveAclFacts :row="row" />
        <small>{{ row.id }}</small>
      </div>
      <el-button :disabled="busy || uncertain" @click="selected = row">处置历史结果</el-button>
    </div>
    <template #footer>
      <el-alert
        v-if="selected"
        title="确认处置原递归ACL修改任务？原任务以失败结束，实际计数和HDD证明保留，不代表整树成功。"
        type="warning"
        :closable="false"
      />
      <p v-if="selected" class="recursive-acl-recovery__reference">
        原任务：<code>{{ selected.id }}</code>
      </p>
      <div class="recursive-acl-recovery__actions">
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
      <p v-if="error" role="alert" class="recursive-acl-recovery__error">{{ error }}</p>
    </template>
  </el-dialog>
</template>

<style scoped>
:global(.container-recursive-acl-recovery) {
  display: flex;
  flex-direction: column;
  max-height: calc(100dvh - 24px);
}
:global(.container-recursive-acl-recovery .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
:global(.container-recursive-acl-recovery .el-dialog__header),
:global(.container-recursive-acl-recovery .el-dialog__footer) {
  flex-shrink: 0;
}
@media (max-width: 600px) {
  :global(.container-recursive-acl-recovery) {
    margin-block: 12px;
  }
}
.recursive-acl-recovery__row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.recursive-acl-recovery__row > div {
  min-width: 0;
  flex: 1 1 240px;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.recursive-acl-recovery__row p {
  margin: 4px 0;
}
.recursive-acl-recovery__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}
.recursive-acl-recovery__reference {
  overflow-wrap: anywhere;
  font-size: 12px;
  text-align: left;
}
.recursive-acl-recovery__actions .el-button + .el-button {
  margin-left: 0;
}
.recursive-acl-recovery__error {
  color: var(--el-color-danger);
  overflow-wrap: anywhere;
}
</style>
