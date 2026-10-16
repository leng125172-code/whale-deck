<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import {
  executeNotificationIntent,
  notificationIntent,
  type NotificationChannel,
  type NotificationWriteIntent,
} from '@/services/notificationManagement'
import {
  emptyPrivateForm,
  preparePrivate,
  privatePayload,
  privateSubmitIntent,
  submitPrivate,
  type PrivateMetadata,
  type PrivateStatus,
  type PrivatePlan,
  type PrivateJob,
} from '@/services/notificationPrivate'

const props = defineProps<{ channel: NotificationChannel }>()
const emit = defineEmits<{ close: []; changed: []; forbidden: [] }>()
const owner = authSession.user.value?.subject
const allowed = computed(
  () =>
    authSession.user.value?.subject === owner && authSession.user.value?.isAdministrator === true,
)
const form = reactive(emptyPrivateForm())
const metadata = ref<PrivateMetadata | null>(null)
const status = ref<PrivateStatus | null>(null)
const job = ref<PrivateJob | null>(null)
const plan = ref<PrivatePlan | null>(null)
const ticket = ref<string | null>(null)
const intent = ref<NotificationWriteIntent | null>(null)
const ack = ref<NotificationWriteIntent | null>(null)
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const traceId = ref('')
const version = ref(props.channel.version)
const pending = computed(
  () => status.value !== null && ['Pending', 'Running', 'Unknown'].includes(status.value.state),
)
const locked = computed(
  () =>
    busy.value ||
    loading.value ||
    plan.value !== null ||
    intent.value !== null ||
    pending.value ||
    !allowed.value,
)
const urlIsPrivate = computed(() => props.channel.provider !== 'Email')
let controller = new AbortController()
let generation = 0
let poll: number | undefined
function clearInput() {
  Object.assign(form, emptyPrivateForm())
}
function reset() {
  generation++
  controller.abort()
  controller = new AbortController()
  window.clearTimeout(poll)
  clearInput()
  metadata.value = null
  status.value = null
  job.value = null
  plan.value = null
  ticket.value = null
  intent.value = null
  ack.value = null
}
function close() {
  reset()
  emit('close')
}
function fail(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    reset()
    emit('forbidden')
    emit('close')
    return
  }
  error.value =
    reason instanceof ApiError && reason.status === 409
      ? '配置或任务版本已变化。重新载入核对；不要直接创建重复任务。'
      : reason instanceof Error
        ? reason.message
        : '私有配置操作失败'
  traceId.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
}
async function refresh() {
  if (!allowed.value || busy.value || loading.value) return
  const current = generation
  loading.value = true
  try {
    const next = await apiRequest<PrivateStatus | null>(
      `notifications/manage/channels/${props.channel.id}/private/status`,
      { signal: controller.signal },
    )
    if (current !== generation || !allowed.value) return
    status.value = next
    const nextJob = next
      ? await apiRequest<PrivateJob>(`jobs/${next.jobId}`, { signal: controller.signal })
      : null
    if (current !== generation || !allowed.value) return
    job.value = nextJob
    if (!pending.value) {
      const nextMetadata = await apiRequest<PrivateMetadata>(
        `notifications/manage/channels/${props.channel.id}/private`,
        { signal: controller.signal },
      )
      if (current !== generation || !allowed.value) return
      metadata.value = nextMetadata
      if (Number.isSafeInteger(nextMetadata.channelVersion) && nextMetadata.channelVersion! >= 0)
        version.value = nextMetadata.channelVersion!
      if (!plan.value && !intent.value)
        form.credentialMode = nextMetadata.hasCredential
          ? 'keep'
          : ['DingTalk', 'Feishu'].includes(props.channel.provider)
            ? 'replace'
            : 'remove'
    }
  } catch (reason) {
    if (current === generation && !controller.signal.aborted) fail(reason)
  } finally {
    if (current === generation) {
      loading.value = false
      window.clearTimeout(poll)
      if (pending.value)
        poll = window.setTimeout(refresh, status.value?.state === 'Unknown' ? 6000 : 2000)
    }
  }
}
async function prepare(action: 'apply' | 'remove') {
  if (locked.value) return
  const current = generation
  busy.value = true
  error.value = ''
  traceId.value = ''
  try {
    const payload = action === 'apply' ? privatePayload(props.channel.provider, form) : null
    const prepared = await preparePrivate(
      props.channel.id,
      version.value,
      action,
      payload,
      controller.signal,
    )
    if (current !== generation || !allowed.value) return
    plan.value = prepared.plan
    ticket.value = prepared.ticket
  } catch (reason) {
    if (current === generation && !controller.signal.aborted) fail(reason)
  } finally {
    clearInput()
    if (current === generation) busy.value = false
  }
}
function discardPlan() {
  if (intent.value || busy.value) return
  plan.value = null
  ticket.value = null
  clearInput()
}
async function submit() {
  if (!allowed.value || busy.value || (!plan.value && !intent.value)) return
  const current = generation
  busy.value = true
  try {
    if (!intent.value) {
      if (Date.parse(plan.value!.expiresAtUtc) <= Date.now()) {
        plan.value = null
        ticket.value = null
        clearInput()
        throw new Error('计划已过期，请重新输入并准备。')
      }
      await ElMessageBox.confirm(plan.value!.warning, '确认私有配置变更', {
        type: 'warning',
        confirmButtonText: '停用渠道并提交',
        cancelButtonText: '取消',
      })
      if (current !== generation || !allowed.value) return
      intent.value = privateSubmitIntent(props.channel.id, plan.value!, ticket.value)
    }
    busy.value = true
    const result = await submitPrivate(intent.value, controller.signal)
    if (current !== generation || !allowed.value) return
    job.value = result
    intent.value = null
    plan.value = null
    ticket.value = null
    error.value = ''
    emit('changed')
    busy.value = false
    await refresh()
  } catch (reason) {
    if (
      reason !== 'cancel' &&
      reason !== 'close' &&
      current === generation &&
      !controller.signal.aborted
    )
      fail(reason)
  } finally {
    if (current === generation) busy.value = false
  }
}
async function acknowledge() {
  if (!allowed.value || busy.value || (!ack.value && !status.value?.canAcknowledgeUnknown)) return
  const current = generation
  const acknowledgedStatus = status.value
  busy.value = true
  try {
    await ElMessageBox.confirm(
      '结果仍可能已生效。此操作只解除旧任务锁，不回滚、不启用发送；必须重新配置并成功对账后才能启用。',
      '确认未知结果',
      { type: 'warning', confirmButtonText: '接受未知结果', cancelButtonText: '取消' },
    )
    if (current !== generation || !allowed.value) return
    if (!ack.value) {
      if (!acknowledgedStatus) return
      ack.value = notificationIntent(
        `channels/${props.channel.id}/private/acknowledge-unknown`,
        'POST',
        {
          operationId: acknowledgedStatus.jobId,
          expectedVersion: acknowledgedStatus.version,
          confirmed: true,
          acceptUnknownOutcome: true,
        },
      )
    }
    busy.value = true
    await executeNotificationIntent(ack.value, controller.signal)
    if (current !== generation || !allowed.value) return
    ack.value = null
    error.value = ''
    busy.value = false
    emit('changed')
    await refresh()
  } catch (reason) {
    if (
      reason !== 'cancel' &&
      reason !== 'close' &&
      current === generation &&
      !controller.signal.aborted
    )
      fail(reason)
  } finally {
    if (current === generation) busy.value = false
  }
}
watch(
  () => [authSession.user.value?.subject, authSession.user.value?.isAdministrator],
  () => {
    if (!allowed.value) close()
  },
)
onMounted(refresh)
onUnmounted(reset)
</script>

