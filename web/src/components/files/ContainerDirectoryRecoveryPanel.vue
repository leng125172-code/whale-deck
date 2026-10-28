<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import {
  assertDirectoryIntent,
  directoryRecoveryBase,
  directoryState,
} from '@/services/containerDirectories'
import type { DirectoryIntent } from '@/services/containerDirectories'

const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const opened = ref(false),
  busy = ref(false),
  error = ref(''),
  completed = ref('')
const rows = ref<DirectoryIntent[]>([]),
  selected = ref<DirectoryIntent | null>(null)
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
  }
}
function failure(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) stop(true)
  error.value = reason instanceof Error ? reason.message : '容器目录异常任务无法核实'
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
  try {
    const values = await apiRequest<DirectoryIntent[]>(directoryRecoveryBase, {
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    if (
      !Array.isArray(values) ||
      values.length > 50 ||
      new Set(values.map((v) => v.id)).size !== values.length
    )
      throw new Error('异常目录任务清单无法核实')
    for (const value of values) {
      assertDirectoryIntent(value)
      if (value.state !== 'Unknown') throw new Error('异常目录任务状态已变化')
    }
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
  if (!allowed.value || busy.value || !selected.value) return
  const original = selected.value
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  try {
    await apiRequest(`${directoryRecoveryBase}/${original.id}/acknowledge`, {
      method: 'POST',
      headers: { 'Idempotency-Key': original.id },
      body: JSON.stringify({ version: original.version, confirmed: true }),
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    const result = await apiRequest<DirectoryIntent>(`${directoryRecoveryBase}/${original.id}`, {
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    assertDirectoryIntent(result, original.command, original.id)
    if (result.state !== 'Acknowledged' || result.version <= original.version)
      throw new Error('处置回执尚未核实，请刷新原任务，不能推断已成功')
    completed.value =
      '已处置历史未知结果并释放保护；不代表目录创建成功，不会重新创建、删除或回滚目录。'
    rows.value = rows.value.filter((v) => v.id !== original.id)
    selected.value = null
  } catch (reason) {
    if (token === generation && !local.signal.aborted) {
      failure(reason)
      error.value += '；请刷新核对原任务，未自动重试确认。'
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
    const result = await apiRequest<DirectoryIntent>(`${directoryRecoveryBase}/${original.id}`, {
      signal: local.signal,
    })
    if (token !== generation || local.signal.aborted) return
    assertDirectoryIntent(result, original.command, original.id)
    if (result.state === 'Acknowledged' && result.version > original.version) {
      completed.value = '已核实原处置回执；不代表目录创建成功，没有再次发送确认。'
      rows.value = rows.value.filter((value) => value.id !== original.id)
      selected.value = null
      error.value = ''
    } else if (result.state === 'Unknown') {
      selected.value = result
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
  <el-button v-if="allowed" @click="opened = true">容器目录异常</el-button>
  <el-dialog
    v-if="allowed"
    v-model="opened"
    title="容器目录异常处置"
    width="min(720px, calc(100vw - 24px))"
    :close-on-click-modal="false"
    destroy-on-close
  >
    <el-alert
      title="仅处置原任务的历史不确定性。确认前会验证根权限操作已被永久阻止再次执行；不重做目录操作，也不删除历史证据。"
      type="warning"
      :closable="false"
    />
    <el-button :loading="busy" @click="load">刷新异常目录任务</el-button>
    <p v-if="!rows.length && !busy">暂无待处置的目录任务</p>
    <div v-for="row in rows" :key="row.id" class="directory-recovery__row">
      <div>
        <strong>{{ directoryState(row.state) }}</strong>
        <p>{{ row.command.location.containerId }}</p>
        <p>/{{ row.command.location.path }}</p>
        <small>{{ row.id }}</small>
      </div>
      <el-button :disabled="busy" @click="selected = row">处置历史结果</el-button>
    </div>
    <el-alert
      v-if="selected"
      :title="`确认处置 /${selected.command.location.path}？原任务将以失败结束；这不表示创建成功。`"
      type="warning"
      :closable="false"
    />
    <div v-if="selected" class="directory-recovery__actions">
      <el-button :disabled="busy" @click="selected = null">暂不处置</el-button
      ><el-button type="warning" :loading="busy" @click="acknowledge">确认处置并释放保护</el-button>
      <el-button v-if="error" :disabled="busy" @click="verifyOriginal">查询原处置</el-button>
    </div>
    <p v-if="completed" role="status">{{ completed }}</p>
    <p v-if="error" role="alert" class="directory-recovery__error">{{ error }}</p>
  </el-dialog>
</template>

<style scoped>
.directory-recovery__row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.directory-recovery__row > div {
  min-width: 0;
  flex: 1 1 240px;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.directory-recovery__row p {
  margin: 4px 0;
}
.directory-recovery__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}
.directory-recovery__error {
  color: var(--el-color-danger);
  overflow-wrap: anywhere;
}
</style>
