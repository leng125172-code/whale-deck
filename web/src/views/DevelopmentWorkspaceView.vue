<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElCard } from 'element-plus'
import WorkspaceSourceImport from '@/components/WorkspaceSourceImport.vue'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import {
  developmentWorkspaces as api,
  workspaceError,
  workspacePath,
  type RunInput,
  type StoragePlan,
  type Workspace,
  type WorkspaceRun,
} from '@/services/developmentWorkspaces'

const route = useRoute()
const router = useRouter()
const id = computed(() => String(route.params.workspaceId ?? ''))
const runId = computed(() => String(route.params.runId ?? ''))
const showRuns = computed(() => route.name === 'development-workspace-runs')
const showSources = computed(() => route.name === 'development-workspace-source')
const workspace = ref<Workspace | null>(null)
const owned = computed(
  () => workspace.value && workspace.value.ownerSubject === authSession.user.value?.subject,
)
const allocated = computed(() => workspace.value?.state === 'StorageAllocated')
const plan = ref<StoragePlan | null>(null)
const checked = ref(false)
const runs = ref<WorkspaceRun[]>([])
const next = ref<string | null>(null)
const selected = ref<WorkspaceRun | null>(null)
const action = ref('')
const error = ref('')
const loading = ref(false)
const denied = ref(false)
const selection = ref('')
const pending = ref<{ key: string; input: RunInput } | null>(null)
const tasks = computed(() =>
  (workspace.value?.environment.runtimes ?? []).flatMap((pin) => {
    if (pin.key === 'node')
      return [{ key: 'node-test', label: 'Node 测试', input: { runtime: 'node', task: 'test' } }]
    if (pin.key === 'dotnet')
      return [
        { key: 'dotnet-build', label: '.NET 构建', input: { runtime: 'dotnet', task: 'build' } },
        { key: 'dotnet-test', label: '.NET 测试', input: { runtime: 'dotnet', task: 'test' } },
      ]
    return []
  }),
)
const picked = computed(() => tasks.value.find((item) => item.key === selection.value)?.input)
const active = (run: WorkspaceRun) => !run.completedAtUtc
const observing = (run: WorkspaceRun) => active(run) || run.reportStatus === 'Pending'
const reportAvailable = (run: WorkspaceRun) =>
  run.reportStatus === 'Available' || run.reportStatus === 'ArtifactAttention'
const states: Record<string, string> = {
  Registered: '待分配存储',
  StorageAllocating: '分配待确认',
  StorageAllocated: '存储已分配',
  Queued: '排队中',
  Running: '运行中',
  Succeeded: '成功',
  Failed: '失败',
  Canceled: '已取消',
  Pending: '报告生成中',
  Available: '报告可下载',
  ArtifactAttention: '报告可下载 · 工件需核对',
  Expired: '报告已到期',
}
const runState = (run: WorkspaceRun) =>
  run.phase === 'ResultUnknown'
    ? '结果未知'
    : active(run) && run.cancellationRequested
      ? '正在取消'
      : (states[run.state] ?? run.state)
const detailRoute = (name: string, targetRun?: string) => ({
  name,
  params: { workspaceId: id.value, ...(targetRun ? { runId: targetRun } : {}) },
})

let lifetime: AbortController
watch(
  () => [id.value, runId.value, showRuns.value, authSession.user.value?.subject, showSources.value],
  (_, previous, onCleanup) => {
    const controller = new AbortController()
    lifetime = controller
    const timer = window.setInterval(() => {
      if (
        document.visibilityState === 'visible' &&
        showRuns.value &&
        !denied.value &&
        !action.value &&
        !loading.value &&
        (selected.value ? observing(selected.value) : runs.value.some(observing))
      )
        void loadRuns(undefined, true)
    }, 3500)
    onCleanup(() => {
      controller.abort()
      window.clearInterval(timer)
    })
    if (!previous || previous[0] !== id.value || previous[3] !== authSession.user.value?.subject)
      pending.value = null
    workspace.value = null
    plan.value = null
    checked.value = false
    runs.value = []
    selected.value = null
    next.value = null
    action.value = ''
    error.value = ''
    loading.value = false
    denied.value = false
    void refresh()
  },
  { immediate: true },
)

function fail(failure: unknown, signal: AbortSignal) {
  if (signal.aborted) return
  if (failure instanceof ApiError && [401, 403].includes(failure.status)) {
    denied.value = true
    workspace.value = null
    plan.value = null
    runs.value = []
    selected.value = null
    next.value = null
    pending.value = null
  }
  error.value = workspaceError(failure)
}

