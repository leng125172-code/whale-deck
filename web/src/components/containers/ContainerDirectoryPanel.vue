<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { FolderAdd } from '@element-plus/icons-vue'
import { RouterLink } from 'vue-router'
import { ApiError } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { filePath } from '@/services/fileManagement'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  assertDirectoryIntent,
  cancelDirectory,
  directoryRegistration,
  directoryState,
  directoryTerminal,
  getDirectory,
  planDirectory,
  resolveDirectory,
  submitDirectory,
} from '@/services/containerDirectories'
import type { DirectoryIntent, DirectoryPlan } from '@/services/containerDirectories'
import {
  directoryHint,
  removeDirectoryHint,
  saveDirectoryHint,
} from '@/services/containerDirectoryHints'
import type { DirectoryHint } from '@/services/containerDirectoryHints'

const props = defineProps<{ target: ContainerFileTarget; path: string; disabled: boolean }>()
const emit = defineEmits<{ locked: [value: boolean]; refresh: [] }>()
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  name = ref(''),
  mode = ref('0750'),
  busy = ref(false),
  error = ref('')
const plan = ref<DirectoryPlan | null>(null),
  hint = ref<DirectoryHint | null>(null),
  intent = ref<DirectoryIntent | null>(null)
const active = computed(() => !!intent.value && !directoryTerminal(intent.value.state))
let controller: AbortController | null = null,
  timer: number | null = null,
  generation = 0,
  refreshed = ''
function stop() {
  generation++
  controller?.abort()
  controller = null
  if (timer !== null) window.clearTimeout(timer)
  timer = null
  busy.value = false
}
function clear() {
  stop()
  plan.value = null
  hint.value = null
  intent.value = null
  name.value = ''
  error.value = ''
  dialog.value = false
}
function fail(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    const subject = authSession.user.value?.subject
    if (subject) removeDirectoryHint(subject, props.target.containerId)
    clear()
  }
  error.value = reason instanceof Error ? reason.message : '目录任务无法核实，请查询原任务'
}
function accept(value: DirectoryIntent) {
  if (!hint.value) throw new Error('原目录登记已失效')
  assertDirectoryIntent(value, hint.value.command, hint.value.id ?? undefined)
  if (
    intent.value &&
    (value.version < intent.value.version ||
      (directoryTerminal(intent.value.state) && value.state !== intent.value.state))
  )
    throw new Error('目录任务返回了旧状态')
  intent.value = value
  hint.value = { ...hint.value, id: value.id }
  saveDirectoryHint(hint.value)
  if (directoryTerminal(value.state)) {
    removeDirectoryHint(hint.value.subject, props.target.containerId)
    if (value.state === 'Succeeded' && refreshed !== value.id) {
      refreshed = value.id
      emit('refresh')
    }
  }
}
function schedule(token: number) {
  if (token !== generation || !active.value || intent.value?.state === 'Unknown' || document.hidden)
    return
  timer = window.setTimeout(() => {
    timer = null
    void query()
  }, 6000)
}
async function query() {
  const original = hint.value
  if (!allowed.value || !original || busy.value) return
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  try {
    const value = original.id
      ? await getDirectory(props.target, original.id, local.signal)
      : await resolveDirectory(props.target, original.registration, local.signal)
    if (token !== generation || local.signal.aborted) return
    if (value) accept(value)
    else {
      removeDirectoryHint(original.subject, props.target.containerId)
      hint.value = null
      error.value = '原计划已过期，服务器确认没有登记任务。可以重新生成计划。'
    }
  } catch (reason) {
    if (token === generation && !local.signal.aborted) fail(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
      schedule(token)
    }
  }
}
async function prepare() {
  if (!allowed.value || props.disabled || busy.value || hint.value) return
  if (!name.value.trim() || name.value.includes('/')) {
    error.value = '请输入单层目录名称'
    return
  }
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  try {
    const value = await planDirectory(
      props.target,
      filePath(props.path, name.value),
      mode.value,
      local.signal,
    )
    if (token === generation && !local.signal.aborted) plan.value = value
  } catch (reason) {
    if (token === generation && !local.signal.aborted) fail(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
    }
  }
}
async function submit(retry = false) {
  if (
    !allowed.value ||
    busy.value ||
    props.disabled ||
    (retry ? !hint.value || !!intent.value : !plan.value || !!hint.value)
  )
    return
  if (!retry) {
    const subject = authSession.user.value?.subject
    if (!subject || !plan.value) return
    hint.value = {
      subject,
      command: plan.value.command,
      registration: directoryRegistration(plan.value),
      id: null,
    }
    saveDirectoryHint(hint.value)
  }
  const original = hint.value!
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  try {
    const value = await submitDirectory(props.target, original.registration, local.signal)
    if (token === generation && !local.signal.aborted) accept(value)
  } catch (reason) {
    if (token === generation && !local.signal.aborted) {
      fail(reason)
      if (hint.value) error.value += '；登记结果待核实，请查询原任务或重试同一登记。'
    }
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
      schedule(token)
    }
  }
}
async function cancel() {
  if (
    !allowed.value ||
    busy.value ||
    !intent.value ||
    directoryTerminal(intent.value.state) ||
    intent.value.state === 'Unknown'
  )
    return
  stop()
  const token = generation,
    local = new AbortController()
  controller = local
  busy.value = true
  try {
    await cancelDirectory(props.target, intent.value.id, local.signal)
  } catch (reason) {
    if (token === generation && !local.signal.aborted) fail(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      controller = null
      void query()
    }
  }
}
function open() {
  if (!allowed.value || busy.value || props.disabled) return
  if (intent.value && directoryTerminal(intent.value.state)) {
    hint.value = null
    intent.value = null
    plan.value = null
    name.value = ''
    error.value = ''
  }
  dialog.value = true
}
function visible() {
  if (!document.hidden && active.value && !busy.value) void query()
}
watch(
  [
    allowed,
    () => authSession.user.value?.subject,
    () => props.target.containerId,
    () => props.target.instanceId,
  ],
  () => {
    clear()
    const subject = authSession.user.value?.subject
    if (allowed.value && subject) {
      hint.value = directoryHint(subject, props.target.containerId)
      if (hint.value) void query()
    }
  },
  { immediate: true },
)
watch(
  () => props.path,
  () => {
    if (!hint.value) {
      stop()
      plan.value = null
      dialog.value = false
    }
  },
)
watch(dialog, (opened) => {
  if (!opened && !hint.value) {
    stop()
    plan.value = null
    name.value = ''
    error.value = ''
  }
})
watch(
  [dialog, busy, hint, active],
  () =>
    emit('locked', dialog.value || busy.value || (!!hint.value && (!intent.value || active.value))),
  { immediate: true },
)
onMounted(() => document.addEventListener('visibilitychange', visible))
onBeforeUnmount(() => {
  stop()
  document.removeEventListener('visibilitychange', visible)
  emit('locked', false)
})
</script>

