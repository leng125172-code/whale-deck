<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Link } from '@element-plus/icons-vue'
import { apiRequest } from '@/services/apiClient'
import type { ServiceEntry } from '@/services/contracts'
import { formatDuration } from '@/services/displayUnits'

const services = ref<ServiceEntry[]>([])
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    services.value = await apiRequest<ServiceEntry[]>('services')
  } finally {
    loading.value = false
  }
}

function open(service: ServiceEntry) {
  window.open(service.url, '_blank', 'noopener,noreferrer')
}

function statusType(status: string) {
  if (status === 'Healthy') return 'success'
  if (status === 'Degraded') return 'warning'
  return 'danger'
}

onMounted(load)
</script>

<template>
  <div class="services-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">SERVICES</span>
        <h1>内部服务</h1>
        <p>使用当前工作站入口打开 Whale Deck 与身份服务；地址不会固定到某一块网卡。</p>
      </div>
      <el-button :loading="loading" @click="load">刷新</el-button>
    </header>

    <section v-loading="loading" class="service-grid">
      <article v-for="service in services" :key="service.id" class="panel service-card">
        <div class="service-card__heading">
          <div class="service-card__icon">
            <el-icon><Link /></el-icon>
          </div>
          <div>
            <h2>{{ service.name }}</h2>
            <p>{{ service.description }}</p>
          </div>
          <el-tag :type="statusType(service.status)" effect="light">{{ service.status }}</el-tag>
        </div>
        <div class="service-card__meta">
          <code>{{ service.url }}</code>
          <span
            >延迟：{{
              service.latencyMilliseconds == null
                ? '—'
                : formatDuration(service.latencyMilliseconds / 1000)
            }}</span
          >
        </div>
        <el-button type="primary" plain @click="open(service)">打开服务</el-button>
      </article>
    </section>
  </div>
</template>

<style scoped>
.services-page,
.service-grid {
  display: grid;
  gap: 18px;
}
.service-grid {
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 360px), 1fr));
}
.service-card {
  display: grid;
  gap: 16px;
  padding: 20px;
}
.service-card__heading {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  gap: 12px;
  align-items: start;
}
.service-card__heading h2 {
  margin: 0;
  font-size: 16px;
}
.service-card__heading p {
  margin: 5px 0 0;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}
.service-card__icon {
  display: grid;
  width: 42px;
  height: 42px;
  place-items: center;
  border-radius: 12px;
  background: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
  font-size: 20px;
}
.service-card__meta {
  display: grid;
  gap: 6px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.service-card__meta code {
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--el-text-color-regular);
  white-space: nowrap;
}
</style>
