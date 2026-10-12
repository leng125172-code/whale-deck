<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { apiRequest } from '@/services/apiClient'
import { enqueueOperation, planOperation, stageSecret } from '@/services/operations'
import type { ManagedResource } from '@/services/contracts'

type DbAction =
  | 'create'
  | 'delete'
  | 'create-principal'
  | 'delete-principal'
  | 'disable-principal'
  | 'grant'
  | 'rotate'
  | 'terminate-connection'
const items = ref<ManagedResource[]>([])
const loading = ref(false)
const submitting = ref(false)
const dialogVisible = ref(false)
const selected = ref<ManagedResource | null>(null)
const action = ref<DbAction>('create')
const form = reactive({
  name: '',
  principal: '',
  database: '',
  role: 'readonly',
  keyPrefix: '',
  password: '',
})
const isValkey = computed(() => selected.value?.id.includes('valkey') === true)
const needsPassword = computed(
  () => action.value === 'create-principal' || action.value === 'rotate',
)

async function load() {
  loading.value = true
  try {
    items.value = await apiRequest<ManagedResource[]>('databases')
  } finally {
    loading.value = false
  }
}

function open(item: ManagedResource, nextAction: DbAction) {
  selected.value = item
  action.value = nextAction
  Object.assign(form, {
    name: '',
    principal: '',
    database: '',
    role: 'readonly',
    keyPrefix: '',
    password: '',
  })
  dialogVisible.value = true
}

async function submit() {
  if (!selected.value) return
  submitting.value = true
  try {
    const parameters: Record<string, string> = {}
    if (form.name) parameters.name = form.name.trim()
    if (form.principal) parameters.principal = form.principal.trim()
    if (form.database) parameters.database = form.database.trim()
    if (form.role) parameters.role = form.role
    if (form.keyPrefix) parameters.keyPrefix = form.keyPrefix.trim()
    if (needsPassword.value) {
      const ticket = await stageSecret(form.password || undefined)
      parameters.inputTicket = ticket.token
      parameters.inputKind = 'password'
    }
    let planHash: string | null = null
    if (['create', 'delete', 'delete-principal'].includes(action.value)) {
      const plan = await planOperation('databases', action.value, selected.value.id, parameters)
      planHash = plan.planHash
      if (action.value !== 'create') {
        await ElMessageBox.confirm(
          [...plan.changes, ...plan.warnings].join('\n') || '将永久删除所选资源。',
          '危险操作确认',
          { type: 'error', confirmButtonText: '删除', cancelButtonText: '取消' },
        )
      }
    } else if (['disable-principal', 'rotate', 'terminate-connection'].includes(action.value)) {
      await ElMessageBox.confirm(
        `确认执行 ${action.value}？该操作会影响现有连接或凭据。`,
        '数据库操作确认',
        { type: 'warning' },
      )
    }
    const job = await enqueueOperation(
      'databases',
      action.value,
      selected.value.id,
      parameters,
      true,
      planHash,
    )
    dialogVisible.value = false
    ElMessage.success(`任务已提交：${job.id}`)
  } finally {
    submitting.value = false
  }
}

async function backup(item: ManagedResource) {
  const job = await enqueueOperation('backups', 'run', item.id, {}, false)
  ElMessage.success(`备份任务已提交：${job.id}`)
}

onMounted(load)
</script>