<template>
  <section v-if="allowed" class="container-directory" aria-label="容器新建目录">
    <el-button :icon="FolderAdd" :disabled="disabled || busy" @click="open">新建目录</el-button>
    <template v-if="hint">
      <span class="container-directory__state" role="status"
        >{{ intent ? directoryState(intent.state) : '原登记结果待核实' }} ·
        {{ hint.command.location.path }}</span
      >
      <el-button link :disabled="busy" @click="query">查询原任务</el-button>
      <el-button v-if="!intent" link :disabled="busy || disabled" @click="submit(true)"
        >重试原登记</el-button
      >
      <el-button v-if="active && intent?.state !== 'Unknown'" link :disabled="busy" @click="cancel"
        >请求取消</el-button
      >
      <RouterLink v-if="intent?.state === 'Unknown'" to="/files">前往文件异常处置</RouterLink>
    </template>
    <p v-if="error && !dialog" class="container-directory__error" role="alert">{{ error }}</p>
    <el-dialog
      v-model="dialog"
      title="新建容器目录"
      width="min(520px, calc(100vw - 24px))"
      :close-on-click-modal="false"
      destroy-on-close
    >
      <el-form label-position="top" @submit.prevent="prepare">
        <el-form-item label="目录名称"
          ><el-input
            v-model="name"
            aria-label="目录名称"
            :disabled="busy || !!plan || !!hint"
            maxlength="255"
        /></el-form-item>
        <el-form-item label="目录权限"
          ><el-select v-model="mode" aria-label="目录权限" :disabled="busy || !!plan || !!hint"
            ><el-option label="0750 · 所有者可读写，组可读取" value="0750" /><el-option
              label="0755 · 所有者可读写，其他用户可读取"
              value="0755" /><el-option label="0700 · 仅所有者" value="0700" /></el-select
        ></el-form-item>
      </el-form>
      <template v-if="plan"
        ><p class="container-directory__path">/{{ plan.command.location.path }}</p>
        <el-alert :title="plan.warning" type="warning" :closable="false"
      /></template>
      <p v-if="intent" role="status">{{ directoryState(intent.state) }}</p>
      <p v-if="error" class="container-directory__error" role="alert">{{ error }}</p>
      <template #footer>
        <el-button @click="dialog = false">{{ hint ? '关闭' : '取消' }}</el-button>
        <el-button
          v-if="!plan && !hint"
          type="primary"
          :loading="busy"
          :disabled="disabled"
          @click="prepare"
          >生成目录计划</el-button
        >
        <el-button
          v-if="plan && !hint"
          type="primary"
          :loading="busy"
          :disabled="disabled"
          @click="submit()"
          >确认创建目录</el-button
        >
        <el-button v-if="hint" :loading="busy" @click="query">查询原任务</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.container-directory {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
  padding-block: 8px;
}
.container-directory__state,
.container-directory__path {
  overflow-wrap: anywhere;
  min-width: 0;
  font-size: 13px;
}
.container-directory__error {
  width: 100%;
  color: var(--el-color-danger);
  font-size: 13px;
  overflow-wrap: anywhere;
}
</style>