async function refresh() {
  if (loading.value) return
  const signal = lifetime.signal
  denied.value = false
  loading.value = true
  error.value = ''
  try {
    const [, value] = await Promise.all([api.catalog(signal), api.get(id.value, signal)])
    if (signal.aborted) return
    workspace.value = value
    if (!tasks.value.some((task) => task.key === selection.value))
      selection.value = tasks.value[0]?.key ?? ''
    if (showRuns.value && owned.value) await fetchRuns(signal)
  } catch (failure) {
    fail(failure, signal)
  } finally {
    if (!signal.aborted) loading.value = false
  }
}

async function fetchRuns(signal: AbortSignal, after?: string, background = false) {
  const workspaceId = id.value
  const selectedRun = runId.value
  if (background && selectedRun) {
    const current = await api.run(workspaceId, selectedRun, signal)
    if (!signal.aborted) selected.value = current
    return
  }
  const page = await api.listRuns(workspaceId, signal, after)
  if (signal.aborted) return
  if (after) runs.value = [...runs.value, ...page.items]
  else if (background) {
    const known = new Set(page.items.map((run) => run.runId))
    runs.value = [...page.items, ...runs.value.filter((run) => !known.has(run.runId))]
  } else runs.value = page.items
  if (!background) next.value = page.nextAfter
  if (selectedRun && !after) {
    const current = await api.run(workspaceId, selectedRun, signal)
    if (!signal.aborted) selected.value = current
  }
}

async function loadRuns(after?: string, background = false) {
  if (loading.value || action.value || !owned.value) return
  const signal = lifetime.signal
  loading.value = true
  try {
    await fetchRuns(signal, after, background)
    if (!signal.aborted) error.value = ''
  } catch (failure) {
    fail(failure, signal)
  } finally {
    if (!signal.aborted) loading.value = false
  }
}

async function perform(kind: string, operation: (signal: AbortSignal) => Promise<void>) {
  if (action.value || !owned.value) return
  const signal = lifetime.signal
  action.value = kind
  error.value = ''
  try {
    await operation(signal)
  } catch (failure) {
    if (signal.aborted) return
    if (
      kind === 'queue' &&
      failure instanceof ApiError &&
      [400, 401, 403, 404, 409, 422].includes(failure.status)
    )
      pending.value = null
    fail(failure, signal)
  } finally {
    if (!signal.aborted) action.value = ''
  }
}

function loadPlan() {
  void perform('plan', async (signal) => {
    const result = await api.plan(id.value, signal)
    if (!signal.aborted) plan.value = result
  })
}
function allocate() {
  const approved = plan.value
  const current = workspace.value
  if (
    !approved ||
    !current ||
    approved.workspaceId !== current.id ||
    approved.environmentSha256 !== current.environmentSha256 ||
    approved.storageRootId !== current.storageRootId ||
    approved.diskMiB !== current.budget.diskMiB
  )
    return
  void perform('allocate', async (signal) => {
    const value = await api.allocate(current.id, approved.planSha256, signal)
    if (signal.aborted) return
    workspace.value = value
    plan.value = null
    checked.value = false
  })
}
function preflight() {
  checked.value = false
  void perform('preflight', async (signal) => {
    const result = await api.preflight(id.value, signal)
    if (signal.aborted) return
    if (
      result.workspaceId !== workspace.value?.id ||
      result.workspaceVersion !== workspace.value.version ||
      result.environmentSha256 !== workspace.value.environmentSha256
    )
      throw new Error('WORKSPACE_PREFLIGHT_CHANGED')
    checked.value = true
  })
}
function queue() {
  const input = pending.value?.input ?? picked.value
  if (!input || !allocated.value) return
  void perform('queue', async (signal) => {
    pending.value ??= { key: createIdempotencyKey(), input }
    const run = await api.queue(id.value, pending.value.input, pending.value.key, signal)
    if (signal.aborted) return
    pending.value = null
    await router.push(detailRoute('development-workspace-runs', run.runId))
  })
}
function cancel(run: WorkspaceRun) {
  if (!active(run) || run.cancellationRequested) return
  void perform('cancel', async (signal) => {
    const result = await api.cancel(id.value, run.runId, signal)
    if (signal.aborted) return
    if (selected.value?.runId === result.runId) selected.value = result
    runs.value = runs.value.map((item) => (item.runId === result.runId ? result : item))
  })
}
</script>

