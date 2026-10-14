<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import type { AlertEvent } from '@/services/contracts'
import type { InboxMessage, NotificationInbox } from '@/services/notificationInbox'

const visible = defineModel<boolean>({ default: false })
const props = defineProps<{
  inbox: NotificationInbox
  administrator: boolean
  alerts: AlertEvent[]
  activitiesLoading: boolean
}>()
const emit = defineEmits<{ refreshActivities: [] }>()
const tab = ref('messages')
const router = useRouter()
const status = computed({
  get: () => props.inbox.state.status,
  set: (value: 'all' | 'unread') => props.inbox.setFilters(value, props.inbox.state.kind),
})
const kind = computed({
  get: () => props.inbox.state.kind,
  set: (value: 'all' | 'Alert' | 'Job') => props.inbox.setFilters(props.inbox.state.status, value),
})

watch(visible, (open) => {
  if (open) {
    tab.value = 'messages'
    void props.inbox.load()
    void props.inbox.refreshSummary(true)
  } else props.inbox.cancelView()
})
watch(
  () => [props.inbox.state.status, props.inbox.state.kind],
  () => {
    if (visible.value) void props.inbox.load()
  },
)
watch(tab, (value) => {
  if (value === 'activities' && props.administrator) emit('refreshActivities')
})
watch(
  () => props.administrator,
  () => {
    tab.value = 'messages'
  },
)

function date(value: string) {
  const result = new Date(value)
  return Number.isFinite(result.getTime()) ? result.toLocaleString() : '时间未知'
}
function severity(value: string) {
  return value === 'Critical' ? 'danger' : value === 'Warning' ? 'warning' : 'info'
}
async function openSource(item: InboxMessage) {
  // Only known local management routes, never an arbitrary server-supplied redirect.
  if (!props.administrator || !item.targetPath || !/^\/(alerts|jobs)(\?|$)/.test(item.targetPath))
    return
  visible.value = false
  await router.push(item.targetPath)
}
function reloadMessages() {
  void props.inbox.load()
  void props.inbox.refreshSummary(true)
}
async function openAlerts() {
  if (!props.administrator) return
  visible.value = false
  await router.push('/alerts')
}
</script>

