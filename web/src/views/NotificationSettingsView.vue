<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import {
  executeNotificationIntent,
  notificationIntent,
  notificationPage,
  notificationStates,
  type NotificationChannel,
  type NotificationRoute,
  type NotificationDelivery,
  type NotificationProvider,
  type NotificationWriteIntent,
  type NotificationAttempt,
  type NotificationSubmission,
} from '@/services/notificationManagement'

const active = ref<'channels' | 'routes' | 'deliveries'>('channels')
const channels = ref<NotificationChannel[]>([])
const choices = ref<NotificationChannel[]>([])
const routes = ref<NotificationRoute[]>([])
const deliveries = ref<NotificationDelivery[]>([])
const page = ref(1)
const hasMore = ref(false)
const filterChannel = ref('')
const filterState = ref('')
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const traceId = ref('')
const attempts = ref<NotificationAttempt[]>([])
const attemptVisible = ref(false)
const attemptLoading = ref(false)
let attemptRevision = 0
const denied = ref(false)
const authorized = computed(() => authSession.user.value?.isAdministrator === true && !denied.value)
const editor = ref<'channel' | 'route' | null>(null)
const editingId = ref<string | null>(null)
const expectedVersion = ref<number | null>(null)
const intent = ref<NotificationWriteIntent | null>(null)
const channelForm = reactive({
  name: '',
  provider: 'Webhook' as NotificationProvider,
  destinationReference: '',
  credentialReference: '',
  keepCredential: false,
  isEnabled: false,
})
const routeForm = reactive({
  channelId: '',
  sourceKind: 'all' as NotificationRoute['sourceKind'],
  minimumSeverity: 'Warning' as NotificationRoute['minimumSeverity'],
  includeRecovery: true,
  isEnabled: false,
})
const providers: NotificationProvider[] = ['Email', 'Webhook', 'DingTalk', 'WeCom', 'Feishu']
let controller = new AbortController()
let generation = 0
const deleteIntents = new Map<string, NotificationWriteIntent>()
const submissionIntents = new Map<string, NotificationWriteIntent>()
const impact = '配置变化会取消该渠道尚未发送的旧版本投递；已经发送中的消息不会自动撤回或重发。'

