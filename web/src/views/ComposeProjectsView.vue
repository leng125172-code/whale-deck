<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { RefreshRight } from '@element-plus/icons-vue'
import { ApiError, apiRequest } from '@/services/apiClient'
import type { ManagedResource } from '@/services/contracts'
import { enqueueOperation, planOperation, type OperationPlan } from '@/services/operations'
import { authSession } from '@/services/authSession'

const resources = ref<ManagedResource[]>([])
const route = useRoute()
const projectFocus = computed(() =>
  typeof route?.query.project === 'string' ? route.query.project : '',
)
const visibleResources = computed(() =>
  resources.value.filter(
    (item) =>
      !projectFocus.value || (item.attributes.composeProject || item.name) === projectFocus.value,
  ),
)
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const traceId = ref('')
const dialog = ref(false)
const selected = ref<ManagedResource | null>(null)
const plan = ref<OperationPlan | null>(null)
const action = ref('redeploy')
const lastJob = ref('')
const labels: Record<string, string> = {
  start: '启动',
  stop: '停止',
  restart: '重启',
  pull: '拉取镜像',
  redeploy: '重部署',
}
const stateLabels: Record<string, string> = {
  Running: '运行中',
  Stopped: '已停止',
  Missing: '尚未创建',
  Partial: '部分运行',
  Restarting: '重启中',
}
let generation = 0
watch(
  () => [authSession.user.value?.subject, authSession.user.value?.isAdministrator],
  () => {
    generation++
    resources.value = []
    selected.value = null
    plan.value = null
    dialog.value = false
    lastJob.value = ''
    loading.value = false
    if (authSession.user.value?.isAdministrator) void load()
  },
)

function failure(reason: unknown) {
  if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
    generation++
    resources.value = []
    selected.value = null
    plan.value = null
    dialog.value = false
    loading.value = false
  }
  error.value = reason instanceof Error ? reason.message : 'Compose 项目请求失败'
  traceId.value = reason instanceof ApiError ? reason.problem.traceId || '' : ''
}

async function load() {
  const current = ++generation
  loading.value = true
  try {
    const value = await apiRequest<ManagedResource[]>('compose-projects')
    if (current !== generation) return
    resources.value = value
    error.value = ''
    traceId.value = ''
  } catch (reason) {
    if (current === generation) failure(reason)
  } finally {
    if (current === generation) loading.value = false
  }
}

function actionsFor(item: ManagedResource) {
  return item.isProtected ? [] : (item.attributes.allowedActions || '').split(',').filter(Boolean)
}

function describe(line: string) {
  return line
    .replace('ConfigurationChanged', '配置已变化')
    .replace('Unchanged', '配置一致')
    .replace('Missing', '尚未创建')
    .replace('affected containers:', '受影响容器：')
    .replace('Orphaned; retained (no automatic deletion)', '孤立服务；保留，不自动删除')
}

async function prepare(item: ManagedResource, verb: string) {
  if (busy.value || !actionsFor(item).includes(verb)) return
  const current = generation
  busy.value = true
  try {
    const preview = await planOperation('compose-projects', verb, item.id, {})
    if (current !== generation) return
    selected.value = item
    action.value = verb
    plan.value = preview
    dialog.value = true
  } catch (reason) {
    if (current === generation) failure(reason)
  } finally {
    busy.value = false
  }
}

async function submit() {
  if (!plan.value || !selected.value || busy.value) return
  if (Date.parse(plan.value.expiresAtUtc) <= Date.now()) {
    plan.value = null
    ElMessage.warning('计划已过期，请关闭窗口后重新预览。')
    return
  }
  const current = generation
  busy.value = true
  const confirmedPlan = plan.value
  const target = selected.value
  plan.value = null
  try {
    const job = await enqueueOperation(
      'compose-projects',
      action.value,
      target.id,
      {},
      true,
      confirmedPlan.planHash,
    )
    if (current !== generation) return
    lastJob.value = job.id
    dialog.value = false
    ElMessage.success('任务已提交，可在任务中心查看进度。')
  } catch (reason) {
    if (current === generation) failure(reason)
  } finally {
    busy.value = false
  }
}

onMounted(load)
onBeforeUnmount(() => {
  generation++
})
</script>