<template>
  <el-dialog
    class="private-dialog"
    :model-value="true"
    :title="`私有配置 · ${channel.name}`"
    width="min(680px, calc(100vw - 24px))"
    top="4vh"
    :close-on-click-modal="false"
    @close="close"
  >
    <div class="private-editor" v-loading="loading">
      <el-alert
        title="已有地址与凭据不回显；保存后渠道保持停用"
        description="清空输入不会删除现有值。请明确选择保留、替换或移除凭据；只有明确准备清除配置才删除绑定。"
        type="info"
        :closable="false"
      />
      <el-alert
        v-if="error"
        :title="error"
        :description="traceId ? `追踪 ID：${traceId}` : undefined"
        type="error"
        :closable="false"
      />
      <section class="private-status" aria-label="私有配置状态">
        <span
          >目标：{{
            metadata?.hasTarget ? '已登记（不回显）' : pending ? '等待对账' : '未登记'
          }}</span
        >
        <span
          >凭据：{{
            metadata?.hasCredential ? '已登记（不回显）' : pending ? '等待对账' : '未登记'
          }}</span
        >
        <span v-if="status">任务：{{ status.state }} · {{ job?.phase || '等待状态' }}</span>
        <small v-if="job">任务 ID：{{ job.id }}</small>
        <el-button :disabled="busy || loading" @click="refresh">核对状态</el-button>
      </section>
      <el-alert
        v-if="status?.state === 'Succeeded'"
        :title="
          metadata?.hasTarget ? '配置任务已完成，渠道仍保持停用' : '私有配置已清除，发送继续阻断'
        "
        :description="
          metadata?.hasTarget
            ? '请核对测试投递后，在渠道元数据中明确启用。完成配置不代表消息已送达。'
            : '清除后不能启用发送，须重新配置并成功对账。'
        "
        type="success"
        :closable="false"
      />
      <el-alert
        v-if="status?.state === 'Unknown'"
        title="宿主结果未知，不要盲目重复创建"
        type="warning"
        :closable="false"
      />
      <el-button
        v-if="ack || status?.canAcknowledgeUnknown"
        type="warning"
        :disabled="busy"
        @click="acknowledge"
        >{{ ack ? '重试原确认请求' : '确认未知结果' }}</el-button
      >
      <el-alert
        v-if="intent"
        title="提交响应可能丢失，原请求已冻结"
        description="使用重试原请求核对同一个任务；不要修改配置后新建任务。"
        type="warning"
        :closable="false"
      />
      <el-alert
        v-if="plan"
        title="计划已准备，秘密输入已清空"
        :description="`${plan.warning} 到期时间：${new Date(plan.expiresAtUtc).toLocaleString('zh-CN')}`"
        type="warning"
        :closable="false"
      />
      <el-form
        label-position="top"
        :disabled="locked"
        autocomplete="off"
        @submit.prevent="prepare('apply')"
      >
        <el-form-item v-if="urlIsPrivate" label="私有目标地址"
          ><el-input
            v-model="form.url"
            type="password"
            show-password
            autocomplete="new-password"
            maxlength="4096"
            placeholder="填写完整地址；机器人使用官方端点"
        /></el-form-item>
        <template v-else>
          <div class="private-grid">
            <el-form-item label="SMTP 主机"
              ><el-input v-model="form.smtpHost" maxlength="253" /></el-form-item
            ><el-form-item label="SMTP 端口"
              ><el-input-number v-model="form.smtpPort" :min="1" :max="65535"
            /></el-form-item>
          </div>
          <el-form-item label="TLS 模式"
            ><el-select v-model="form.tls"
              ><el-option label="STARTTLS" value="StartTls" /><el-option
                label="直接 TLS"
                value="SslOnConnect" /></el-select
          ></el-form-item>
          <el-form-item label="发件邮箱"
            ><el-input v-model="form.from" autocomplete="off" maxlength="254"
          /></el-form-item>
          <el-form-item label="收件邮箱（最多 10 个，逗号或换行分隔）"
            ><el-input
              v-model="form.to"
              type="textarea"
              :rows="2"
              autocomplete="off"
              maxlength="2560"
          /></el-form-item>
        </template>
        <el-form-item v-if="channel.provider !== 'WeCom'" label="凭据策略"
          ><el-select v-model="form.credentialMode"
            ><el-option
              label="保留已登记凭据（不回显）"
              value="keep"
              :disabled="!metadata?.hasCredential" /><el-option
              label="替换：全部重新输入"
              value="replace" /><el-option label="移除独立凭据" value="remove" /></el-select
        ></el-form-item>
        <template v-if="form.credentialMode === 'replace'">
          <template v-if="channel.provider === 'Email'"
            ><el-form-item label="SMTP 用户名"
              ><el-input
                v-model="form.username"
                autocomplete="off"
                maxlength="4096" /></el-form-item
            ><el-form-item label="SMTP 密码"
              ><el-input
                v-model="form.password"
                type="password"
                show-password
                autocomplete="new-password"
                maxlength="4096" /></el-form-item
          ></template>
          <el-form-item v-if="channel.provider === 'Webhook'" label="Bearer Token"
            ><el-input
              v-model="form.bearerToken"
              type="password"
              show-password
              autocomplete="new-password"
              maxlength="4096"
          /></el-form-item>
          <el-form-item
            v-if="['Webhook', 'DingTalk', 'Feishu'].includes(channel.provider)"
            label="签名密钥"
            ><el-input
              v-model="form.signingSecret"
              type="password"
              show-password
              autocomplete="new-password"
              maxlength="4096"
          /></el-form-item>
        </template>
        <el-form-item label="批准 IP 地址（最多 8 个，逗号分隔）"
          ><el-input
            v-model="form.allowedAddresses"
            maxlength="512"
            placeholder="公共 HTTPS 可留空；私网需明确批准地址"
        /></el-form-item>
        <el-form-item label="允许批准的私网目标"
          ><el-switch v-model="form.allowPrivateNetwork"
        /></el-form-item>
        <el-form-item v-if="channel.provider === 'Webhook'" label="允许私网 HTTP（不用于公网）"
          ><el-switch v-model="form.allowHttp"
        /></el-form-item>
        <el-form-item label="最小投递间隔（秒）"
          ><el-input-number v-model="form.minimumIntervalSeconds" :min="5" :max="300"
        /></el-form-item>
      </el-form>
      <div class="private-actions">
        <el-button :disabled="locked" :loading="busy" type="primary" @click="prepare('apply')"
          >准备配置计划</el-button
        ><el-button
          type="danger"
          :disabled="locked || !metadata?.hasTarget"
          @click="prepare('remove')"
          >准备清除配置</el-button
        >
      </div>
    </div>
    <template #footer
      ><el-button @click="close">关闭并清空</el-button
      ><el-button v-if="plan && !intent" :disabled="busy" @click="discardPlan">放弃计划</el-button
      ><el-button
        v-if="plan || intent"
        type="primary"
        :loading="busy"
        :disabled="!allowed"
        @click="submit"
        >{{ intent ? '重试原请求' : '确认并提交' }}</el-button
      ></template
    >
  </el-dialog>
</template>

<style scoped>
:global(.private-dialog) {
  display: flex;
  flex-direction: column;
  max-height: 92vh;
}
:global(.private-dialog .el-dialog__body) {
  min-height: 0;
  overflow-y: auto;
}
:global(.private-dialog .el-dialog__header),
:global(.private-dialog .el-dialog__footer) {
  flex-shrink: 0;
}
.private-editor {
  display: grid;
  gap: 14px;
  min-width: 0;
}
.private-status {
  display: grid;
  gap: 8px;
  font-size: 13px;
  overflow-wrap: anywhere;
}
.private-grid {
  display: grid;
  grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
  gap: 12px;
}
.private-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.private-actions .el-button {
  margin: 0;
}
:deep(.el-select),
:deep(.el-input-number) {
  max-width: 100%;
  width: 100%;
}
@media (max-width: 560px) {
  .private-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
