<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { Upload } from '@element-plus/icons-vue'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import { filePath, hashFile, maximumFileBytes } from '@/services/fileManagement'
import type { ContainerFileTarget } from '@/services/containerFiles'
import {
  assertContainerUploadIntent,
  cancelContainerUpload,
  containerUploadFollows,
  containerUploadState,
  containerUploadTerminal,
  createContainerUpload,
  getContainerUpload,
  inspectContainerUploadEntry,
  newContainerUploadRegistration,
  planContainerUpload,
  uploadContainerContent,
} from '@/services/containerUploads'
import type {
  ContainerUploadIntent,
  ContainerUploadRegistration,
} from '@/services/containerUploads'

const props = defineProps<{
  target: ContainerFileTarget
  path: string
  disabled: boolean
}>()
const emit = defineEmits<{ refresh: [] }>()

const fileInput = ref<HTMLInputElement | null>(null)
const chosen = ref<File | null>(null)
const intent = ref<ContainerUploadIntent | null>(null)
const frozen = ref<ContainerUploadRegistration | null>(null)
const draft = ref<{ path: string; sizeBytes: number; sha256: string } | null>(null)
const busy = ref(false)
const phase = ref('')
const error = ref('')
const progress = ref(0)
const contentAttempted = ref(false)
const confirmationUncertain = ref(false)
const confirmationVisible = ref(false)
const confirmation = ref<{
  title: string
  message: string
  confirmLabel: string
  cancelLabel: string
} | null>(null)
const allowed = computed(() => authSession.user.value?.isAdministrator === true)
const active = computed(() => !!intent.value && !containerUploadTerminal(intent.value.state))
const expected = computed(() =>
  intent.value
    ? {
        path: intent.value.location.path,
        sizeBytes: intent.value.sizeBytes,
        sha256: intent.value.sha256,
      }
    : draft.value,
)

let generation = 0
let controller: AbortController | null = null
let poll: number | null = null
let refreshedId = ''
let confirmationResolver: ((confirmed: boolean) => void) | null = null

function settleConfirmation(confirmed: boolean) {
  const resolve = confirmationResolver
  confirmationResolver = null
  confirmationVisible.value = false
  confirmation.value = null
  resolve?.(confirmed)
}

function requestConfirmation(value: NonNullable<typeof confirmation.value>) {
  settleConfirmation(false)
  confirmation.value = value
  confirmationVisible.value = true
  return new Promise<boolean>((resolve) => {
    confirmationResolver = resolve
  })
}

function abortWork() {
  generation++
  settleConfirmation(false)
  controller?.abort()
  controller = null
  if (poll !== null) window.clearTimeout(poll)
  poll = null
  busy.value = false
}

function reset() {
  abortWork()
  chosen.value = null
  intent.value = null
  frozen.value = null
  draft.value = null
  phase.value = ''
  error.value = ''
  progress.value = 0
  contentAttempted.value = false
  confirmationUncertain.value = false
  refreshedId = ''
  if (fileInput.value) fileInput.value.value = ''
}

function message(reason: unknown) {
  error.value = reason instanceof Error ? reason.message : '容器上传失败，请核实原任务'
}

function selectFile(event: Event) {
  if (!allowed.value || props.disabled || busy.value || active.value || frozen.value) return
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  error.value = ''
  phase.value = ''
  progress.value = 0
  intent.value = null
  draft.value = null
  contentAttempted.value = false
  confirmationUncertain.value = false
  chosen.value = file
  if (file && file.size > maximumFileBytes) {
    error.value = '单文件最多 1 GiB'
    chosen.value = null
    input.value = ''
  }
}

function accept(next: ContainerUploadIntent) {
  const prior = intent.value
  const binding = expected.value
  assertContainerUploadIntent(next, {
    target: props.target,
    path: binding?.path ?? next.location.path,
    sizeBytes: binding?.sizeBytes ?? next.sizeBytes,
    sha256: binding?.sha256 ?? next.sha256,
  })
  if (prior && !containerUploadFollows(prior, next))
    throw new Error('容器上传任务、实例或发布事实已变化；保留任务编号，不重传文件')
  intent.value = next
  phase.value = containerUploadState(next.state)
  confirmationUncertain.value = false
}

function schedule(token: number) {
  if (
    token !== generation ||
    !intent.value ||
    containerUploadTerminal(intent.value.state) ||
    intent.value.state === 'Unknown'
  )
    return
  if (poll !== null) window.clearTimeout(poll)
  poll = window.setTimeout(() => void query(token), 2000)
}