<template>
  <div class="compose-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">COMPOSE</span>
        <h1>Compose 项目</h1>
        <p>查看项目与服务；批准目录中的应用可预检后管理，平台核心项目仅展示。</p>
      </div>
      <el-button :loading="loading" :disabled="busy" @click="load">
        <el-icon><RefreshRight /></el-icon>刷新
      </el-button>
    </header>
    <el-alert
      v-if="error"
      :title="error"
      :description="traceId ? '追踪号：' + traceId : undefined"
      type="error"
      show-icon
      :closable="false"
    />
    <el-alert v-if="lastJob" title="任务已提交，状态不会立即变化" type="info" :closable="false">
      <router-link to="/jobs">前往任务中心查看进度</router-link>
    </el-alert>
    <section class="compose-list" v-loading="loading">
      <el-alert v-if="projectFocus" type="info" :closable="false" title="已按关联项目筛选">
        {{ projectFocus }} · <router-link to="/compose-projects">显示全部</router-link>
      </el-alert>
      <el-empty
        v-if="!loading && !error && !visibleResources.length"
        :description="projectFocus ? '关联项目已移除或没有可见容器' : '没有发现 Compose 项目'"
      />
      <article v-for="item in visibleResources" :key="item.id" class="panel compose-project">
        <header class="compose-project__heading">
          <h2>{{ item.name }}</h2>
          <el-tag :type="item.state === 'Running' ? 'success' : 'info'" effect="plain">{{
            stateLabels[item.state] || item.state
          }}</el-tag>
          <el-tag v-if="item.isProtected" type="warning">受保护</el-tag>
          <el-tag v-else-if="!actionsFor(item).length" type="info">只读</el-tag>
        </header>
        <dl>
          <dt>服务</dt>
          <dd>
            {{ item.attributes.services || '尚未创建' }} ·
            {{ item.attributes.containerCount || '0' }} 个容器
          </dd>
          <dt>配置来源</dt>
          <dd>
            {{
              actionsFor(item).length
                ? item.attributes.configurationSource
                : '未注册管理来源，仅展示'
            }}
          </dd>
          <template v-if="item.attributes.workingDirectory"
            ><dt>工作目录</dt>
            <dd>{{ item.attributes.workingDirectory }}</dd></template
          >
        </dl>
        <div class="compose-actions">
          <router-link
            :to="{
              path: '/containers',
              query: { project: item.attributes.composeProject || item.name },
            }"
            >关联容器</router-link
          >
          <router-link
            v-if="item.id.startsWith('application:') && item.type === 'ComposeApplication'"
            :to="'/applications/' + encodeURIComponent(item.id)"
            >应用详情</router-link
          >
        </div>
        <footer v-if="actionsFor(item).length" class="compose-actions">
          <el-button :disabled="busy" @click="prepare(item, 'redeploy')">配置差异</el-button>
          <el-button
            v-for="verb in actionsFor(item)"
            :key="verb"
            :disabled="busy"
            :type="verb === 'stop' ? 'danger' : 'primary'"
            plain
            @click="prepare(item, verb)"
            >{{ labels[verb] || verb }}</el-button
          >
        </footer>
      </article>
    </section>
    <el-dialog
      v-model="dialog"
      :title="labels[action] + ' · ' + (selected?.name || '')"
      width="min(660px, 94vw)"
      :close-on-click-modal="!busy"
      :close-on-press-escape="!busy"
      :show-close="!busy"
    >
      <el-alert
        title="保留配置与数据卷，不自动删除孤立服务"
        type="info"
        :closable="false"
        show-icon
      />
      <p>
        拉取镜像不会重启服务；重部署使用本地镜像，不隐式拉取。执行前重新核对配置，变化后需重新预览。
      </p>
      <ul v-if="plan" class="compose-changes">
        <li v-for="line in plan.changes.slice(1)" :key="line">{{ describe(line) }}</li>
      </ul>
      <el-alert
        v-else
        title="计划已使用、过期或提交失败，请重新预览"
        type="warning"
        :closable="false"
      />
      <template #footer>
        <el-button :disabled="busy" @click="dialog = false">取消</el-button>
        <el-button
          :loading="busy"
          :disabled="!plan"
          :type="action === 'stop' ? 'danger' : 'primary'"
          @click="submit"
          >确认{{ labels[action] }}</el-button
        >
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.compose-page,
.compose-list {
  display: grid;
  gap: 18px;
  min-width: 0;
}
.compose-project {
  padding: 20px;
  min-width: 0;
}
.compose-project__heading,
.compose-actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}
.compose-project__heading h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 500;
  overflow-wrap: anywhere;
}
.compose-project dl {
  display: grid;
  grid-template-columns: 80px minmax(0, 1fr);
  gap: 8px 12px;
  font-size: 13px;
}
.compose-project dt {
  color: var(--el-text-color-secondary);
}
.compose-project dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.compose-actions .el-button + .el-button {
  margin-left: 0;
}
.compose-changes {
  padding-left: 20px;
  overflow-wrap: anywhere;
  line-height: 1.8;
}
</style>
