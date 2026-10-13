<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { ElPagination } from 'element-plus'
import { ApiError, apiRequest } from '@/services/apiClient'
import type { HostProcess, HostProcessPage } from '@/services/contracts'
import { formatBytes as memory, formatDuration, formatQuantity } from '@/services/displayUnits'

const result = ref<HostProcessPage | null>(null)
const loading = ref(false)
const error = ref('')
const traceId = ref('')
const filter = ref('')
const sort = ref('cpu')
const descending = ref(true)
const page = ref(1)
const pageSize = ref(50)
const automatic = ref(true)
const detailVisible = ref(false)
const detail = ref<HostProcess | null>(null)
const detailLoading = ref(false)
const detailError = ref('')
let timer: number | undefined
let generation = 0
let detailGeneration = 0

function invalidatePermission() {
  generation++
  detailGeneration++
  result.value = null
  detail.value = null
  detailVisible.value = false
  loading.value = false
  detailLoading.value = false
  automatic.value = false
}

async function load(reset = false) {
  if (reset) page.value = 1
  const current = ++generation
  loading.value = true
  const query = new URLSearchParams({
    page: String(page.value),
    pageSize: String(pageSize.value),
    sort: sort.value,
    descending: String(descending.value),
  })
  if (filter.value.trim()) query.set('filter', filter.value.trim())
  try {
    const snapshot = await apiRequest<HostProcessPage>(`host/processes?${query}`)
    if (current !== generation) return
    result.value = snapshot
    error.value = ''
    traceId.value = ''
  } catch (reason) {
    if (current !== generation) return
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
      invalidatePermission()
    }
    error.value = reason instanceof Error ? reason.message : '进程采样暂时不可用'
    traceId.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
  } finally {
    if (current === generation) loading.value = false
  }
}

async function openDetail(item: HostProcess) {
  const current = ++detailGeneration
  detail.value = null
  detailError.value = ''
  detailVisible.value = true
  detailLoading.value = true
  try {
    const value = await apiRequest<HostProcess>(
      `host/processes/${item.pid}?instanceId=${encodeURIComponent(item.instanceId)}`,
    )
    if (current === detailGeneration) detail.value = value
  } catch (reason) {
    if (current !== detailGeneration) return
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
      invalidatePermission()
      error.value = reason.message
      traceId.value = reason.problem.traceId || ''
      return
    }
    detailError.value =
      reason instanceof ApiError && reason.status === 404
        ? '进程已退出或 PID 已被复用，请刷新列表后重新选择。'
        : reason instanceof Error
          ? reason.message
          : '无法读取进程详情'
  } finally {
    if (current === detailGeneration) detailLoading.value = false
  }
}

function closeDetail() {
  detailGeneration++
  detailLoading.value = false
}
function cpu(value: number | null) {
  return formatQuantity(value, '%', 1)
}

onMounted(() => {
  void load()
  timer = window.setInterval(() => {
    if (automatic.value && !loading.value && !document.hidden && !detailVisible.value) void load()
  }, 6000)
})
onBeforeUnmount(() => {
  generation++
  detailGeneration++
  window.clearInterval(timer)
})
</script>