async function query(token = generation, manual = false) {
  const row = intent.value
  const binding = expected.value
  if (!allowed.value || !row || !binding || (busy.value && manual)) return
  if (poll !== null) window.clearTimeout(poll)
  poll = null
  const local = new AbortController()
  controller = local
  if (manual) busy.value = true
  try {
    const next = await getContainerUpload(props.target, row.id, binding, local.signal)
    if (token !== generation || local.signal.aborted) return
    accept(next)
    if (next.state === 'Unknown') {
      error.value = ''
      return
    }
    if (containerUploadTerminal(next.state)) {
      chosen.value = null
      frozen.value = null
      if (next.state === 'Succeeded' && refreshedId !== next.id) {
        refreshedId = next.id
        emit('refresh')
      }
      return
    }
    schedule(token)
  } catch (reason) {
    if (!local.signal.aborted && token === generation) message(reason)
  } finally {
    if (token === generation) {
      if (manual) busy.value = false
      if (controller === local) controller = null
    }
  }
}

async function createAndTransfer(file: File, token: number, local: AbortController) {
  if (!frozen.value) throw new Error('没有已确认的容器上传计划')
  if (!draft.value) throw new Error('容器上传文件绑定已丢失')
  phase.value = '登记上传任务'
  const created = await createContainerUpload(
    props.target,
    frozen.value,
    {
      path: draft.value.path,
      sizeBytes: draft.value.sizeBytes,
      sha256: draft.value.sha256,
    },
    local.signal,
  )
  if (token !== generation || local.signal.aborted) return
  accept(created)
  frozen.value = null
  if (created.state !== 'AwaitingUpload' || contentAttempted.value) {
    await query(token)
    return
  }
  contentAttempted.value = true
  phase.value = '传输中'
  progress.value = 0
  try {
    const received = await uploadContainerContent(
      props.target,
      created,
      file,
      local.signal,
      (value) => {
        if (token === generation) progress.value = Math.floor(value)
      },
    )
    if (token !== generation || local.signal.aborted) return
    accept(received)
  } catch (reason) {
    if (!local.signal.aborted && token === generation) {
      confirmationUncertain.value = true
      message(reason)
    }
    return
  }
  await query(token)
}

async function start(retry = false) {
  if (
    !allowed.value ||
    props.disabled ||
    !chosen.value ||
    busy.value ||
    active.value ||
    (retry ? !frozen.value : !!frozen.value)
  )
    return
  const file = chosen.value
  const target = props.target
  const directory = props.path
  const token = ++generation
  const local = new AbortController()
  controller = local
  busy.value = true
  error.value = ''
  progress.value = 0
  try {
    if (!retry) {
      intent.value = null
      contentAttempted.value = false
      confirmationUncertain.value = false
      phase.value = '计算 SHA-256'
      const digest = await hashFile(file, local.signal, (value) => {
        if (token === generation) progress.value = Math.floor(value)
      })
      if (token !== generation || local.signal.aborted) return
      const destination = filePath(directory, file.name)
      const existing = await inspectContainerUploadEntry(target, destination, local.signal)
      if (token !== generation || local.signal.aborted) return
      if (existing && (existing.kind !== 'File' || existing.isProtected))
        throw new Error('不能覆盖目录、链接、特殊文件或受保护文件')
      const plan = await planContainerUpload(
        target,
        destination,
        file,
        digest,
        existing?.version || 'ABSENT',
        local.signal,
      )
      phase.value = '等待确认'
      const retention = plan.overwrites
        ? '\n覆盖操作会在发布前校验 HDD 备份或受控同盘旧版本留存；请确认留存空间和策略可用。'
        : ''
      const confirmed = await requestConfirmation({
        title: plan.overwrites ? '确认覆盖容器文件' : '确认上传容器文件',
        message: `${destination}\n${formatBytes(file.size)}\n${plan.warning}${retention}`,
        confirmLabel: '确认并上传',
        cancelLabel: '取消',
      })
      if (!confirmed || token !== generation || local.signal.aborted) return
      draft.value = { path: destination, sizeBytes: file.size, sha256: digest }
      frozen.value = newContainerUploadRegistration(plan.planHash)
    }
    await createAndTransfer(file, token, local)
  } catch (reason) {
    if (!local.signal.aborted && reason !== 'cancel' && reason !== 'close') message(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      if (controller === local) controller = null
    }
  }
}

