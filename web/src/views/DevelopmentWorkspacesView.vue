<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElCard, ElCheckboxGroup } from 'element-plus'
import { ApiError, createIdempotencyKey } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import {
  developmentWorkspaces as api,
  workspaceError,
  workspacePath,
  type Workspace,
  type WorkspaceCatalog,
  type WorkspaceRegistration,
} from '@/services/developmentWorkspaces'

const route = useRoute()
const router = useRouter()
const creating = computed(() => route.name === 'development-workspace-new')
const rows = ref<Workspace[]>([])
const catalog = ref<WorkspaceCatalog | null>(null)
const next = ref<string | null>(null)
const error = ref('')
const loading = ref(false)
const saving = ref(false)
const search = ref('')
const state = ref('')
const form = reactive<WorkspaceRegistration>({
  slug: '',
  storageRootId: '',
  runtimes: [],
  budget: { cpuMillis: 500, memoryMiB: 512, diskMiB: 1024 },
})
const pending = ref<{ key: string; input: WorkspaceRegistration } | null>(null)
const filtered = computed(() =>
  rows.value.filter(
    (row) =>
      row.slug.includes(search.value.trim().toLowerCase()) &&
      (!state.value || row.state === state.value),
  ),
)
const valid = computed(() => {
  const limits = catalog.value?.capacity
  const { cpuMillis, memoryMiB, diskMiB } = form.budget
  return (
    limits &&
    /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(form.slug) &&
    form.slug.length <= 48 &&
    catalog.value?.storageRootIds.includes(form.storageRootId) &&
    form.runtimes.length >= 1 &&
    form.runtimes.length <= 4 &&
    Number.isInteger(cpuMillis) &&
    cpuMillis >= 100 &&
    cpuMillis <= limits.cpuMillis &&
    Number.isInteger(memoryMiB) &&
    memoryMiB >= 128 &&
    memoryMiB <= limits.memoryMiB &&
    Number.isInteger(diskMiB) &&
    diskMiB >= 256 &&
    diskMiB <= Math.min(1048576, limits.diskMiB)
  )
})
let lifetime: AbortController
watch(
  () => [creating.value, authSession.user.value?.subject],
  (_, __, onCleanup) => {
    const controller = new AbortController()
    lifetime = controller
    onCleanup(() => controller.abort())
    pending.value = null
    saving.value = false
    catalog.value = null
    rows.value = []
    next.value = null
    loading.value = false
    Object.assign(form, {
      slug: '',
      storageRootId: '',
      runtimes: [],
      budget: { cpuMillis: 500, memoryMiB: 512, diskMiB: 1024 },
    })
    void load()
  },
  { immediate: true },
)

async function load(after?: string) {
  if (loading.value) return
  const signal = lifetime.signal
  loading.value = true
  error.value = ''
  try {
    if (creating.value) {
      const value = await api.catalog(signal)
      if (signal.aborted) return
      catalog.value = value
      form.storageRootId = value.storageRootIds[0] ?? ''
      form.budget.cpuMillis = Math.min(500, value.capacity.cpuMillis)
      form.budget.memoryMiB = Math.min(512, value.capacity.memoryMiB)
      form.budget.diskMiB = Math.min(1024, value.capacity.diskMiB)
    } else {
      const [configuration, page] = await Promise.all([
        api.catalog(signal),
        api.list(signal, after),
      ])
      if (signal.aborted) return
      catalog.value = configuration
      rows.value = after ? [...rows.value, ...page.items] : page.items
      next.value = page.nextAfter
    }
  } catch (failure) {
    if (signal.aborted) return
    if (failure instanceof ApiError && [401, 403].includes(failure.status)) {
      catalog.value = null
      rows.value = []
      next.value = null
    }
    error.value = workspaceError(failure)
  } finally {
    if (!signal.aborted) loading.value = false
  }
}

async function register() {
  if (saving.value || (!pending.value && !valid.value)) return
  const signal = lifetime.signal
  pending.value ??= {
    key: createIdempotencyKey(),
    input: {
      slug: form.slug,
      storageRootId: form.storageRootId,
      runtimes: [...form.runtimes],
      budget: { ...form.budget },
    },
  }
  const submission = pending.value
  saving.value = true
  error.value = ''
  try {
    const { workspace } = await api.register(submission.input, submission.key, signal)
    if (signal.aborted) return
    pending.value = null
    await router.push({
      name: 'development-workspace-environment',
      params: { workspaceId: workspace.id },
    })
  } catch (failure) {
    if (signal.aborted) return
    if (failure instanceof ApiError && [400, 401, 403, 404, 409, 422].includes(failure.status))
      pending.value = null
    if (failure instanceof ApiError && [401, 403].includes(failure.status)) catalog.value = null
    error.value = workspaceError(failure)
  } finally {
    if (!signal.aborted) saving.value = false
  }
}

const states: Record<string, string> = {
  Registered: '待分配存储',
  StorageAllocating: '分配待确认',
  StorageAllocated: '存储已分配',
}
</script>