<template>
  <div class="database-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">DATA PLATFORM</span>
        <h1>数据库与缓存</h1>
        <p>按引擎管理数据库、账号、权限与一次性凭据；基础容器受平台保护。</p>
      </div>
      <el-button :loading="loading" @click="load">刷新</el-button>
    </header>
    <section class="database-grid" v-loading="loading">
      <article v-for="item in items" :key="item.id" class="panel database-card">
        <div class="database-card__heading">
          <div>
            <strong>{{ item.name }}</strong
            ><small>{{ item.id }}</small>
          </div>
          <el-tag :type="item.state.toLowerCase().includes('run') ? 'success' : 'info'">{{
            item.state
          }}</el-tag>
        </div>
        <p>{{ item.version || '等待 Agent 返回版本信息' }}</p>
        <div class="database-card__actions">
          <el-button
            v-if="!item.id.includes('valkey')"
            link
            type="primary"
            @click="open(item, 'create')"
            >创建数据库</el-button
          >
          <el-button
            v-if="!item.id.includes('valkey')"
            link
            type="danger"
            @click="open(item, 'delete')"
            >删除数据库</el-button
          >
          <el-button link type="primary" @click="open(item, 'create-principal')"
            >创建账号</el-button
          >
          <el-dropdown trigger="click" @command="(command: DbAction) => open(item, command)"
            ><el-button link>更多账号操作</el-button
            ><template #dropdown
              ><el-dropdown-menu
                ><el-dropdown-item command="grant">授权</el-dropdown-item
                ><el-dropdown-item command="rotate">轮换密码</el-dropdown-item
                ><el-dropdown-item command="disable-principal">禁用账号</el-dropdown-item
                ><el-dropdown-item command="delete-principal" divided>删除账号</el-dropdown-item
                ><el-dropdown-item command="terminate-connection"
                  >终止连接</el-dropdown-item
                ></el-dropdown-menu
              ></template
            ></el-dropdown
          >
          <el-button link @click="backup(item)">立即备份</el-button>
        </div>
      </article>
    </section>

    <el-dialog
      v-model="dialogVisible"
      :title="`${selected?.name || ''} · ${action}`"
      width="min(600px, calc(100vw - 32px))"
    >
      <el-alert
        v-if="needsPassword"
        title="留空将由服务端生成强密码；成功后只能在任务结果中消费一次。"
        type="info"
        show-icon
        :closable="false"
      />
      <el-form label-position="top" class="database-form">
        <el-form-item v-if="action === 'create' || action === 'delete'" label="数据库名称"
          ><el-input v-model="form.name" autocomplete="off"
        /></el-form-item>
        <el-form-item
          v-if="action !== 'create' && action !== 'delete'"
          :label="isValkey ? 'ACL 用户' : '账号 / 角色'"
          ><el-input v-model="form.principal" autocomplete="off"
        /></el-form-item>
        <el-form-item
          v-if="
            !isValkey &&
            ['create-principal', 'delete-principal', 'grant', 'terminate-connection'].includes(
              action,
            )
          "
          label="数据库"
          ><el-input v-model="form.database"
        /></el-form-item>
        <el-form-item v-if="['create-principal', 'grant'].includes(action)" label="权限模板"
          ><el-select v-model="form.role"
            ><el-option label="只读" value="readonly" /><el-option
              label="读写"
              value="readwrite" /><el-option label="数据库管理员" value="admin" /></el-select
        ></el-form-item>
        <el-form-item
          v-if="isValkey && ['create-principal', 'grant'].includes(action)"
          label="Key 前缀"
          ><el-input v-model="form.keyPrefix" placeholder="app:username:"
        /></el-form-item>
        <el-form-item v-if="needsPassword" label="初始密码（可选）"
          ><el-input
            v-model="form.password"
            type="password"
            show-password
            autocomplete="new-password"
        /></el-form-item>
      </el-form>
      <template #footer
        ><el-button @click="dialogVisible = false">取消</el-button
        ><el-button type="primary" :loading="submitting" @click="submit"
          >提交任务</el-button
        ></template
      >
    </el-dialog>
  </div>
</template>

<style scoped>
.database-page,
.database-grid {
  display: grid;
  gap: 18px;
}
.database-grid {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.database-card {
  padding: 18px;
}
.database-card__heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}
.database-card__heading > div {
  display: grid;
  min-width: 0;
  overflow-wrap: anywhere;
  gap: 5px;
}
.database-card small,
.database-card p {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.database-card__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.database-form {
  margin-top: 18px;
}
@media (max-width: 760px) {
  .database-grid {
    grid-template-columns: 1fr;
  }
}
</style>