<template>
  <div class="process-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">HOST PROCESSES</span>
        <h1>主机进程</h1>
        <p>只读主机进程统计；CPU 100% 表示占满一个逻辑核心，不展示命令行或环境变量。</p>
      </div>
      <el-button :loading="loading" @click="load()">刷新</el-button>
    </header>
    <section class="panel process-filters">
      <el-input
        v-model="filter"
        maxlength="128"
        placeholder="名称或 PID"
        clearable
        @clear="load(true)"
        @keyup.enter="load(true)"
      />
      <el-select v-model="sort" aria-label="进程排序" @change="load(true)"
        ><el-option label="CPU 使用率" value="cpu" /><el-option
          label="内存占用"
          value="memory" /><el-option label="PID" value="pid" /><el-option
          label="进程名称"
          value="name"
      /></el-select>
      <el-select v-model="descending" aria-label="排序方向" @change="load(true)"
        ><el-option label="降序" :value="true" /><el-option label="升序" :value="false"
      /></el-select>
      <label class="auto-refresh"
        ><span>每 6 秒刷新</span><el-switch v-model="automatic" aria-label="自动刷新进程"
      /></label>
      <el-button type="primary" @click="load(true)">筛选</el-button>
    </section>
    <el-alert
      v-if="error"
      :title="error"
      :description="traceId ? `追踪号：${traceId}` : undefined"
      type="error"
      :closable="false"
      show-icon
    />
    <el-alert
      v-if="result?.isStale"
      title="进程采样已过期，当前显示最后一次有效快照。"
      type="warning"
      :closable="false"
      show-icon
    />
    <el-alert
      v-if="result?.isTruncated"
      title="进程数量超过采样上限，结果为部分主机进程。"
      type="warning"
      :closable="false"
      show-icon
    />
    <section class="panel process-table" v-loading="loading">
      <div class="process-summary">
        <span>{{ result?.total ?? '—' }} 个匹配进程</span
        ><span v-if="result"
          >采样 {{ new Date(result.sampledAtUtc).toLocaleTimeString() }} ·
          {{ formatDuration(result.cpuWindowSeconds) }}窗口<span v-if="result.skippedCount">
            · {{ result.skippedCount }} 个进程退出或无法读取</span
          ></span
        >
      </div>
      <el-table
        :data="result?.items ?? []"
        row-key="instanceId"
        empty-text="没有匹配的进程"
        @row-click="openDetail"
      >
        <el-table-column prop="pid" label="PID" width="95" /><el-table-column
          prop="name"
          label="进程名称"
          min-width="190"
          show-overflow-tooltip
        /><el-table-column prop="userId" label="UID" width="95" /><el-table-column
          prop="state"
          label="状态"
          width="90"
        />
        <el-table-column label="CPU" width="110"
          ><template #default="scope">{{ cpu(scope.row.cpuPercent) }}</template></el-table-column
        >
        <el-table-column label="常驻内存" width="140"
          ><template #default="scope">{{
            memory(scope.row.memoryBytes)
          }}</template></el-table-column
        >
        <el-table-column prop="threadCount" label="线程" width="95" /><el-table-column
          label="操作"
          width="85"
          fixed="right"
          ><template #default="scope"
            ><el-button link type="primary" @click.stop="openDetail(scope.row)"
              >详情</el-button
            ></template
          ></el-table-column
        >
      </el-table>
      <div class="process-pagination">
        <ElPagination
          v-model:current-page="page"
          :page-size="pageSize"
          :total="result?.total ?? 0"
          layout="prev, pager, next"
          :pager-count="5"
          @current-change="load()"
        /><el-select v-model="pageSize" aria-label="每页进程数量" @change="load(true)"
          ><el-option
            v-for="size in [25, 50, 100]"
            :key="size"
            :label="`${size} / 页`"
            :value="size"
        /></el-select>
      </div>
    </section>
    <el-drawer
      v-model="detailVisible"
      title="进程详情（只读）"
      size="min(560px, 94vw)"
      @closed="closeDetail"
    >
      <div v-loading="detailLoading" class="process-detail">
        <p class="detail-sampling-note">CPU 使用率来自最近一次采样窗口，其余详情为当前读取。</p>
        <el-alert
          v-if="detailError"
          :title="detailError"
          type="error"
          show-icon
          :closable="false"
        />
        <el-descriptions v-if="detail" :column="1" border>
          <el-descriptions-item label="名称">{{ detail.name }}</el-descriptions-item
          ><el-descriptions-item label="PID / 父 PID"
            >{{ detail.pid }} / {{ detail.parentPid }}</el-descriptions-item
          ><el-descriptions-item label="UID / 状态"
            >{{ detail.userId }} / {{ detail.state }}</el-descriptions-item
          >
          <el-descriptions-item label="启动时间">{{
            new Date(detail.startedAtUtc).toLocaleString()
          }}</el-descriptions-item
          ><el-descriptions-item label="CPU 使用率">{{
            cpu(detail.cpuPercent)
          }}</el-descriptions-item
          ><el-descriptions-item label="累计 CPU 时间">{{
            formatDuration(detail.cpuTimeSeconds)
          }}</el-descriptions-item
          ><el-descriptions-item label="常驻 / 虚拟内存"
            >{{ memory(detail.memoryBytes) }} /
            {{ memory(detail.virtualMemoryBytes) }}</el-descriptions-item
          ><el-descriptions-item label="线程数">{{ detail.threadCount }}</el-descriptions-item>
        </el-descriptions>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.process-page {
  display: grid;
  gap: 18px;
}
.process-filters {
  display: grid;
  grid-template-columns: minmax(180px, 1fr) 170px 100px auto auto;
  gap: 10px;
  padding: 14px;
}
.auto-refresh {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
}
.process-table {
  padding: 14px;
}
.process-summary,
.process-pagination {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 12px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.process-summary {
  padding-bottom: 12px;
}
.process-pagination {
  margin-top: 16px;
}
.process-pagination .el-select {
  width: 110px;
}
.process-detail {
  min-height: 150px;
  overflow-wrap: anywhere;
}
.detail-sampling-note {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.process-detail :deep(.el-descriptions__label) {
  width: 112px;
  min-width: 112px;
}
@container page (max-width: 800px) {
  .process-filters {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
@container page (max-width: 480px) {
  .process-filters {
    grid-template-columns: 1fr;
  }
}
</style>
