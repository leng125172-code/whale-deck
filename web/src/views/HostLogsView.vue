<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RefreshRight, Search } from '@element-plus/icons-vue'
import { ApiError, apiRequest } from '@/services/apiClient'
import type { JournalEntry } from '@/services/contracts'

const loading = ref(false)
const error = ref('')
const traceId = ref('')
const entries = ref<JournalEntry[]>([])
const unit = ref('')
const priority = ref('')
const sinceMinutes = ref(1440)
const keyword = ref('')

const units = [
  { label: '全部批准服务', value: '' },
  { label: 'Docker', value: 'docker.service' },
  { label: 'Whale Deck Agent', value: 'whaledeck-agent.service' },
  { label: 'Whale Deck 维护入口', value: 'whaledeck-maintenance.service' },
  { label: '工作站自动更新', value: 'database-platform-workstation-update.service' },
]

const priorityLabels: Record<string, string> = {
  '0': '紧急',
  '1': '警报',
  '2': '严重',
  '3': '错误',
  '4': '警告',
  '5': '通知',
  '6': '信息',
  '7': '调试',
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    const query = new URLSearchParams({ take: '300', sinceMinutes: String(sinceMinutes.value) })
    if (unit.value) query.set('unit', unit.value)
    if (priority.value) query.set('priority', priority.value)
    if (keyword.value.trim()) query.set('keyword', keyword.value.trim())
    entries.value = await apiRequest<JournalEntry[]>(`host/journal?${query}`)
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '读取系统日志失败'
    traceId.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="host-logs-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">JOURNALD</span>
        <h1>系统日志</h1>
        <p>仅查询批准服务，限制时间、条数和单条消息长度。</p>
      </div>
    </header>
    <section class="panel filter-panel">
      <el-select v-model="unit" aria-label="日志来源" placeholder="日志来源">
        <el-option
          v-for="item in units"
          :key="item.value"
          :label="item.label"
          :value="item.value"
        />
      </el-select>
      <el-select v-model="priority" aria-label="日志级别" placeholder="全部级别">
        <el-option label="全部级别" value="" />
        <el-option label="错误及以上" value="err" />
        <el-option label="警告及以上" value="warning" />
        <el-option label="信息及以上" value="info" />
        <el-option label="调试及以上" value="debug" />
      </el-select>
      <el-select v-model="sinceMinutes" aria-label="时间范围">
        <el-option label="最近 1 小时" :value="60" />
        <el-option label="最近 24 小时" :value="1440" />
        <el-option label="最近 7 天" :value="10080" />
      </el-select>
      <el-input v-model="keyword" clearable placeholder="筛选消息关键字" @keyup.enter="load">
        <template #prefix
          ><el-icon><Search /></el-icon
        ></template>
      </el-input>
      <el-button type="primary" :loading="loading" @click="load"
        ><el-icon><RefreshRight /></el-icon>查询</el-button
      >
    </section>
    <el-alert
      v-if="error"
      :title="error"
      type="error"
      :description="traceId ? `追踪号：${traceId}` : undefined"
      show-icon
      :closable="false"
    />
    <section class="panel logs-panel" v-loading="loading">
      <el-table :data="entries" row-key="occurredAtUtc" empty-text="当前筛选条件下没有日志">
        <el-table-column label="时间" width="180">
          <template #default="scope">{{
            new Date(scope.row.occurredAtUtc).toLocaleString()
          }}</template>
        </el-table-column>
        <el-table-column label="级别" width="80">
          <template #default="scope"
            ><el-tag effect="plain">{{
              priorityLabels[scope.row.priority] || scope.row.priority
            }}</el-tag></template
          >
        </el-table-column>
        <el-table-column prop="unit" label="来源" min-width="190" show-overflow-tooltip />
        <el-table-column label="进程" min-width="170">
          <template #default="scope">
            <div class="journal-process">
              <span class="journal-process__name" :title="scope.row.process || '—'">{{
                scope.row.process || '—'
              }}</span>
              <span class="journal-process__pid">PID {{ scope.row.processId || '—' }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="message" label="消息" min-width="420" show-overflow-tooltip />
      </el-table>
    </section>
  </div>
</template>

<style scoped>
.host-logs-page {
  display: grid;
  gap: 18px;
}
.filter-panel {
  display: grid;
  grid-template-columns: 180px 150px 150px minmax(220px, 1fr) auto;
  gap: 10px;
  padding: 14px;
}
.logs-panel {
  padding: 14px;
  min-height: 360px;
}
.journal-process {
  display: grid;
  min-width: 0;
  gap: 2px;
  line-height: 1.5;
}
.journal-process__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.journal-process__pid {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  white-space: nowrap;
}
@container page (max-width: 860px) {
  .filter-panel {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
@container page (max-width: 520px) {
  .filter-panel {
    grid-template-columns: 1fr;
  }
}
</style>
