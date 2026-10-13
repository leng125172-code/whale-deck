<script setup lang="ts">
import { computed } from 'vue'
import type { ContainerUsage } from '@/services/contracts'
import { formatBytes as bytes, formatDuration, formatQuantity } from '@/services/displayUnits'

const props = defineProps<{ sample?: ContainerUsage; state: string; stale: boolean }>()
const unavailable = computed(() =>
  !['running', 'paused'].includes(props.state)
    ? '未运行'
    : props.stale
      ? '采样已过期'
      : !props.sample
        ? '等待采样'
        : props.sample.quality === 'Unavailable'
          ? '采样不可用'
          : '',
)
const cpu = computed(() =>
  props.sample?.cpuPercent === null || props.sample?.cpuPercent === undefined
    ? '预热'
    : formatQuantity(props.sample.cpuPercent, '%'),
)
const memory = computed(
  () => `${bytes(props.sample?.memoryUsedBytes)} / ${bytes(props.sample?.memoryLimitBytes)}`,
)
</script>

<template>
  <div class="container-usage" :class="{ 'container-usage--unavailable': unavailable }">
    <span v-if="unavailable" class="container-usage__status">{{ unavailable }}</span>
    <el-tooltip
      v-else
      placement="top"
      effect="light"
      :enterable="false"
      popper-class="container-usage-popper"
      :show-after="250"
      :trigger="['hover', 'focus']"
    >
      <template #content>
        <div class="container-usage__tooltip">
          <span>CPU 100% 为一个逻辑核心 · {{ formatDuration(sample?.cpuWindowSeconds) }}窗口</span>
          <span>内存已扣除可回收文件缓存 · {{ formatQuantity(sample?.memoryPercent, '%') }}</span>
          <span
            >累计接收 / 发送：{{ bytes(sample?.networkReceiveBytes) }} /
            {{ bytes(sample?.networkSendBytes) }}</span
          >
          <span
            >累计读取 / 写入：{{ bytes(sample?.blockReadBytes) }} /
            {{ bytes(sample?.blockWriteBytes) }}</span
          >
          <span>进程数：{{ sample?.pids ?? '—' }}</span>
          <span
            >采样：{{
              sample?.sampledAtUtc ? new Date(sample.sampledAtUtc).toLocaleTimeString() : '—'
            }}</span
          >
        </div>
      </template>
      <div
        class="container-usage__values"
        tabindex="0"
        aria-label="容器 CPU 和内存，悬停或聚焦查看 I/O 详情"
      >
        <span
          >CPU <strong>{{ cpu }}</strong></span
        >
        <span :title="memory"
          >内存 <strong>{{ memory }}</strong></span
        >
      </div>
    </el-tooltip>
  </div>
</template>

<style scoped>
.container-usage {
  min-width: 0;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}
.container-usage__values,
.container-usage__tooltip {
  display: grid;
  gap: 4px;
}
:global(.container-usage-popper) {
  pointer-events: none;
}
.container-usage__values > span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.container-usage strong {
  color: var(--el-text-color-regular);
  font-weight: 400;
}
.container-usage__status {
  color: var(--el-text-color-placeholder);
}
.container-usage__tooltip {
  max-width: min(420px, 80vw);
  overflow-wrap: anywhere;
  white-space: normal;
}
</style>