<template>
  <section class="workspace-page" aria-labelledby="workspace-list-title">
    <header class="workspace-heading">
      <div>
        <h1 id="workspace-list-title">{{ creating ? '登记开发空间' : '开发空间' }}</h1>
        <p>开发和测试环境 · 固定运行时与独立资源预算</p>
      </div>
      <RouterLink v-if="creating" :to="workspacePath">返回空间列表</RouterLink>
      <RouterLink v-else-if="catalog" to="/development/workspaces/new">登记开发空间</RouterLink>
    </header>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <el-button v-if="error && !catalog" :loading="loading" @click="load()">重试</el-button>

    <el-card v-if="creating && catalog" v-loading="loading" shadow="never">
      <el-alert
        v-if="pending && !saving"
        title="上次提交结果未确认。可以使用原参数和原提交键重试。"
        type="warning"
        :closable="false"
        show-icon
      />
      <el-form label-position="top" :disabled="!!pending" @submit.prevent="register">
        <div class="workspace-fields">
          <el-form-item label="空间标识">
            <el-input
              v-model="form.slug"
              placeholder="例如 team-test"
              maxlength="48"
              aria-label="空间标识"
            />
          </el-form-item>
          <el-form-item label="批准存储根">
            <el-select
              v-model="form.storageRootId"
              aria-label="批准存储根"
              placeholder="选择存储根"
            >
              <el-option
                v-for="root in catalog.storageRootIds"
                :key="root"
                :label="root"
                :value="root"
              />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item label="固定运行时">
          <el-checkbox-group v-model="form.runtimes" :max="4">
            <el-checkbox
              v-for="runtime in catalog.runtimes"
              :key="runtime.key"
              :value="runtime.key"
            >
              {{ runtime.key }} {{ runtime.version }}
            </el-checkbox>
          </el-checkbox-group>
          <p v-if="!catalog.runtimes.length">管理员尚未登记可用的运行时。</p>
        </el-form-item>
        <div class="workspace-fields">
          <el-form-item label="CPU（毫核）">
            <el-input-number
              v-model="form.budget.cpuMillis"
              :min="100"
              :max="catalog.capacity.cpuMillis"
              aria-label="CPU（毫核）"
            />
          </el-form-item>
          <el-form-item label="内存（MiB）">
            <el-input-number
              v-model="form.budget.memoryMiB"
              :min="128"
              :max="catalog.capacity.memoryMiB"
              aria-label="内存（MiB）"
            />
          </el-form-item>
          <el-form-item label="磁盘（MiB）">
            <el-input-number
              v-model="form.budget.diskMiB"
              :min="256"
              :max="Math.min(1048576, catalog.capacity.diskMiB)"
              aria-label="磁盘（MiB）"
            />
          </el-form-item>
        </div>
      </el-form>
      <el-button type="primary" :disabled="!pending && !valid" :loading="saving" @click="register">
        {{ pending && !saving ? '使用原提交重试' : '登记空间' }}
      </el-button>
    </el-card>

    <el-card v-else-if="!creating && (catalog || loading)" v-loading="loading" shadow="never">
      <div class="workspace-toolbar">
        <el-input
          v-model="search"
          placeholder="筛选已加载空间"
          aria-label="筛选已加载空间"
          clearable
        />
        <el-select v-model="state" placeholder="全部状态" aria-label="空间状态筛选" clearable>
          <el-option v-for="(label, key) in states" :key="key" :label="label" :value="key" />
        </el-select>
        <el-button @click="load()">刷新</el-button>
      </div>
      <el-empty
        v-if="!filtered.length && !loading"
        :description="rows.length ? '已加载空间中没有匹配项' : '暂无可见的开发空间'"
      />
      <div v-else class="workspace-cards">
        <el-card v-for="row in filtered" :key="row.id" shadow="never">
          <template #header>
            <RouterLink
              :to="{ name: 'development-workspace-environment', params: { workspaceId: row.id } }"
            >
              {{ row.slug }}
            </RouterLink>
          </template>
          <el-tag effect="plain">{{ states[row.state] ?? row.state }}</el-tag>
          <p>
            {{ row.environment.runtimes.map((pin) => `${pin.key} ${pin.version}`).join(' · ') }}
          </p>
          <p>
            {{ row.budget.cpuMillis }} 毫核 · {{ row.budget.memoryMiB }} MiB 内存 ·
            {{ row.budget.diskMiB }} MiB 磁盘
          </p>
          <RouterLink :to="{ name: 'development-workspace-runs', params: { workspaceId: row.id } }">
            查看运行
          </RouterLink>
        </el-card>
      </div>
      <el-button v-if="next" class="workspace-more" :loading="loading" @click="load(next)"
        >加载更多空间</el-button
      >
    </el-card>
    <p v-else-if="loading" role="status">正在加载空间配置…</p>
  </section>
</template>

<style scoped>
.workspace-page {
  display: grid;
  gap: 16px;
}
.workspace-heading,
.workspace-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  justify-content: space-between;
}
.workspace-heading h1 {
  margin: 0;
}
.workspace-heading p {
  color: var(--el-text-color-secondary);
  margin: 8px 0 0;
}
.workspace-toolbar > :is(.el-input, .el-select) {
  width: min(100%, 240px);
}
.workspace-cards,
.workspace-fields {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(240px, 100%), 1fr));
  gap: 12px;
}
.workspace-cards {
  margin-top: 16px;
  overflow-wrap: anywhere;
}
.workspace-more {
  margin-top: 16px;
}
</style>
