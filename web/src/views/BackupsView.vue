<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import ApplicationBackups from '@/components/applications/ApplicationBackups.vue'
import { enqueueOperation } from '@/services/operations'
import type { BackupPolicy, ManagedResource } from '@/services/contracts'

const items = ref<BackupPolicy[]>([])
const databases = ref<ManagedResource[]>([])
const loading = ref(false)
const dialogVisible = ref(false)
const editing = ref<BackupPolicy | null>(null)
const error = ref('')
const saving = ref(false)
const archivesVisible = ref(false)
const archivesResourceId = ref('')
const application = computed(() => form.instanceResourceId.startsWith('application:'))
let generation = 0
function archives(id: string) {
  archivesResourceId.value = id
  archivesVisible.value = true
}
const form = reactive({
  instanceResourceId: '',
  isEnabled: true,
  scheduleExpression: '0 2 * * *',
  timezone: 'Asia/Shanghai',
  retentionCount: 14,
  retentionDays: 14,
  targetDirectoryId: 'database-platform.backup.hdd',
  compression: 'zstd',
  verifyAfterBackup: true,
  consistency: 'quiesced',
  capacityWarningPercent: 80,
  capacityCriticalPercent: 90,
})
async function load() {
  const current = ++generation
  loading.value = true
  error.value = ''
  try {
    const [policies, databaseResources, applications] = await Promise.all([
      apiRequest<BackupPolicy[]>('backups/policies'),
      apiRequest<ManagedResource[]>('databases'),
      apiRequest<ManagedResource[]>('applications'),
    ])
    if (current !== generation) return
    if (
      !Array.isArray(policies) ||
      !Array.isArray(databaseResources) ||
      !Array.isArray(applications)
    )
      throw new Error('备份策略响应格式无效。')
    items.value = policies
    databases.value = [
      ...databaseResources,
      ...applications.filter(
        (item) =>
          item.type === 'ComposeApplication' &&
          !item.isProtected &&
          item.id.startsWith('application:'),
      ),
    ]
  } catch (reason) {
    if (current !== generation) return
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) clearPrivate()
    error.value = reason instanceof Error ? reason.message : '无法加载备份策略。'
  } finally {
    if (current === generation) loading.value = false
  }
}
function open(item?: BackupPolicy) {
  editing.value = item || null
  Object.assign(
    form,
    item
      ? { ...item, consistency: item.consistency || 'quiesced' }
      : {
          instanceResourceId: '',
          isEnabled: true,
          scheduleExpression: '0 2 * * *',
          timezone: 'Asia/Shanghai',
          retentionCount: 14,
          retentionDays: 14,
          targetDirectoryId: 'database-platform.backup.hdd',
          compression: 'zstd',
          verifyAfterBackup: true,
          consistency: 'quiesced',
          capacityWarningPercent: 80,
          capacityCriticalPercent: 90,
        },
  )
  dialogVisible.value = true
}
async function save() {
  if (saving.value) return
  const current = generation
  try {
    saving.value = true
    if (application.value && form.isEnabled)
      await ElMessageBox.confirm(
        '确认启用应用自动备份及旧备份清理？暂停归档会临时暂停服务；旧归档按天数/份数清理，至少保留最新一份，运行数据不受影响。',
        '启用应用备份策略',
        { type: 'warning', confirmButtonText: '确认启用', cancelButtonText: '取消' },
      )
    if (current !== generation) return
    const payload = {
      id: editing.value?.id || null,
      ...form,
      expectedVersion: editing.value?.version ?? null,
      confirmed: application.value && form.isEnabled,
    }
    await apiRequest<BackupPolicy>('backups/policies', {
      method: 'POST',
      body: JSON.stringify(payload),
    })
    dialogVisible.value = false
    ElMessage.success('备份策略已保存')
    await load()
  } catch (reason) {
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) clearPrivate()
    if (reason !== 'cancel' && reason !== 'close')
      ElMessage.error(reason instanceof Error ? reason.message : '保存失败')
  } finally {
    saving.value = false
  }
}
async function run(item: BackupPolicy) {
  if (item.instanceResourceId.startsWith('application:')) {
    archives(item.instanceResourceId)
    return
  }
  const job = await enqueueOperation('backups', 'run', item.instanceResourceId, {}, false)
  ElMessage.success(`备份任务已提交：${job.id}`)
}
async function verify(item: BackupPolicy) {
  if (item.instanceResourceId.startsWith('application:')) {
    archives(item.instanceResourceId)
    return
  }
  const job = await enqueueOperation('backups', 'verify', item.instanceResourceId, {}, false)
  ElMessage.success(`校验任务已提交：${job.id}`)
}
onMounted(load)
function clearPrivate() {
  generation++
  items.value = []
  databases.value = []
  archivesVisible.value = false
  archivesResourceId.value = ''
  dialogVisible.value = false
  loading.value = false
}
watch(
  () => [authSession.user.value?.subject, authSession.user.value?.isAdministrator],
  () => {
    clearPrivate()
    if (authSession.user.value?.isAdministrator) void load()
  },
)
watch(
  () => form.instanceResourceId,
  () => {
    if (application.value) {
      form.compression = 'gzip'
      form.verifyAfterBackup = true
    }
  },
)
onUnmounted(clearPrivate)
</script>
<template>
  <div class="backup-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">BACKUPS</span>
        <h1>备份策略</h1>
        <p>数据库与普通应用写入批准的 HDD 目标；当前不提供恢复入口。</p>
      </div>
      <div class="page-heading__actions">
        <el-button :loading="loading" @click="load">刷新</el-button
        ><el-button type="primary" @click="open()">新增策略</el-button>
      </div>
    </header>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <section class="panel">
      <el-table :data="items" row-key="id" v-loading="loading"
        ><el-table-column prop="instanceResourceId" label="资源" min-width="250" /><el-table-column
          prop="scheduleExpression"
          label="计划"
          min-width="140"
        /><el-table-column label="保留" width="150"
          ><template #default="scope"
            >{{ scope.row.retentionCount }} 份 / {{ scope.row.retentionDays }} 天</template
          ></el-table-column
        ><el-table-column label="状态" width="100"
          ><template #default="scope"
            ><el-tag :type="scope.row.isEnabled ? 'success' : 'info'">{{
              scope.row.isEnabled ? '启用' : '停用'
            }}</el-tag></template
          ></el-table-column
        ><el-table-column label="操作" width="270"
          ><template #default="scope"
            ><el-button link @click="open(scope.row)">编辑</el-button
            ><el-button link type="primary" @click="run(scope.row)">立即备份</el-button
            ><el-button link @click="verify(scope.row)">{{
              scope.row.instanceResourceId.startsWith('application:') ? '备份历史' : '校验'
            }}</el-button></template
          ></el-table-column
        ></el-table
      >
    </section>
    <ApplicationBackups v-model="archivesVisible" :resource-id="archivesResourceId" />
    <el-dialog
      v-model="dialogVisible"
      :title="editing ? '编辑备份策略' : '新增备份策略'"
      width="min(680px, calc(100vw - 32px))"
      ><el-form label-position="top"
        ><el-alert
          title="选择受管数据库或普通应用；不接受任意路径或脚本。应用归档使用 Gzip 和强制 SHA256 校验。"
          type="info"
          :closable="false" />
        <div class="form-grid">
          <el-form-item label="备份资源"
            ><el-select v-model="form.instanceResourceId" :disabled="!!editing" filterable
              ><el-option
                v-for="database in databases"
                :key="database.id"
                :label="database.name"
                :value="database.id" /></el-select></el-form-item
          ><el-form-item label="Cron 表达式"
            ><el-input v-model="form.scheduleExpression" /></el-form-item
          ><el-form-item label="时区"><el-input v-model="form.timezone" /></el-form-item
          ><el-form-item label="压缩"
            ><el-select v-model="form.compression" :disabled="application"
              ><el-option label="Zstandard" value="zstd" /><el-option
                label="Gzip"
                value="gzip" /></el-select></el-form-item
          ><el-form-item v-if="application" label="归档一致性"
            ><el-select v-model="form.consistency"
              ><el-option label="暂停归档 · 临时暂停应用" value="quiesced" /><el-option
                label="实时归档 · 文件可能变化"
                value="live" /></el-select></el-form-item
          ><el-form-item label="保留份数"
            ><el-input-number v-model="form.retentionCount" :min="1" :max="365" /></el-form-item
          ><el-form-item label="保留天数"
            ><el-input-number v-model="form.retentionDays" :min="1" :max="3650" /></el-form-item
          ><el-form-item label="容量告警 %"
            ><el-input-number
              v-model="form.capacityWarningPercent"
              :min="50"
              :max="95" /></el-form-item
          ><el-form-item label="容量严重 %"
            ><el-input-number
              v-model="form.capacityCriticalPercent"
              :min="form.capacityWarningPercent + 1"
              :max="99" /></el-form-item
          ><el-form-item label="启用"><el-switch v-model="form.isEnabled" /></el-form-item
          ><el-form-item label="备份后校验"
            ><el-switch v-model="form.verifyAfterBackup" :disabled="application"
          /></el-form-item></div></el-form
      ><template #footer
        ><el-button @click="dialogVisible = false">取消</el-button
        ><el-button type="primary" :loading="saving" @click="save">保存</el-button></template
      ></el-dialog
    >
  </div>
</template>
<style scoped>
.backup-page {
  display: grid;
  gap: 18px;
}
.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0 14px;
  margin-top: 16px;
}
@media (max-width: 680px) {
  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>
