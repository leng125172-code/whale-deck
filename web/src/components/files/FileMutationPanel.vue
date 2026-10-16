<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import { formatBytes } from '@/services/displayUnits'
import { filePath } from '@/services/fileManagement'
import type { FileEntry, FileRoot } from '@/services/fileManagement'
import {
  mutationEligible,
  mutationLabel,
  mutationState,
  mutationTarget,
  mutationTerminal,
} from '@/services/fileMutations'
import type {
  FileMutationCommand,
  FileMutationIntent,
  FileMutationKind,
  FileMutationPlan,
  FrozenFileMutation,
} from '@/services/fileMutations'

const props = defineProps<{ roots: FileRoot[]; rootId: string; path: string; disabled: boolean }>()
const route = useRoute(),
  router = useRouter()
const emit = defineEmits<{ locked: [value: boolean]; changed: []; failure: [reason: unknown] }>()
const authorized = computed(() => authSession.user.value?.isAdministrator === true)
const dialog = ref(false),
  submitting = ref(false),
  kind = ref<FileMutationKind>('mkdir')
const source = ref<FileMutationCommand['source']>(null),
  expectedVersion = ref('ABSENT')
const targetRoot = ref(''),
  targetPath = ref(''),
  mode = ref('0750')
const planned = ref<FileMutationPlan | null>(null),
  frozen = ref<FrozenFileMutation | null>(null)
const intent = ref<FileMutationIntent | null>(null),
  error = ref(''),
  trace = ref('')
const active = computed(() => intent.value !== null && !mutationTerminal(intent.value.state))
const targetRoots = computed(() =>
  props.roots.filter(
    (root) =>
      root.actions.includes('files-mutate') &&
      (kind.value === 'mkdir' || root.actions.includes('files-upload')),
  ),
)
const locked = computed(() => dialog.value || submitting.value || !!frozen.value || active.value)
const command = computed<FileMutationCommand>(() => ({
  kind: kind.value,
  source: source.value,
  target: kind.value === 'chmod' ? null : { rootId: targetRoot.value, path: targetPath.value },
  expectedVersion: expectedVersion.value,
  mode: kind.value === 'mkdir' || kind.value === 'chmod' ? mode.value : '',
}))
const valid = computed(
  () =>
    (kind.value === 'chmod' ||
      (mutationTarget(targetPath.value) &&
        targetRoots.value.some((root) => root.id === targetRoot.value))) &&
    (!(kind.value === 'mkdir' || kind.value === 'chmod') || /^0[0-7]{3}$/.test(mode.value)) &&
    !(
      source.value &&
      command.value.target?.rootId === source.value.rootId &&
      command.value.target.path === source.value.path
    ),
)
let pending: AbortController | null = null,
  timer: number | null = null,
  generation = 0
let checking = false,
  disposed = false