async function cancel() {
  if (busy.value) {
    abortWork()
    if (intent.value && contentAttempted.value) {
      confirmationUncertain.value = true
      phase.value = '本地传输已中止'
      error.value = '只查询原任务，不会重新发送文件字节'
    }
    return
  }
  const row = intent.value
  if (!row) {
    chosen.value = null
    frozen.value = null
    draft.value = null
    phase.value = ''
    progress.value = 0
    if (fileInput.value) fileInput.value.value = ''
    return
  }
  if (containerUploadTerminal(row.state)) return
  const confirmationGeneration = generation
  const confirmed = await requestConfirmation({
    title: '取消容器上传',
    message: '请求取消原上传任务。已接收的字节与发布状态仍以原任务回执为准。',
    confirmLabel: '确认取消',
    cancelLabel: '继续保留',
  })
  if (
    !confirmed ||
    confirmationGeneration !== generation ||
    intent.value?.id !== row.id ||
    containerUploadTerminal(intent.value.state)
  )
    return
  const token = ++generation
  const local = new AbortController()
  controller = local
  busy.value = true
  try {
    await cancelContainerUpload(props.target, row.id, local.signal)
    if (token !== generation || local.signal.aborted) return
    phase.value = '已请求取消，正在核实'
    await query(token)
  } catch (reason) {
    if (!local.signal.aborted && token === generation) message(reason)
  } finally {
    if (token === generation) {
      busy.value = false
      if (controller === local) controller = null
    }
  }
}

watch(
  [
    () => props.target.containerId,
    () => props.target.instanceId,
    () => props.path,
    () => authSession.user.value?.subject,
    allowed,
  ],
  reset,
)
onBeforeUnmount(reset)
</script>

<template>
  <section v-if="allowed" class="container-upload" aria-label="容器上传">
    <div class="container-upload__actions">
      <input
        ref="fileInput"
        type="file"
        class="container-upload__input"
        aria-label="选择容器上传文件"
        :disabled="disabled || busy || active || !!frozen"
        @change="selectFile"
      />
      <el-button
        :icon="Upload"
        :disabled="disabled || busy || active || !!frozen"
        @click="fileInput?.click()"
        >选择上传文件</el-button
      >
      <el-button
        v-if="chosen"
        type="primary"
        :disabled="disabled || busy || active"
        @click="start(!!frozen)"
        >{{ frozen ? '重试原登记' : '校验并上传' }}</el-button
      >
      <el-button v-if="chosen || active" :disabled="disabled" @click="cancel">
        {{ busy ? '中止本地操作' : active ? '取消上传' : '清除选择' }}
      </el-button>
      <el-button v-if="intent" :disabled="busy" @click="query(generation, true)"
        >查询原任务</el-button
      >
    </div>
    <p v-if="chosen" class="container-upload__file">
      {{ chosen.name }} · {{ formatBytes(chosen.size) }}
    </p>
    <el-progress
      v-if="busy && (phase === '计算 SHA-256' || phase === '传输中')"
      :percentage="progress"
      :stroke-width="8"
    />
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <div v-if="intent" class="container-upload__facts">
      <span>{{ containerUploadState(intent.state) }}</span>
      <span>任务 {{ intent.jobId }}</span>
      <span v-if="intent.previousRetained">旧版本已完成 HDD 或受控同盘留存</span>
      <span v-if="intent.publishedVersion">发布版本 {{ intent.publishedVersion }}</span>
    </div>
    <p v-else-if="phase" class="container-upload__phase">{{ phase }}</p>
    <el-alert
      v-if="intent?.state === 'Unknown' || confirmationUncertain"
      title="结果尚未确认：已停止自动查询和文件字节重传。请保留任务编号，需管理员核实。"
      type="warning"
      :closable="false"
      show-icon
    />
    <p class="container-upload__note">
      上传绑定当前容器运行实例和当前目录；切换容器、实例、目录或登录账号会中止本页操作。单文件上限 1
      GiB。
    </p>
    <el-dialog
      v-model="confirmationVisible"
      :title="confirmation?.title || '确认容器上传操作'"
      width="min(520px, calc(100vw - 32px))"
      destroy-on-close
      append-to-body
      @closed="settleConfirmation(false)"
    >
      <p v-if="confirmation" class="container-upload__confirmation">
        {{ confirmation.message }}
      </p>
      <template #footer>
        <el-button @click="settleConfirmation(false)">
          {{ confirmation?.cancelLabel || '取消' }}
        </el-button>
        <el-button type="primary" @click="settleConfirmation(true)">
          {{ confirmation?.confirmLabel || '确认' }}
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.container-upload {
  min-width: 0;
  padding: 12px;
  margin: 12px 0;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  background: var(--el-fill-color-lighter);
}
.container-upload__actions,
.container-upload__facts {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
}
.container-upload__input {
  display: none;
}
.container-upload__file,
.container-upload__phase,
.container-upload__note,
.container-upload__facts {
  overflow-wrap: anywhere;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.container-upload__file,
.container-upload__phase,
.container-upload__note {
  margin: 8px 0 0;
}
.container-upload__facts {
  margin-top: 10px;
}
.container-upload__confirmation {
  margin: 0;
  white-space: pre-line;
  overflow-wrap: anywhere;
  color: var(--el-text-color-regular);
}
.container-upload :deep(.el-alert),
.container-upload :deep(.el-progress) {
  margin-top: 10px;
}
</style>