<template>
  <section class="workspace-page" aria-labelledby="workspace-title">
    <header>
      <RouterLink :to="workspacePath">开发空间</RouterLink>
      <h1 id="workspace-title">{{ workspace?.slug ?? '开发空间详情' }}</h1>
      <p>开发和测试环境</p>
    </header>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <el-button v-if="!workspace" :loading="loading" @click="refresh">重新加载</el-button>
    <template v-if="workspace">
      <nav class="workspace-actions" aria-label="开发空间详情">
        <RouterLink :to="detailRoute('development-workspace-environment')">环境与存储</RouterLink>
        <RouterLink v-if="owned" :to="detailRoute('development-workspace-source')"
          >源码导入</RouterLink
        >
        <RouterLink :to="detailRoute('development-workspace-runs')">运行记录</RouterLink>
      </nav>

      <el-card v-if="!showRuns && !showSources" shadow="never">
        <template #header><strong>环境与存储</strong></template>
        <dl>
          <dt>空间状态</dt>
          <dd>{{ states[workspace.state] ?? workspace.state }}</dd>
          <dt>批准存储根</dt>
          <dd>{{ workspace.storageRootId }}</dd>
          <dt>资源预算</dt>
          <dd>
            {{ workspace.budget.cpuMillis }} 毫核 · {{ workspace.budget.memoryMiB }} MiB 内存 ·
            {{ workspace.budget.diskMiB }} MiB 磁盘
          </dd>
          <dt>环境摘要</dt>
          <dd>
            <code>{{ workspace.environmentSha256 }}</code>
          </dd>
          <dt>固定运行时</dt>
          <dd v-for="pin in workspace.environment.runtimes" :key="pin.key">
            {{ pin.key }} {{ pin.version }}<br /><code>{{ pin.image }}</code>
          </dd>
        </dl>
        <div v-if="owned" class="workspace-actions">
          <el-button
            v-if="!allocated"
            :disabled="!!action"
            :loading="action === 'plan'"
            @click="loadPlan"
          >
            核对存储计划
          </el-button>
          <el-button
            v-else
            :disabled="!!action"
            :loading="action === 'preflight'"
            @click="preflight"
          >
            核验 Runner 存储
          </el-button>
          <el-button :disabled="!!action" :loading="loading" @click="refresh">刷新状态</el-button>
        </div>
        <p v-if="checked" role="status">Runner 存储和配额核验通过。</p>
        <el-card v-if="plan" class="workspace-plan" shadow="never">
          <template #header><strong>确认存储分配</strong></template>
          <p>存储根 {{ plan.storageRootId }} · 磁盘配额 {{ plan.diskMiB }} MiB</p>
          <p>
            <code>{{ plan.planSha256 }}</code>
          </p>
          <el-button
            type="primary"
            :disabled="!!action"
            :loading="action === 'allocate'"
            @click="allocate"
            >确认分配存储</el-button
          >
          <el-button :disabled="!!action" @click="plan = null">关闭计划</el-button>
        </el-card>
      </el-card>

      <template v-else-if="showSources">
        <WorkspaceSourceImport
          v-if="owned && allocated"
          :key="workspace.id"
          :workspace-id="workspace.id"
        />
        <el-alert
          v-else
          :title="owned ? '分配存储后可导入源码。' : '源码导入仅向空间所有者开放。'"
          type="info"
          :closable="false"
          show-icon
        />
      </template>

      <template v-else-if="owned">
        <el-card v-if="!runId" shadow="never">
          <template #header><strong>执行开发任务</strong></template>
          <el-alert
            v-if="pending && !action"
            title="上次提交结果未确认。可以使用原任务和原提交键重试。"
            type="warning"
            :closable="false"
            show-icon
          />
          <p v-if="!allocated">分配存储后可执行开发任务。</p>
          <p v-else-if="!tasks.length">这个空间没有可用的固定构建或测试任务。</p>
          <div v-else class="workspace-actions">
            <el-select v-model="selection" aria-label="开发任务" :disabled="!!pending || !!action">
              <el-option
                v-for="task in tasks"
                :key="task.key"
                :label="task.label"
                :value="task.key"
              />
            </el-select>
            <el-button
              type="primary"
              :disabled="!!action || !picked"
              :loading="action === 'queue'"
              @click="queue"
            >
              {{ pending && !action ? '使用原任务重试' : '提交运行' }}
            </el-button>
          </div>
        </el-card>

        <el-card v-if="runId" v-loading="loading && !selected" shadow="never">
          <template #header><strong>运行详情</strong></template>
          <RouterLink :to="detailRoute('development-workspace-runs')">返回运行记录</RouterLink>
          <template v-if="selected">
            <dl>
              <dt>运行 ID</dt>
              <dd>
                <code>{{ selected.runId }}</code>
              </dd>
              <dt>任务</dt>
              <dd>{{ selected.runtime }} · {{ selected.task }}</dd>
              <dt>状态</dt>
              <dd>{{ runState(selected) }}</dd>
              <dt>阶段</dt>
              <dd>
                <code>{{ selected.phase }}</code>
              </dd>
              <dt v-if="selected.errorCode">故障代码</dt>
              <dd v-if="selected.errorCode">
                <code>{{ selected.errorCode }}</code>
              </dd>
              <dt>报告</dt>
              <dd>
                {{
                  selected.reportStatus
                    ? (states[selected.reportStatus] ?? selected.reportStatus)
                    : '暂无报告'
                }}
              </dd>
            </dl>
            <div class="workspace-actions">
              <el-button
                v-if="active(selected) && !selected.cancellationRequested"
                type="danger"
                plain
                :disabled="!!action"
                :loading="action === 'cancel'"
                @click="cancel(selected)"
                >取消运行</el-button
              >
              <el-button :disabled="!!action" :loading="loading" @click="loadRuns()"
                >刷新运行</el-button
              >
              <RouterLink
                v-if="reportAvailable(selected)"
                :to="detailRoute('development-workspace-report', selected.runId)"
              >
                查看运行报告
              </RouterLink>
            </div>
          </template>
        </el-card>

        <el-card v-else v-loading="loading && !runs.length" shadow="never">
          <template #header>
            <div class="workspace-actions">
              <strong>运行记录</strong
              ><el-button :loading="loading" @click="loadRuns()">刷新记录</el-button>
            </div>
          </template>
          <el-empty v-if="!runs.length && !loading" description="暂无运行记录" />
          <ul v-else class="workspace-history">
            <li v-for="run in runs" :key="run.runId">
              <RouterLink :to="detailRoute('development-workspace-runs', run.runId)"
                >{{ run.runtime }} · {{ run.task }}</RouterLink
              >
              <el-tag effect="plain">{{ runState(run) }}</el-tag>
              <time :datetime="run.createdAtUtc">{{
                new Date(run.createdAtUtc).toLocaleString()
              }}</time>
              <RouterLink
                v-if="reportAvailable(run)"
                :to="detailRoute('development-workspace-report', run.runId)"
                >运行报告</RouterLink
              >
              <span v-else-if="run.reportStatus">{{
                states[run.reportStatus] ?? run.reportStatus
              }}</span>
            </li>
          </ul>
          <el-button v-if="next" :loading="loading" @click="loadRuns(next)">加载更多运行</el-button>
        </el-card>
      </template>
      <el-alert
        v-else
        title="运行记录和控制仅向空间所有者开放。已获报告授权的账号可通过报告链接下载。"
        :closable="false"
        type="info"
        show-icon
      />
    </template>
  </section>
</template>

<style scoped>
.workspace-page a {
  color: var(--el-color-primary);
}
.workspace-page a:hover {
  text-decoration: underline;
}
.workspace-page nav a[aria-current='page'] {
  font-weight: 600;
  text-decoration: underline;
  text-underline-offset: 0.25em;
}
.workspace-page {
  display: grid;
  gap: 16px;
  overflow-wrap: anywhere;
}
.workspace-page h1 {
  margin: 10px 0 0;
}
.workspace-page header p {
  color: var(--el-text-color-secondary);
  margin: 8px 0 0;
}
.workspace-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
}
.workspace-actions .el-select {
  width: min(100%, 280px);
}
.workspace-page dl {
  display: grid;
  grid-template-columns: minmax(88px, max-content) minmax(0, 1fr);
  gap: 8px 16px;
}
.workspace-page dt,
.workspace-history time {
  color: var(--el-text-color-secondary);
}
.workspace-page dd {
  margin: 0;
}
.workspace-plan {
  margin-top: 16px;
}
.workspace-history {
  padding: 0;
  list-style: none;
}
.workspace-history li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  padding: 12px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
</style>