watch(locked, (value) => emit('locked', value), { immediate: true })
function fail(reason: unknown) {
  if (
    reason instanceof ApiError &&
    (reason.status === 401 ||
      (reason.status === 403 && reason.problem.code !== 'AGENT_OPERATION_FORBIDDEN'))
  )
    clearSensitiveState()
  error.value =
    reason instanceof ApiError
      ? (
          {
            401: '登录已失效',
            403: '没有权限或目标受到保护',
            404: '文件或任务不存在',
            409: '版本、路径锁或任务状态已变化，请刷新并核实原任务',
            429: '操作预算已用完，请稍后查询',
            503: 'Agent 暂时不可用，请稍后查询原任务',
          } as Record<number, string>
        )[reason.status] || reason.message
      : reason instanceof Error
        ? reason.message
        : '文件操作失败'
  trace.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
  emit('failure', reason)
}
function clearTimer() {
  if (timer !== null) window.clearTimeout(timer)
  timer = null
}
function clearSensitiveState() {
  generation++
  pending?.abort()
  pending = null
  clearTimer()
  frozen.value = null
  planned.value = null
  intent.value = null
  source.value = null
  dialog.value = false
  submitting.value = false
  error.value = ''
  trace.value = ''
}
watch(authorized, (value) => {
  if (!value) clearSensitiveState()
})
function open(action: FileMutationKind, entry?: FileEntry) {
  if (!authorized.value || props.disabled || locked.value) return
  if (action !== 'mkdir' && (!entry || !mutationEligible(entry))) return
  if (
    !props.roots.some((root) => root.id === props.rootId && root.actions.includes('files-mutate'))
  )
    return
  kind.value = action
  planned.value = null
  intent.value = null
  error.value = ''
  trace.value = ''
  source.value =
    action === 'mkdir' ? null : { rootId: props.rootId, path: filePath(props.path, entry!.name) }
  expectedVersion.value = entry?.version || 'ABSENT'
  targetRoot.value = props.rootId
  targetPath.value =
    action === 'mkdir'
      ? filePath(props.path, '新建目录')
      : filePath(props.path, entry!.name + (action === 'copy' ? '.copy' : '.moved'))
  mode.value = action === 'chmod' ? entry!.mode : '0750'
  dialog.value = true
}
async function prepare() {
  if (!authorized.value || !valid.value || submitting.value) return
  pending = new AbortController()
  const request = pending,
    token = generation
  submitting.value = true
  error.value = ''
  try {
    const result = await apiRequest<FileMutationPlan>('files/operations/plan', {
      method: 'POST',
      body: JSON.stringify(command.value),
      signal: request.signal,
    })
    if (token === generation && !request.signal.aborted) planned.value = result
  } catch (reason) {
    if (token === generation && !request.signal.aborted) fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
}
async function submit() {
  if (!authorized.value || submitting.value || (!planned.value && !frozen.value)) return
  frozen.value ??= {
    planHash: planned.value!.planHash,
    confirmed: true,
    idempotencyKey: createIdempotencyKey(),
  }
  pending = new AbortController()
  const request = pending,
    token = generation
  submitting.value = true
  error.value = ''
  dialog.value = false
  try {
    const result = await apiRequest<FileMutationIntent>('files/operations', {
      method: 'POST',
      signal: request.signal,
      headers: { 'Idempotency-Key': frozen.value.idempotencyKey },
      body: JSON.stringify(frozen.value),
    })
    if (token !== generation || request.signal.aborted) return
    intent.value = result
    frozen.value = null
    planned.value = null
    void router.replace({ query: { ...route.query, operationId: result.id } })
    void check()
  } catch (reason) {
    if (token === generation && !request.signal.aborted) fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
}
async function check() {
  if (!intent.value || !authorized.value || checking || disposed) return
  clearTimer()
  checking = true
  const token = generation,
    id = intent.value.id
  try {
    const result = await apiRequest<FileMutationIntent>(`files/operations/${id}`)
    if (token !== generation || disposed) return
    intent.value = result
    if (mutationTerminal(result.state)) {
      if (result.state === 'Succeeded') ElMessage.success('文件操作已完成')
      emit('changed')
      return
    }
    if (result.state !== 'Unknown') timer = window.setTimeout(() => void check(), 2000)
  } catch (reason) {
    if (token === generation && !disposed) fail(reason)
  } finally {
    checking = false
  }
}
async function resolveRegistration() {
  const request = frozen.value
  if (!request || submitting.value || !authorized.value) return
  const token = generation
  submitting.value = true
  try {
    const result = await apiRequest<FileMutationIntent | null>('files/operations/resolve', {
      method: 'POST',
      headers: { 'Idempotency-Key': request.idempotencyKey },
      body: JSON.stringify(request),
    })
    if (token !== generation || disposed) return
    frozen.value = null
    planned.value = null
    error.value = ''
    intent.value = result
    if (result) {
      void router.replace({ query: { ...route.query, operationId: result.id } })
      void check()
    } else ElMessage.info('原计划已到期，已核实没有登记任务，可以重新操作')
  } catch (reason) {
    if (token === generation && !disposed) fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
}
async function cancel() {
  if (!intent.value || submitting.value || !authorized.value) return
  submitting.value = true
  const token = generation
  try {
    await apiRequest(`files/operations/${intent.value.id}/cancel`, {
      method: 'POST',
      headers: { 'Idempotency-Key': intent.value.id },
    })
    if (token === generation) void check()
  } catch (reason) {
    if (token === generation) fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
}
async function acknowledge() {
  const current = intent.value
  if (!current || current.state !== 'Unknown' || !authorized.value || submitting.value) return
  const token = generation
  try {
    await ElMessageBox.confirm(
      '请先检查源与目标文件、内容和权限。确认只接受历史结果未知并释放路径锁，不会回滚、重做、删除证据或将任务标为成功。',
      '人工确认未知结果',
      {
        type: 'warning',
        confirmButtonText: '已核实，接受未知结果',
        cancelButtonText: '保留待调查',
      },
    )
    if (token !== generation || !authorized.value || disposed) return
    submitting.value = true
    await apiRequest(`files/operations/${current.id}/acknowledge`, {
      method: 'POST',
      headers: { 'Idempotency-Key': current.id },
      body: JSON.stringify({ version: current.version, confirmed: true }),
    })
    if (token === generation) void check()
  } catch (reason) {
    if (token === generation && reason !== 'cancel' && reason !== 'close') fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
}
function closeDialog() {
  if (!submitting.value) {
    dialog.value = false
    planned.value = null
    source.value = null
  }
}
onMounted(async () => {
  const id = route.query.operationId
  if (
    !authorized.value ||
    typeof id !== 'string' ||
    !/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/.test(id)
  )
    return
  const token = generation
  submitting.value = true
  try {
    const result = await apiRequest<FileMutationIntent>(`files/operations/${id}`)
    if (token !== generation || disposed) return
    intent.value = result
    if (!mutationTerminal(result.state)) void check()
  } catch (reason) {
    if (token === generation && !disposed) fail(reason)
  } finally {
    if (token === generation) submitting.value = false
  }
})
onBeforeUnmount(() => {
  disposed = true
  clearSensitiveState()
  emit('locked', false)
})
defineExpose({ open })
</script>

<template>
  <div
    v-if="authorized && (intent || frozen)"
    class="mutation-status"
    data-testid="file-mutation-status"
    aria-live="polite"
  >
    <div class="mutation-status__heading">
      <strong>{{ intent ? mutationLabel(intent.command.kind) : '登记文件操作' }}</strong>
      <el-tag
        v-if="intent"
        :type="
          intent.state === 'Succeeded' ? 'success' : intent.state === 'Unknown' ? 'warning' : 'info'
        "
        >{{ mutationState(intent.state) }}</el-tag
      >
    </div>
    <p v-if="intent" class="mutation-path">
      {{ (intent.command.target || intent.command.source)?.rootId }} /
      {{ (intent.command.target || intent.command.source)?.path }}
    </p>
    <p v-if="intent">
      任务 <router-link :to="`/jobs?jobId=${intent.jobId}`">{{ intent.jobId }}</router-link>
    </p>
    <p v-if="intent?.sourceRetained">
      完整原文件已保留在宿主私有目录；不是 HDD 备份或可直接恢复的回收站。
    </p>
    <el-alert
      v-if="intent?.state === 'Unknown'"
      title="结果未知，禁止自动重做。源或目标可能已经变化，请先核实现场。"
      type="warning"
      :closable="false"
    />
    <el-alert
      v-if="error"
      :title="error"
      :description="trace ? `追踪号：${trace}` : undefined"
      type="error"
      :closable="false"
    />
    <p v-if="intent?.resultCode">{{ intent.resultCode }}</p>
    <p v-if="frozen">登记响应可能丢失，原请求已冻结。只能重试原登记，不能重新生成操作。</p>
    <div class="mutation-actions">
      <el-button v-if="frozen" :loading="submitting" @click="submit">重试原操作登记</el-button>
      <el-button v-if="frozen" :disabled="submitting" @click="resolveRegistration"
        >查询原登记（到期未登记可释放）</el-button
      >
      <el-button
        v-if="intent && !mutationTerminal(intent.state)"
        :disabled="submitting"
        @click="check"
        >查询原操作</el-button
      >
      <el-button
        v-if="intent && !mutationTerminal(intent.state) && intent.state !== 'Unknown'"
        :loading="submitting"
        @click="cancel"
        >请求取消操作</el-button
      >
      <el-button
        v-if="intent?.state === 'Unknown'"
        type="warning"
        :loading="submitting"
        @click="acknowledge"
        >人工确认未知结果</el-button
      >
    </div>
  </div>
  <el-dialog
    v-if="authorized"
    :model-value="dialog"
    :title="mutationLabel(kind)"
    width="min(560px, calc(100vw - 32px))"
    append-to-body
    :close-on-click-modal="false"
    :close-on-press-escape="!submitting"
    :show-close="!submitting"
    @update:model-value="closeDialog"
  >
    <template v-if="!planned">
      <el-alert
        title="只处理登记根内的单个对象；不覆盖已有目标。目录复制、所有者修改请勿通过此入口尝试。"
        type="info"
        :closable="false"
      />
      <el-form label-position="top" class="mutation-form" @submit.prevent="prepare">
        <el-form-item v-if="source" label="源文件"
          ><p class="mutation-path">{{ source.rootId }} / {{ source.path }}</p></el-form-item
        >
        <template v-if="kind !== 'chmod'">
          <el-form-item label="目标根目录"
            ><el-select v-model="targetRoot" aria-label="操作目标根目录" :disabled="submitting"
              ><el-option
                v-for="root in targetRoots"
                :key="root.id"
                :value="root.id"
                :label="root.name" /></el-select
          ></el-form-item>
          <el-form-item label="目标相对路径"
            ><el-input
              v-model="targetPath"
              aria-label="操作目标相对路径"
              :disabled="submitting"
              maxlength="4096"
          /></el-form-item>
        </template>
        <el-form-item v-if="kind === 'mkdir' || kind === 'chmod'" label="普通权限模式（0000–0777）"
          ><el-input v-model="mode" aria-label="操作权限模式" :disabled="submitting" maxlength="4"
        /></el-form-item>
      </el-form>
    </template>
    <template v-else>
      <el-alert :title="planned.warning" type="warning" :closable="false" />
      <dl class="mutation-summary">
        <dt>操作</dt>
        <dd>{{ mutationLabel(planned.command.kind) }}</dd>
        <template v-if="planned.command.source"
          ><dt>源</dt>
          <dd>{{ planned.command.source.rootId }} / {{ planned.command.source.path }}</dd></template
        >
        <template v-if="planned.command.target"
          ><dt>目标</dt>
          <dd>{{ planned.command.target.rootId }} / {{ planned.command.target.path }}</dd></template
        >
        <dt>大小</dt>
        <dd>{{ formatBytes(planned.sizeBytes) }}</dd>
        <template v-if="planned.command.mode"
          ><dt>权限</dt>
          <dd>{{ planned.command.mode }}</dd></template
        >
        <dt>计划到期</dt>
        <dd>{{ new Date(planned.expiresAtUtc).toLocaleString() }}</dd>
      </dl>
    </template>
    <el-alert
      v-if="error"
      :title="error"
      :description="trace ? `追踪号：${trace}` : undefined"
      type="error"
      :closable="false"
    />
    <template #footer
      ><div class="mutation-actions">
        <el-button :disabled="submitting" @click="closeDialog">取消</el-button>
        <el-button v-if="planned" :disabled="submitting" @click="planned = null"
          >返回修改</el-button
        >
        <el-button v-if="planned" type="primary" :loading="submitting" @click="submit"
          >确认并执行</el-button
        >
        <el-button v-else type="primary" :loading="submitting" :disabled="!valid" @click="prepare"
          >生成操作计划</el-button
        >
      </div></template
    >
  </el-dialog>
</template>

<style scoped>
.mutation-status {
  margin: 16px 0;
  padding: 16px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 10px;
  background: var(--el-fill-color-extra-light);
  min-width: 0;
}
.mutation-status__heading,
.mutation-actions {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  align-items: center;
}
.mutation-actions {
  justify-content: flex-end;
  margin-top: 14px;
}
.mutation-actions .el-button + .el-button {
  margin-left: 0;
}
.mutation-path,
.mutation-status p,
.mutation-summary dd {
  overflow-wrap: anywhere;
  white-space: pre-wrap;
  margin: 8px 0;
}
.mutation-form {
  margin-top: 18px;
}
.mutation-summary {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 8px 16px;
  margin: 18px 0;
}
.mutation-summary dt {
  color: var(--el-text-color-secondary);
}
.mutation-summary dd {
  margin: 0;
}
</style>
