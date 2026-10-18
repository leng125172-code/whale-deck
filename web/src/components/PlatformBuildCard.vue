<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { loadPlatformBuild, type BuildIdentity } from '@/services/platformBuild'

const api = ref<BuildIdentity | null>(null)
const gateway = ref<BuildIdentity | null>(null)
const loading = ref(true)
const controller = new AbortController()
const mismatch = computed(
  () =>
    api.value?.sourceRevision &&
    gateway.value?.sourceRevision &&
    api.value.sourceRevision !== gateway.value.sourceRevision,
)
onMounted(async () => {
  try {
    const result = await loadPlatformBuild(controller.signal)
    if (!controller.signal.aborted) {
      api.value = result.api
      gateway.value = result.gateway
    }
  } finally {
    if (!controller.signal.aborted) loading.value = false
  }
})
onBeforeUnmount(() => controller.abort())
</script>

<template>
  <article class="panel build-card" aria-label="实际构建版本" :aria-busy="loading">
    <h2>实际构建版本</h2>
    <p>读取程序与镜像内的构建标记，不使用环境配置冒充实际版本。</p>
    <p v-if="loading" role="status">正在读取版本…</p>
    <dl v-else>
      <template
        v-for="row in [
          { name: 'API 程序集', value: api },
          { name: '网页镜像', value: gateway },
        ]"
        :key="row.name"
      >
        <dt>{{ row.name }}</dt>
        <dd>
          <template v-if="row.value">
            <span>V{{ row.value.version }}</span>
            <code :title="row.value.sourceRevision || undefined">{{
              row.value.sourceRevision?.slice(0, 12) || '未登记源码提交'
            }}</code>
          </template>
          <span v-else>未能读取版本</span>
        </dd>
      </template>
    </dl>
    <el-alert
      v-if="mismatch"
      title="API 与网页源码提交不一致，可能仍在更新中。"
      type="warning"
      :closable="false"
    />
    <p class="build-card__note">
      此处不代表 Worker、Agent 或依赖服务已经同步；完整核验由部署验证完成。
    </p>
  </article>
</template>

<style scoped>
.build-card {
  min-width: 0;
  padding: 22px;
}
h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 400;
}
p {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.7;
}
dl {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 2fr);
  gap: 12px;
  font-size: 13px;
}
dt,
dd {
  min-width: 0;
  margin: 0;
  overflow-wrap: anywhere;
}
dd {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
code {
  color: var(--el-color-primary);
}
.build-card__note {
  margin-bottom: 0;
}
@media (max-width: 440px) {
  dl {
    grid-template-columns: minmax(0, 1fr);
    gap: 8px;
  }
  dd {
    margin-bottom: 8px;
  }
}
</style>
