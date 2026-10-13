<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { enqueueOperation, planOperation } from '@/services/operations'
import { apiRequest } from '@/services/apiClient'
import type { Job } from '@/services/contracts'
import { formatDuration } from '@/services/displayUnits'

const settingsJson = ref('{}')
const editableKeys = ref<string[]>([])
const loading = ref(false)
const submitting = ref(false)
const maintenanceVisible = ref(false)
const maintenancePhase = ref('正在提交 Docker 配置')
const maintenanceElapsed = ref(0)
async function load() {
  loading.value = true
  try {
    const settings = await apiRequest<{ settingsJson: string; editableKeys: string[] }>(
      'docker/settings',
    )
    settingsJson.value = JSON.stringify(JSON.parse(settings.settingsJson), null, 2)
    editableKeys.value = settings.editableKeys
  } finally {
    loading.value = false
  }
}
function parse() {
  const value = JSON.parse(settingsJson.value) as unknown
  if (!value || Array.isArray(value) || typeof value !== 'object')
    throw new Error('配置必须是 JSON 对象')
  return settingsJson.value
}
async function validate() {
  submitting.value = true
  try {
    const job = await enqueueOperation(
      'docker',
      'validate-settings',
      null,
      { settingsJson: parse() },
      false,
    )
    ElMessage.success(`校验任务已提交：${job.id}`)
  } finally {
    submitting.value = false
  }
}
async function apply() {
  submitting.value = true
  try {
    const parameters = { settingsJson: parse() }
    const plan = await planOperation('docker', 'apply-settings', null, parameters)
    await ElMessageBox.confirm(
      [
        ...plan.changes,
        ...plan.warnings,
        '应用期间 Docker 和 Whale Deck 容器会短暂不可用，宿主机维护页将持续显示状态；失败会自动恢复旧配置。',
      ].join('\n'),
      '应用 Docker 设置',
      { type: 'warning', confirmButtonText: '确认重启 Docker' },
    )
    const job = await enqueueOperation(
      'docker',
      'apply-settings',
      null,
      parameters,
      true,
      plan.planHash,
    )
    maintenanceVisible.value = true
    maintenancePhase.value = 'Docker 正在重启，等待容器启动中'
    const startedAt = Date.now()
    const deadline = startedAt + 360_000
    while (Date.now() < deadline) {
      maintenanceElapsed.value = Math.floor((Date.now() - startedAt) / 1000)
      try {
        const current = await apiRequest<Job>(`jobs/${encodeURIComponent(job.id)}`)
        maintenancePhase.value = current.phase || '等待容器启动中'
        if (current.state === 'Succeeded') {
          await load()
          ElMessage.success('Docker 设置已应用，平台容器已恢复')
          return
        }
        if (['Failed', 'Canceled', 'RolledBack'].includes(current.state)) {
          const terminalError = new Error(current.errorCode || `Docker 设置任务${current.state}`)
          terminalError.name = 'TerminalJobError'
          throw terminalError
        }
      } catch (error) {
        if (error instanceof Error && error.name === 'TerminalJobError') throw error
        maintenancePhase.value = '连接暂时中断，等待容器启动中'
      }
      await new Promise((resolve) => window.setTimeout(resolve, 2000))
    }
    throw new Error('等待 Docker 与平台容器恢复超时')
  } finally {
    maintenanceVisible.value = false
    submitting.value = false
  }
}
onMounted(load)
</script>
<template>
  <div class="docker-settings-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">DOCKER ENGINE</span>
        <h1>Docker 设置</h1>
        <p>仅允许受控字段；后端合并未知字段、原子备份并在重启失败时自动回滚。</p>
      </div>
    </header>
    <el-alert
      title="保存会重启 Docker。进行中请停留在维护加载页，直到 API 与核心依赖恢复健康。"
      type="warning"
      show-icon
      :closable="false"
    />
    <section class="panel editor-panel">
      <el-form label-position="top"
        ><el-form-item label="允许修改的 daemon.json 片段"
          ><el-input
            v-model="settingsJson"
            type="textarea"
            :rows="18"
            spellcheck="false" /></el-form-item
      ></el-form>
      <div class="editor-actions">
        <span class="editable-hint">允许字段：{{ editableKeys.join('、') }}</span>
        <el-button :loading="submitting || loading" @click="validate">只校验</el-button
        ><el-button type="primary" :loading="submitting || loading" @click="apply"
          >预检、保存并重启</el-button
        >
      </div>
    </section>
    <Teleport to="body">
      <div v-if="maintenanceVisible" class="maintenance-overlay" role="status" aria-live="polite">
        <div class="maintenance-overlay__card">
          <span class="maintenance-overlay__spinner" aria-hidden="true" />
          <h2>等待容器启动中</h2>
          <p>{{ maintenancePhase }}</p>
          <small>已等待 {{ formatDuration(maintenanceElapsed) }}，请勿关闭页面。</small>
        </div>
      </div>
    </Teleport>
  </div>
</template>
<style scoped>
.docker-settings-page {
  display: grid;
  gap: 18px;
}
.editor-panel {
  padding: 20px;
}
.editor-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}
.editable-hint {
  margin-right: auto;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.editor-panel :deep(textarea) {
  font-family: var(--whaledeck-font-family);
  line-height: 1.6;
}
.maintenance-overlay {
  position: fixed;
  z-index: 4000;
  inset: 0;
  display: grid;
  place-items: center;
  padding: 24px;
  background: color-mix(in srgb, var(--el-bg-color) 86%, transparent);
  backdrop-filter: blur(14px);
}
.maintenance-overlay__card {
  width: min(420px, 100%);
  padding: 32px;
  text-align: center;
  border: 1px solid var(--el-border-color-light);
  border-radius: 18px;
  background: var(--el-bg-color-overlay);
  box-shadow: var(--el-box-shadow-dark);
}
.maintenance-overlay__card h2 {
  margin: 18px 0 8px;
  font-size: 20px;
}
.maintenance-overlay__card p {
  color: var(--el-text-color-regular);
}
.maintenance-overlay__card small {
  color: var(--el-text-color-secondary);
}
.maintenance-overlay__spinner {
  display: inline-block;
  width: 44px;
  height: 44px;
  border: 3px solid var(--el-color-primary-light-7);
  border-top-color: var(--el-color-primary);
  border-radius: 50%;
  animation: maintenance-spin 0.8s linear infinite;
}
@keyframes maintenance-spin {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .maintenance-overlay__spinner {
    animation: none;
    border-top-color: var(--el-color-primary-light-7);
    background: var(--el-color-primary-light-9);
  }
}
</style>