<template>
  <el-drawer v-model="visible" title="消息中心" size="min(520px, 96vw)" class="notification-drawer">
    <el-tabs v-model="tab" stretch>
      <el-tab-pane label="消息" name="messages">
        <div class="inbox-toolbar">
          <el-radio-group
            v-model="status"
            :disabled="inbox.state.writing"
            size="small"
            aria-label="消息已读筛选"
          >
            <el-radio-button value="all">全部</el-radio-button>
            <el-radio-button value="unread">未读</el-radio-button>
          </el-radio-group>
          <el-select
            v-model="kind"
            :disabled="inbox.state.writing"
            size="small"
            aria-label="消息类型"
            class="inbox-kind"
          >
            <el-option label="全部类型" value="all" />
            <el-option label="告警" value="Alert" />
            <el-option label="任务" value="Job" />
          </el-select>
          <el-button
            size="small"
            :loading="inbox.state.writing"
            :disabled="inbox.state.loading || inbox.state.unreadCount === 0"
            @click="inbox.markAllRead()"
            >全部已读</el-button
          >
        </div>
        <el-alert
          v-if="inbox.state.summaryUnavailable"
          title="未读数量暂时不可用，消息列表仍可重试。"
          type="warning"
          :closable="false"
        />
        <el-alert
          v-if="inbox.state.error"
          :title="inbox.state.error"
          type="error"
          :closable="false"
          show-icon
        />
        <div
          v-loading="inbox.state.loading"
          class="inbox-list"
          aria-live="polite"
          :aria-busy="inbox.state.loading || inbox.state.writing"
        >
          <article
            v-for="item in inbox.state.items"
            :key="item.id"
            class="inbox-message"
            :class="{ 'inbox-message--unread': !item.readAtUtc }"
            data-test="inbox-message"
          >
            <div class="inbox-message__heading">
              <strong>{{ item.title }}</strong>
              <el-tag size="small" :type="severity(item.severity)">{{
                item.sourceKind === 'Alert'
                  ? '告警'
                  : item.sourceKind === 'NotificationTest'
                    ? '通知测试'
                    : '任务'
              }}</el-tag>
            </div>
            <p>{{ item.summary }}</p>
            <small>{{ date(item.occurredAtUtc) }} · {{ item.readAtUtc ? '已读' : '未读' }}</small>
            <div class="inbox-message__actions">
              <el-button
                text
                size="small"
                :disabled="inbox.state.writing || inbox.state.loading"
                @click="inbox.setRead(item, !item.readAtUtc)"
                >{{ item.readAtUtc ? '标为未读' : '标为已读' }}</el-button
              >
              <el-button
                v-if="administrator && item.targetPath"
                text
                type="primary"
                size="small"
                @click="openSource(item)"
                >查看详情</el-button
              >
            </div>
          </article>
          <el-empty
            v-if="!inbox.state.loading && !inbox.state.error && inbox.state.items.length === 0"
            :description="inbox.state.status === 'unread' ? '没有未读消息' : '暂无消息'"
            :image-size="64"
          />
        </div>
        <div class="inbox-footer">
          <el-button
            v-if="inbox.state.error"
            :disabled="inbox.state.writing"
            @click="reloadMessages"
            >重试</el-button
          >
          <el-button
            v-else-if="inbox.state.nextBefore !== null"
            :loading="inbox.state.loading"
            :disabled="inbox.state.writing"
            @click="inbox.load(true)"
            >加载更多</el-button
          >
          <el-button
            v-else
            :disabled="inbox.state.loading || inbox.state.writing"
            @click="reloadMessages"
            >刷新消息</el-button
          >
        </div>
      </el-tab-pane>
      <el-tab-pane v-if="administrator" label="活动告警" name="activities">
        <div v-loading="activitiesLoading" class="inbox-list">
          <el-empty v-if="alerts.length === 0" description="当前没有活动告警" :image-size="64" />
          <button
            v-for="item in alerts"
            :key="item.id"
            class="inbox-activity"
            type="button"
            @click="openAlerts"
          >
            <span
              ><strong>{{ item.summaryCode }}</strong
              ><small>{{ item.state }} · {{ date(item.lastOccurredAtUtc) }}</small></span
            >
            <el-tag :type="severity(item.severity)" size="small">{{ item.severity }}</el-tag>
          </button>
        </div>
      </el-tab-pane>
    </el-tabs>
  </el-drawer>
</template>

<style scoped>
.inbox-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 16px;
  align-items: center;
}
.inbox-kind {
  width: 116px;
}
.inbox-list {
  display: grid;
  gap: 10px;
  min-height: 90px;
}
.inbox-message,
.inbox-activity {
  min-width: 0;
  padding: 12px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
  background: var(--el-fill-color-blank);
}
.inbox-message--unread {
  border-color: var(--el-color-primary-light-5);
  background: var(--el-color-primary-light-9);
}
.inbox-message__heading,
.inbox-activity {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
}
.inbox-message strong,
.inbox-activity strong {
  overflow-wrap: anywhere;
  color: var(--el-text-color-primary);
  font-size: 14px;
}
.inbox-message p {
  margin: 8px 0;
  font-size: 13px;
  color: var(--el-text-color-regular);
  overflow-wrap: anywhere;
}
.inbox-message small,
.inbox-activity small {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  overflow-wrap: anywhere;
}
.inbox-message__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  margin-top: 6px;
}
.inbox-message__actions .el-button + .el-button {
  margin-left: 4px;
}
.inbox-footer {
  display: flex;
  justify-content: center;
  margin-top: 16px;
}
.inbox-activity {
  width: 100%;
  text-align: left;
  cursor: pointer;
}
.inbox-activity > span {
  display: grid;
  gap: 4px;
  min-width: 0;
}
</style>