function closeEditor() {
  editor.value = null
  editingId.value = null
  expectedVersion.value = null
  intent.value = null
  Object.assign(channelForm, {
    name: '',
    provider: 'Webhook',
    destinationReference: '',
    credentialReference: '',
    keepCredential: false,
    isEnabled: false,
  })
  Object.assign(routeForm, {
    channelId: '',
    sourceKind: 'all',
    minimumSeverity: 'Warning',
    includeRecovery: true,
    isEnabled: false,
  })
}
function clearPrivate() {
  generation++
  controller.abort()
  controller = new AbortController()
  denied.value = true
  channels.value = []
  choices.value = []
  routes.value = []
  deliveries.value = []
  deleteIntents.clear()
  submissionIntents.clear()
  attemptRevision++
  attempts.value = []
  attemptVisible.value = false
  attemptLoading.value = false
  closeEditor()
  loading.value = false
  saving.value = false
}
function failure(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) clearPrivate()
  error.value =
    reason instanceof ApiError && reason.status === 409
      ? '配置已变化、请求键已用于其他操作，或历史投递阻止删除。请重新载入核对；不要盲目重复创建。'
      : reason instanceof Error
        ? reason.message
        : '通知配置操作失败'
  traceId.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
}
async function load() {
  if (!authorized.value) return
  const current = ++generation
  controller.abort()
  controller = new AbortController()
  const signal = controller.signal
  loading.value = true
  error.value = ''
  traceId.value = ''
  try {
    const filters = {
      channelId: filterChannel.value,
      state: active.value === 'deliveries' ? filterState.value : undefined,
    }
    const [data, options] = await Promise.all([
      notificationPage<NotificationChannel | NotificationRoute | NotificationDelivery>(
        active.value,
        page.value,
        signal,
        active.value === 'channels' ? {} : filters,
      ),
      notificationPage<NotificationChannel>('channels', 1, signal, {}, 100),
    ])
    if (current !== generation || !authorized.value) return
    choices.value = options.items
    hasMore.value = data.hasMore
    if (active.value === 'channels') channels.value = data.items as NotificationChannel[]
    else if (active.value === 'routes') routes.value = data.items as NotificationRoute[]
    else deliveries.value = data.items as NotificationDelivery[]
  } catch (reason) {
    if (current === generation && !signal.aborted) failure(reason)
  } finally {
    if (current === generation) loading.value = false
  }
}
function openChannel(item?: NotificationChannel) {
  closeEditor()
  editor.value = 'channel'
  editingId.value = item?.id || null
  expectedVersion.value = item?.version ?? null
  if (item)
    Object.assign(channelForm, {
      name: item.name,
      provider: item.provider,
      destinationReference: item.destinationReference,
      credentialReference: '',
      keepCredential: item.hasCredential,
      isEnabled: item.isEnabled,
    })
}
function openRoute(item?: NotificationRoute) {
  closeEditor()
  editor.value = 'route'
  editingId.value = item?.id || null
  expectedVersion.value = item?.version ?? null
  if (item)
    Object.assign(routeForm, {
      channelId: item.channelId,
      sourceKind: item.sourceKind,
      minimumSeverity: item.minimumSeverity,
      includeRecovery: item.includeRecovery,
      isEnabled: item.isEnabled,
    })
}
async function save() {
  if (!authorized.value || saving.value || !editor.value) return
  const current = generation
  try {
    if (!intent.value) {
      await ElMessageBox.confirm(impact, '确认保存通知配置', {
        type: 'warning',
        confirmButtonText: '确认保存',
        cancelButtonText: '取消',
      })
      if (current !== generation || !authorized.value) return
      const kind = editor.value === 'channel' ? 'channels' : 'routes'
      const command =
        editor.value === 'channel'
          ? {
              ...channelForm,
              credentialReference:
                channelForm.keepCredential || !channelForm.credentialReference
                  ? null
                  : channelForm.credentialReference,
              confirmed: true,
              expectedVersion: expectedVersion.value,
            }
          : { ...routeForm, confirmed: true, expectedVersion: expectedVersion.value }
      intent.value = notificationIntent(
        editingId.value ? `${kind}/${editingId.value}` : kind,
        editingId.value ? 'PUT' : 'POST',
        command,
      )
    }
    saving.value = true
    await executeNotificationIntent(intent.value, controller.signal)
    if (current !== generation || !authorized.value) return
    closeEditor()
    ElMessage.success('配置已保存，不表示消息已发出')
    await load()
  } catch (reason) {
    if (reason !== 'cancel' && reason !== 'close' && current === generation) failure(reason)
  } finally {
    saving.value = false
  }
}
async function remove(kind: 'channels' | 'routes', item: NotificationChannel | NotificationRoute) {
  if (!authorized.value || saving.value) return
  const current = generation
  const key = `${kind}/${item.id}:${item.version}`
  try {
    await ElMessageBox.confirm(
      kind === 'channels'
        ? '有历史投递的渠道不能删除，请改为停用。没有历史的渠道及其路由将删除。'
        : impact,
      '确认删除',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
    if (current !== generation || !authorized.value) return
    saving.value = true
    let command = deleteIntents.get(key)
    if (!command) {
      command = notificationIntent(`${kind}/${item.id}`, 'DELETE', {
        expectedVersion: item.version,
        confirmed: true,
      })
      deleteIntents.set(key, command)
    }
    await executeNotificationIntent(command, controller.signal)
    if (current !== generation || !authorized.value) return
    deleteIntents.delete(key)
    ElMessage.success('已删除')
    await load()
  } catch (reason) {
    if (reason !== 'cancel' && reason !== 'close' && current === generation) failure(reason)
  } finally {
    saving.value = false
  }
}
function channelName(id: string) {
  return choices.value.find((item) => item.id === id)?.name || id
}
async function openAttempts(id: string) {
  if (!authorized.value) return
  const current = generation
  const selected = ++attemptRevision
  attempts.value = []
  attemptVisible.value = true
  attemptLoading.value = true
  try {
    const values = await apiRequest<NotificationAttempt[]>(
      `notifications/manage/deliveries/${encodeURIComponent(id)}/attempts`,
      { signal: controller.signal },
    )
    if (
      current === generation &&
      selected === attemptRevision &&
      authorized.value &&
      attemptVisible.value
    )
      attempts.value = values
  } catch (reason) {
    if (current === generation && selected === attemptRevision && authorized.value) failure(reason)
  } finally {
    if (current === generation && selected === attemptRevision) attemptLoading.value = false
  }
}
function closeAttempts() {
  attemptRevision++
  attempts.value = []
  attemptLoading.value = false
}
async function submitNotification(
  kind: 'test' | 'retry',
  item: NotificationChannel | NotificationDelivery,
) {
  if (!authorized.value || saving.value) return
  const current = generation
  const key = `${kind}:${item.id}:${item.version}`
  try {
    await ElMessageBox.confirm(
      kind === 'test'
        ? '将向已登记的批准目标发送一条固定测试消息；即使渠道停用也会发送，不修改启停状态。是否继续？'
        : '原消息可能已经送达！再次发送可能导致重复通知。此操作创建新记录，保留原记录；必须确认目标和渠道版本未变。',
      kind === 'test' ? '确认测试发送' : '确认可能重复送达',
      {
        type: 'warning',
        confirmButtonText: kind === 'test' ? '发送测试' : '接受风险并重试',
        cancelButtonText: '取消',
      },
    )
    if (current !== generation || !authorized.value) return
    saving.value = true
    let command = submissionIntents.get(key)
    if (!command) {
      command =
        kind === 'test'
          ? notificationIntent(`channels/${item.id}/test`, 'POST', {
              expectedChannelVersion: item.version,
              confirmed: true,
            })
          : notificationIntent(`deliveries/${item.id}/retry-unknown`, 'POST', {
              expectedVersion: item.version,
              expectedChannelVersion: (item as NotificationDelivery).channelVersion,
              confirmed: true,
              acceptDuplicateRisk: true,
            })
      submissionIntents.set(key, command)
    }
    const result = await executeNotificationIntent<NotificationSubmission>(
      command,
      controller.signal,
    )
    if (current !== generation || !authorized.value) return
    submissionIntents.delete(key)
    ElMessage.success(`已提交，尚未确认发送成功：${result.deliveryId.slice(0, 8)}`)
    active.value = 'deliveries'
    page.value = 1
    await load()
  } catch (reason) {
    if (reason !== 'cancel' && reason !== 'close' && current === generation) failure(reason)
  } finally {
    saving.value = false
  }
}
function date(value: string | null) {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—'
}
function changePage(delta: number) {
  page.value += delta
  void load()
}
watch([active, filterChannel, filterState], () => {
  page.value = 1
  void load()
})
watch(
  () => authSession.user.value?.isAdministrator,
  (value) => {
    if (value !== true) clearPrivate()
  },
)
onMounted(load)
onUnmounted(clearPrivate)
</script>

<template>
  <div class="notification-settings">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">NOTIFICATIONS</span>
        <h1>通知管理</h1>
        <p>管理渠道元数据、事件路由和投递状态。</p>
      </div>
      <el-button :disabled="!authorized || saving" :loading="loading" @click="load"
        >重新载入</el-button
      >
    </header>
    <el-alert
      title="外部发送需要私有配置"
      description="发送 Worker 已接入。请先在服务器私有配置中登记批准目标及凭据，再启用渠道。当前表单只保存引用标识；已接受不代表收件人已收到，结果未知不会自动重发。"
      type="info"
      :closable="false"
      show-icon
    />
    <el-alert
      v-if="error"
      :title="error"
      :description="traceId ? `追踪号：${traceId}` : undefined"
      type="error"
      :closable="false"
      show-icon
    />
    <section v-if="authorized" class="panel notification-panel" v-loading="loading">
      <el-tabs v-model="active"
        ><el-tab-pane label="渠道" name="channels" /><el-tab-pane
          label="路由"
          name="routes" /><el-tab-pane label="投递记录" name="deliveries"
      /></el-tabs>
      <div class="notification-toolbar">
        <el-button
          v-if="active !== 'deliveries'"
          type="primary"
          :disabled="saving"
          @click="active === 'channels' ? openChannel() : openRoute()"
          >{{ active === 'channels' ? '新增渠道' : '新增路由' }}</el-button
        >
        <el-select
          v-if="active !== 'channels'"
          v-model="filterChannel"
          clearable
          filterable
          allow-create
          placeholder="所有渠道（或输入完整 ID）"
          aria-label="筛选通知渠道"
          ><el-option v-for="item in choices" :key="item.id" :label="item.name" :value="item.id"
        /></el-select>
        <el-select
          v-if="active === 'deliveries'"
          v-model="filterState"
          clearable
          placeholder="所有状态"
          aria-label="筛选投递状态"
          ><el-option
            v-for="(label, value) in notificationStates"
            :key="value"
            :label="label"
            :value="value"
        /></el-select>
      </div>
      <el-table v-if="active === 'channels'" :data="channels" row-key="id" empty-text="暂无渠道">
        <el-table-column prop="name" label="名称" min-width="160" show-overflow-tooltip />
        <el-table-column prop="provider" label="类型" min-width="95" />
        <el-table-column
          prop="destinationReference"
          label="目标引用"
          min-width="145"
          show-overflow-tooltip
        />
        <el-table-column label="凭据" width="80"
          ><template #default="{ row }">{{
            row.hasCredential ? '已配置' : '未配置'
          }}</template></el-table-column
        >
        <el-table-column label="状态" width="80"
          ><template #default="{ row }"
            ><el-tag :type="row.isEnabled ? 'success' : 'info'">{{
              row.isEnabled ? '启用' : '停用'
            }}</el-tag></template
          ></el-table-column
        >
        <el-table-column label="操作" width="194"
          ><template #default="{ row }"
            ><el-button
              link
              type="primary"
              :disabled="saving"
              @click="submitNotification('test', row)"
              >测试</el-button
            ><el-button link type="primary" :disabled="saving" @click="openChannel(row)"
              >编辑</el-button
            ><el-button link type="danger" :disabled="saving" @click="remove('channels', row)"
              >删除</el-button
            ></template
          ></el-table-column
        >
      </el-table>
      <el-table v-else-if="active === 'routes'" :data="routes" row-key="id" empty-text="暂无路由">
        <el-table-column label="渠道" min-width="170" show-overflow-tooltip
          ><template #default="{ row }">{{ channelName(row.channelId) }}</template></el-table-column
        >
        <el-table-column prop="sourceKind" label="来源" width="90" />
        <el-table-column prop="minimumSeverity" label="最低级别" width="100" />
        <el-table-column label="包含恢复" width="100"
          ><template #default="{ row }">{{
            row.includeRecovery ? '是' : '否'
          }}</template></el-table-column
        >
        <el-table-column label="状态" width="80"
          ><template #default="{ row }">{{
            row.isEnabled ? '启用' : '停用'
          }}</template></el-table-column
        >
        <el-table-column label="操作" width="132"
          ><template #default="{ row }"
            ><el-button link type="primary" :disabled="saving" @click="openRoute(row)"
              >编辑</el-button
            ><el-button link type="danger" :disabled="saving" @click="remove('routes', row)"
              >删除</el-button
            ></template
          ></el-table-column
        >
      </el-table>
      <el-table v-else :data="deliveries" row-key="id" empty-text="暂无投递记录">
        <el-table-column label="渠道" min-width="180" show-overflow-tooltip
          ><template #default="{ row }">{{ channelName(row.channelId) }}</template></el-table-column
        >
        <el-table-column label="状态" width="110"
          ><template #default="{ row }">{{
            notificationStates[row.state] || row.state
          }}</template></el-table-column
        >
        <el-table-column prop="attemptCount" label="尝试次数" width="95" />
        <el-table-column label="创建时间" min-width="185"
          ><template #default="{ row }">{{ date(row.createdAtUtc) }}</template></el-table-column
        >
        <el-table-column label="最近尝试" min-width="185"
          ><template #default="{ row }">{{ date(row.lastAttemptAtUtc) }}</template></el-table-column
        >
        <el-table-column
          prop="lastResultCode"
          label="结果码"
          min-width="150"
          show-overflow-tooltip
        />
        <el-table-column label="记录" width="105"
          ><template #default="{ row }"
            ><el-button link type="primary" @click="openAttempts(row.id)"
              >尝试记录</el-button
            ></template
          ></el-table-column
        >
        <el-table-column label="人工重试" width="150"
          ><template #default="{ row }"
            ><el-button
              v-if="row.state === 'Unknown'"
              link
              type="warning"
              :disabled="saving || row.hasManualRetry"
              @click="submitNotification('retry', row)"
              >{{ row.hasManualRetry ? '已有重试记录' : '确认重试' }}</el-button
            ><span v-else>{{
              row.intentKind === 'ManualRetry'
                ? '人工重试记录'
                : row.intentKind === 'Test'
                  ? '测试发送'
                  : '—'
            }}</span></template
          ></el-table-column
        >
      </el-table>
      <div class="notification-pagination">
        <el-button :disabled="page <= 1 || loading || saving" @click="changePage(-1)"
          >上一页</el-button
        ><span>第 {{ page }} 页</span
        ><el-button :disabled="!hasMore || loading || saving" @click="changePage(1)"
          >下一页</el-button
        >
      </div>
    </section>
    <el-empty v-else description="需要管理员权限" />
    <el-drawer
      v-model="attemptVisible"
      title="投递尝试记录"
      size="min(560px, 100vw)"
      @closed="closeAttempts"
    >
      <el-alert
        title="仅显示固定结果码和时间，不记录地址、凭据或响应正文。"
        :closable="false"
        type="info"
      />
      <el-table :data="attempts" v-loading="attemptLoading" empty-text="暂无尝试记录">
        <el-table-column prop="attemptNumber" label="次数" width="70" />
        <el-table-column label="开始时间" min-width="185"
          ><template #default="{ row }">{{ date(row.startedAtUtc) }}</template></el-table-column
        >
        <el-table-column label="完成时间" min-width="185"
          ><template #default="{ row }">{{ date(row.finishedAtUtc) }}</template></el-table-column
        >
        <el-table-column label="结果" min-width="160"
          ><template #default="{ row }">{{
            row.resultCode || '等待结果'
          }}</template></el-table-column
        >
      </el-table>
    </el-drawer>
    <el-dialog
      :model-value="editor !== null"
      :title="editor === 'channel' ? '渠道配置' : '路由配置'"
      width="min(540px, calc(100vw - 24px))"
      :close-on-click-modal="false"
      :close-on-press-escape="!saving"
      :show-close="!saving"
      @close="closeEditor"
    >
      <el-alert
        v-if="intent"
        title="已提交的请求已冻结"
        description="响应可能丢失。重试使用相同请求与幂等键，不重复执行；修改配置请先重新载入核对。"
        type="warning"
        :closable="false"
      />
      <el-form label-position="top" :disabled="saving || intent !== null" @submit.prevent="save">
        <template v-if="editor === 'channel'">
          <el-form-item label="渠道名称"
            ><el-input v-model="channelForm.name" maxlength="80"
          /></el-form-item>
          <el-form-item label="渠道类型"
            ><el-select v-model="channelForm.provider"
              ><el-option
                v-for="provider in providers"
                :key="provider"
                :value="provider"
                :label="provider" /></el-select
          ></el-form-item>
          <el-form-item label="目标引用（不是 URL 或邮箱）"
            ><el-input
              v-model="channelForm.destinationReference"
              maxlength="64"
              placeholder="例如：operations-target"
          /></el-form-item>
          <el-form-item v-if="editingId" label="现有凭据"
            ><el-checkbox v-model="channelForm.keepCredential"
              >保留现有凭据（切换渠道类型时需取消）</el-checkbox
            ></el-form-item
          >
          <el-form-item v-if="!channelForm.keepCredential" label="凭据引用（不输入密钥，留空移除）"
            ><el-input
              v-model="channelForm.credentialReference"
              maxlength="64"
              autocomplete="off"
              placeholder="例如：operations-credential"
          /></el-form-item>
          <el-form-item label="启用渠道"
            ><el-switch v-model="channelForm.isEnabled"
          /></el-form-item>
        </template>
        <template v-else>
          <el-form-item label="目标渠道（已有路由不能更换渠道）"
            ><el-select
              v-model="routeForm.channelId"
              filterable
              allow-create
              :disabled="editingId !== null"
              ><el-option
                v-for="item in choices"
                :key="item.id"
                :value="item.id"
                :label="item.name" /></el-select
          ></el-form-item>
          <el-form-item label="事件来源"
            ><el-select v-model="routeForm.sourceKind"
              ><el-option label="所有管理员事件" value="all" /><el-option
                label="告警"
                value="Alert" /><el-option label="任务" value="Job" /></el-select
          ></el-form-item>
          <el-form-item label="最低级别"
            ><el-select v-model="routeForm.minimumSeverity"
              ><el-option
                v-for="value in ['Info', 'Warning', 'Error', 'Critical']"
                :key="value"
                :label="value"
                :value="value" /></el-select
          ></el-form-item>
          <el-form-item label="包含恢复事件"
            ><el-switch v-model="routeForm.includeRecovery"
          /></el-form-item>
          <el-form-item label="启用路由"><el-switch v-model="routeForm.isEnabled" /></el-form-item>
        </template>
      </el-form>
      <template #footer
        ><el-button :disabled="saving" @click="closeEditor">关闭</el-button
        ><el-button type="primary" :loading="saving" :disabled="!authorized" @click="save">{{
          intent ? '重试原请求' : '确认并保存'
        }}</el-button></template
      >
    </el-dialog>
  </div>
</template>

<style scoped>
.notification-settings {
  display: grid;
  gap: 16px;
  min-width: 0;
}
.notification-panel {
  padding: 16px;
  min-width: 0;
  overflow: hidden;
}
.notification-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-bottom: 16px;
}
.notification-toolbar .el-select {
  width: min(270px, 100%);
}
.notification-pagination {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 12px;
  padding-top: 16px;
}
:deep(.el-form .el-select) {
  width: 100%;
}
@media (max-width: 680px) {
  .notification-panel {
    padding: 12px;
  }
  .notification-pagination {
    justify-content: center;
  }
}
</style>
