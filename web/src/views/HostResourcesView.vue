<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RefreshRight } from '@element-plus/icons-vue'
import { ApiError, apiRequest } from '@/services/apiClient'
import type { ManagedResource } from '@/services/contracts'
import { formatBytes as bytes, formatBitRate } from '@/services/displayUnits'

const loading = ref(false)
const error = ref('')
const traceId = ref('')
const interfaces = ref<ManagedResource[]>([])
const storage = ref<ManagedResource[]>([])
const showVirtualInterfaces = ref(false)
const visibleInterfaces = computed(() =>
  showVirtualInterfaces.value
    ? interfaces.value
    : interfaces.value.filter((item) => item.attributes.isVirtual !== 'true'),
)

function speed(value: string) {
  return Number(value) > 0 ? formatBitRate(value) : '—'
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    ;[interfaces.value, storage.value] = await Promise.all([
      apiRequest<ManagedResource[]>('host/network-interfaces'),
      apiRequest<ManagedResource[]>('host/storage-devices'),
    ])
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '读取主机设备失败'
    traceId.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="host-resources-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">HOST</span>
        <h1>主机设备</h1>
        <p>查看物理网卡、USB 网卡、磁盘与文件系统的实时状态。</p>
      </div>
      <el-button :loading="loading" @click="load"
        ><el-icon><RefreshRight /></el-icon>刷新</el-button
      >
    </header>
    <el-alert
      v-if="error"
      :title="error"
      type="error"
      :description="traceId ? `追踪号：${traceId}` : undefined"
      show-icon
      :closable="false"
    />
    <section class="panel resource-section" v-loading="loading">
      <div class="section-heading">
        <h2>网络接口</h2>
        <label class="virtual-toggle"
          ><span>显示虚拟接口</span><el-switch v-model="showVirtualInterfaces"
        /></label>
      </div>
      <el-table :data="visibleInterfaces" row-key="id">
        <el-table-column prop="name" label="接口" min-width="150" />
        <el-table-column label="状态" width="100">
          <template #default="scope"
            ><el-tag :type="scope.row.state === 'Up' ? 'success' : 'info'" effect="plain">{{
              scope.row.state
            }}</el-tag></template
          >
        </el-table-column>
        <el-table-column label="地址" min-width="250" show-overflow-tooltip>
          <template #default="scope">{{ scope.row.attributes.addresses || '—' }}</template>
        </el-table-column>
        <el-table-column label="类型" min-width="130">
          <template #default="scope">{{ scope.row.attributes.type || '—' }}</template>
        </el-table-column>
        <el-table-column label="速率" width="120">
          <template #default="scope">{{ speed(scope.row.version) }}</template>
        </el-table-column>
        <el-table-column label="累计收 / 发" min-width="180">
          <template #default="scope"
            >{{ bytes(scope.row.attributes.bytesReceived) }} /
            {{ bytes(scope.row.attributes.bytesSent) }}</template
          >
        </el-table-column>
      </el-table>
    </section>
    <section class="panel resource-section" v-loading="loading">
      <div class="section-heading">
        <h2>存储设备</h2>
        <span>{{ storage.length }} 个设备与分区</span>
      </div>
      <el-table :data="storage" row-key="id">
        <el-table-column prop="name" label="设备" min-width="150" />
        <el-table-column prop="type" label="类型" width="120" />
        <el-table-column label="型号 / 挂载点" min-width="240" show-overflow-tooltip>
          <template #default="scope">{{
            scope.row.attributes.model || scope.row.attributes.mountPoints || '—'
          }}</template>
        </el-table-column>
        <el-table-column label="容量" width="120">
          <template #default="scope">{{ bytes(scope.row.attributes.sizeBytes) }}</template>
        </el-table-column>
        <el-table-column label="介质" width="100">
          <template #default="scope">{{
            scope.row.attributes.rotational === 'true'
              ? 'HDD'
              : scope.row.type === 'BlockDevice'
                ? 'SSD / NVMe'
                : '—'
          }}</template>
        </el-table-column>
        <el-table-column label="SMART" width="120">
          <template #default="scope">
            <el-tag
              v-if="scope.row.type === 'BlockDevice'"
              :type="
                scope.row.attributes.smartPassed === 'true'
                  ? 'success'
                  : scope.row.attributes.smartPassed === 'false'
                    ? 'danger'
                    : 'info'
              "
              effect="plain"
              >{{
                scope.row.attributes.smartPassed === 'true'
                  ? '健康'
                  : scope.row.attributes.smartPassed === 'false'
                    ? '异常'
                    : '未知'
              }}</el-tag
            >
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column label="温度" width="90">
          <template #default="scope">{{
            scope.row.attributes.temperatureCelsius
              ? `${scope.row.attributes.temperatureCelsius} °C`
              : '—'
          }}</template>
        </el-table-column>
      </el-table>
    </section>
  </div>
</template>

<style scoped>
.host-resources-page {
  display: grid;
  gap: 18px;
}
.resource-section {
  padding: 14px;
  min-width: 0;
}
.section-heading {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  padding: 4px 6px 14px;
}
.section-heading h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 500;
}
.section-heading span {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.virtual-toggle {
  display: flex;
  align-items: center;
  gap: 8px;
}
</style>
