<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  ArrowDown,
  Box,
  DataAnalysis,
  Grid,
  Link,
  Monitor,
  Odometer,
  Operation,
  Setting,
  BellFilled,
  Calendar,
  Document,
  UserFilled,
  Connection,
  Tickets,
  UploadFilled,
} from '@element-plus/icons-vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
import AppTopbar from '@/components/AppTopbar.vue'
import { authSession } from '@/services/authSession'
import { apiRequest } from '@/services/apiClient'
import type { AlertEvent, GlobalSearchResult, Job } from '@/services/contracts'

const route = useRoute()
const router = useRouter()
const isMobileNavigationOpen = ref(false)
const searchVisible = ref(false)
const notificationsVisible = ref(false)
const jobsVisible = ref(false)
const searchQuery = ref('')
const alerts = ref<AlertEvent[]>([])
const jobs = ref<Job[]>([])
const managementLoading = ref(false)
const remoteSearchResults = ref<GlobalSearchResult[]>([])
const searchLoading = ref(false)

const activeNavigation = computed(() => route.path)
const currentUserName = computed(() => authSession.user.value?.name || '已登录')
const isAdministrator = computed(() => authSession.user.value?.isAdministrator === true)

const navigationGroups = [
  {
    label: '工作站',
    items: [
      { index: '/', label: '工作站概览', icon: Grid, administratorOnly: false },
      { index: '/portal', label: '我的门户', icon: Link, administratorOnly: false },
      { index: '/services', label: '内部服务', icon: Link, administratorOnly: false },
    ],
  },
  {
    label: '运行与监控',
    items: [
      { index: '/containers', label: '容器', icon: DataAnalysis, administratorOnly: true },
      { index: '/docker/images', label: '镜像', icon: Box, administratorOnly: true },
      {
        index: '/docker/networks',
        label: 'Docker 网络',
        icon: Connection,
        administratorOnly: true,
      },
      { index: '/docker/volumes', label: '数据卷', icon: Odometer, administratorOnly: true },
      { index: '/docker/settings', label: 'Docker 设置', icon: Setting, administratorOnly: true },
      { index: '/compose-projects', label: 'Compose 项目', icon: Box, administratorOnly: true },
      { index: '/databases', label: '数据库', icon: Odometer, administratorOnly: true },
      { index: '/systemd', label: '系统服务', icon: Monitor, administratorOnly: true },
      { index: '/host/resources', label: '主机设备', icon: Connection, administratorOnly: true },
      { index: '/host/processes', label: '主机进程', icon: Monitor, administratorOnly: true },
      { index: '/host/logs', label: '系统日志', icon: Tickets, administratorOnly: true },
      { index: '/host/updates', label: '系统更新', icon: UploadFilled, administratorOnly: true },
      { index: '/jobs', label: '任务中心', icon: Setting, administratorOnly: true },
      {
        index: '/operations/agents',
        label: '宿主机 Agent',
        icon: Monitor,
        administratorOnly: true,
      },
      { index: '/schedules', label: '计划任务', icon: Calendar, administratorOnly: true },
    ],
  },
  {
    label: '应用',
    items: [
      { index: '/applications', label: '应用管理', icon: Box, administratorOnly: true },
      { index: '/backups', label: '备份策略', icon: Document, administratorOnly: true },
      { index: '/identity/users', label: '身份用户', icon: UserFilled, administratorOnly: true },
      { index: '/identity/groups', label: '身份组', icon: UserFilled, administratorOnly: true },
      { index: '/identity/sso', label: 'SSO 应用', icon: Link, administratorOnly: true },
    ],
  },
  {
    label: '治理',
    items: [
      { index: '/alerts', label: '告警', icon: BellFilled, administratorOnly: true },
      { index: '/audit', label: '审计日志', icon: Document, administratorOnly: true },
      { index: '/settings', label: '平台设置', icon: Setting, administratorOnly: true },
      {
        index: '/settings/config-repository',
        label: '配置仓库',
        icon: Document,
        administratorOnly: true,
      },
      {
        index: '/platform/maintenance',
        label: '平台维护',
        icon: Operation,
        administratorOnly: true,
      },
    ],
  },
] as const

const visibleNavigationGroups = computed(() =>
  navigationGroups
    .map((group) => ({
      ...group,
      items: group.items.filter((item) => !item.administratorOnly || isAdministrator.value),
    }))
    .filter((group) => group.items.length > 0),
)
const navigationSearchResults = computed(() => {
  const query = searchQuery.value.trim().toLocaleLowerCase()
  return visibleNavigationGroups.value
    .flatMap((group) =>
      group.items.map((item) => ({
        id: `navigation:${item.index}`,
        title: item.label,
        subtitle: group.label,
        targetUrl: item.index,
        external: false,
        icon: item.icon,
      })),
    )
    .filter(
      (item) =>
        !query ||
        item.title.toLocaleLowerCase().includes(query) ||
        item.subtitle.toLocaleLowerCase().includes(query),
    )
})
const searchResults = computed(() => [
  ...navigationSearchResults.value,
  ...remoteSearchResults.value.map((item) => ({
    ...item,
    icon: searchIcon(item.kind),
  })),
])
const activeAlertCount = computed(
  () => alerts.value.filter((item) => item.state !== 'Recovered').length,
)
const activeJobCount = computed(
  () => jobs.value.filter((item) => item.state === 'Queued' || item.state === 'Running').length,
)

let managementRefreshTimer: number | undefined
let searchTimer: number | undefined
let searchGeneration = 0

async function refreshManagementData() {
  if (!isAdministrator.value || managementLoading.value) return
  managementLoading.value = true
  try {
    ;[alerts.value, jobs.value] = await Promise.all([
      apiRequest<AlertEvent[]>('alerts?includeRecovered=false'),
      apiRequest<Job[]>('jobs?take=50'),
    ])
  } catch {
    // The dedicated pages surface request diagnostics. Topbar counters remain
    // unobtrusive when a dependency is temporarily unavailable.
  } finally {
    managementLoading.value = false
  }
}

function openSearch() {
  searchQuery.value = ''
  remoteSearchResults.value = []
  searchVisible.value = true
}

async function openNotifications() {
  await refreshManagementData()
  notificationsVisible.value = true
}

async function openJobs() {
  await refreshManagementData()
  jobsVisible.value = true
}

async function openAlertsPage() {
  notificationsVisible.value = false
  await router.push('/alerts')
}

async function openJobsPage() {
  jobsVisible.value = false
  await router.push('/jobs')
}

async function selectSearchResult(item: { targetUrl: string; external: boolean }) {
  searchVisible.value = false
  if (item.external) {
    window.open(item.targetUrl, '_blank', 'noopener,noreferrer')
    return
  }
  await selectNavigation(item.targetUrl)
}

function searchIcon(kind: string) {
  if (kind === 'Portal') return Link
  if (kind === 'User') return UserFilled
  if (kind === 'Job') return Setting
  if (kind === 'Application' || kind === 'ComposeApplication') return Box
  if (kind === 'Container') return DataAnalysis
  if (kind === 'DockerNetwork') return Connection
  if (kind === 'DockerVolume' || kind.includes('Database')) return Odometer
  return Document
}

watch(searchQuery, (value) => {
  if (searchTimer !== undefined) window.clearTimeout(searchTimer)
  const generation = ++searchGeneration
  const query = value.trim()
  if (query.length < 2) {
    remoteSearchResults.value = []
    searchLoading.value = false
    return
  }
  searchTimer = window.setTimeout(async () => {
    searchLoading.value = true
    try {
      const results = await apiRequest<GlobalSearchResult[]>(
        `search?q=${encodeURIComponent(query)}&take=30`,
      )
      if (generation === searchGeneration) remoteSearchResults.value = results
    } catch {
      if (generation === searchGeneration) remoteSearchResults.value = []
    } finally {
      if (generation === searchGeneration) searchLoading.value = false
    }
  }, 250)
})

function handleSearchShortcut(event: KeyboardEvent) {
  if ((event.ctrlKey || event.metaKey) && event.key.toLocaleLowerCase() === 'k') {
    event.preventDefault()
    openSearch()
  }
}

function formatDate(value: string | null) {
  return value ? new Date(value).toLocaleString() : '—'
}

onMounted(() => {
  window.addEventListener('keydown', handleSearchShortcut)
  void refreshManagementData()
  managementRefreshTimer = window.setInterval(() => void refreshManagementData(), 30_000)
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', handleSearchShortcut)
  if (managementRefreshTimer !== undefined) window.clearInterval(managementRefreshTimer)
  if (searchTimer !== undefined) window.clearTimeout(searchTimer)
})

function toggleMobileNavigation() {
  isMobileNavigationOpen.value = !isMobileNavigationOpen.value
}

async function selectNavigation(index: string) {
  isMobileNavigationOpen.value = false
  await router.push(index)
}

async function userCommand(command: string) {
  if (command === 'portal') await router.push('/portal')
  if (command === 'logout') await authSession.logout()
}
</script>

<template>
  <div class="app-shell">
    <AppTopbar
      show-navigation-trigger
      :show-management-actions="isAdministrator"
      :notification-count="activeAlertCount"
      :job-count="activeJobCount"
      @toggle-navigation="toggleMobileNavigation"
      @open-search="openSearch"
      @open-notifications="openNotifications"
      @open-jobs="openJobs"
    />

    <div class="shell-body">
      <aside class="sidebar" :class="{ 'sidebar--mobile-open': isMobileNavigationOpen }">
        <nav class="sidebar__navigation" aria-label="主导航">
          <template v-for="group in visibleNavigationGroups" :key="group.label">
            <div class="navigation-group">
              <span class="navigation-group__label">{{ group.label }}</span>
              <el-menu
                class="side-menu"
                :default-active="activeNavigation"
                @select="selectNavigation"
              >
                <el-menu-item v-for="item in group.items" :key="item.index" :index="item.index">
                  <el-icon><component :is="item.icon" /></el-icon>
                  <template #title>{{ item.label }}</template>
                </el-menu-item>
              </el-menu>
            </div>
          </template>
        </nav>

        <div class="sidebar__footer">
          <div class="sidebar-status">
            <span class="sidebar-status__icon"
              ><el-icon><Operation /></el-icon
            ></span>
            <span class="sidebar-status__copy">
              <strong>平台服务</strong>
              <small>API 会话已连接</small>
            </span>
          </div>
          <el-dropdown trigger="click" placement="top-start" @command="userCommand">
            <button class="sidebar-user" type="button" aria-label="打开用户菜单">
              <span class="sidebar-user__avatar">
                <el-icon><UserFilled /></el-icon>
              </span>
              <span class="sidebar-user__copy">
                <strong>{{ currentUserName }}</strong>
                <small>{{ isAdministrator ? '管理员' : '普通用户' }} · Authentik</small>
              </span>
              <el-icon class="sidebar-user__arrow"><ArrowDown /></el-icon>
            </button>
            <template #dropdown
              ><el-dropdown-menu
                ><el-dropdown-item command="portal">我的门户</el-dropdown-item
                ><el-dropdown-item divided command="logout"
                  >退出登录</el-dropdown-item
                ></el-dropdown-menu
              ></template
            >
          </el-dropdown>
        </div>
      </aside>

      <button
        v-if="isMobileNavigationOpen"
        class="navigation-backdrop"
        type="button"
        aria-label="关闭导航"
        @click="isMobileNavigationOpen = false"
      />

      <main class="main-stage">
        <div class="page-content">
          <RouterView />
        </div>
      </main>
    </div>

    <el-dialog
      v-model="searchVisible"
      title="搜索平台功能"
      width="min(620px, calc(100vw - 28px))"
      class="global-search-dialog"
    >
      <el-input
        v-model="searchQuery"
        autofocus
        clearable
        placeholder="搜索页面、门户、资源、任务、应用或用户"
        size="large"
      />
      <div v-loading="searchLoading" class="global-search-results">
        <button
          v-for="item in searchResults"
          :key="item.id"
          type="button"
          @click="selectSearchResult(item)"
        >
          <el-icon><component :is="item.icon" /></el-icon>
          <span
            ><strong>{{ item.title }}</strong
            ><small>{{ item.subtitle }}</small></span
          >
        </button>
        <el-empty
          v-if="searchResults.length === 0"
          description="没有可访问的匹配功能"
          :image-size="64"
        />
      </div>
    </el-dialog>

    <el-drawer v-model="notificationsVisible" title="通知与活动告警" size="min(440px, 92vw)">
      <div class="topbar-drawer-list" v-loading="managementLoading">
        <el-empty v-if="alerts.length === 0" description="当前没有活动告警" />
        <button v-for="item in alerts" :key="item.id" type="button" @click="openAlertsPage">
          <span
            ><strong>{{ item.summaryCode }}</strong
            ><small>{{ item.state }} · {{ formatDate(item.lastOccurredAtUtc) }}</small></span
          >
          <el-tag
            :type="
              item.severity === 'Critical'
                ? 'danger'
                : item.severity === 'Warning'
                  ? 'warning'
                  : 'info'
            "
            >{{ item.severity }}</el-tag
          >
        </button>
      </div>
    </el-drawer>

    <el-drawer v-model="jobsVisible" title="任务中心" size="min(480px, 92vw)">
      <div class="topbar-drawer-list" v-loading="managementLoading">
        <el-empty v-if="jobs.length === 0" description="暂无任务" />
        <button v-for="item in jobs" :key="item.id" type="button" @click="openJobsPage">
          <span
            ><strong>{{ item.jobType }}</strong
            ><small>{{ item.phase }} · {{ formatDate(item.createdAtUtc) }}</small></span
          >
          <el-tag
            :type="
              item.state === 'Succeeded' ? 'success' : item.state === 'Failed' ? 'danger' : 'info'
            "
            >{{ item.state }}</el-tag
          >
        </button>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.global-search-results,
.topbar-drawer-list {
  display: grid;
  gap: 8px;
  margin-top: 14px;
}
.global-search-results button,
.topbar-drawer-list button {
  display: flex;
  width: 100%;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
  color: var(--el-text-color-primary);
  background: var(--el-fill-color-blank);
  text-align: left;
  cursor: pointer;
}
.global-search-results button:hover,
.topbar-drawer-list button:hover {
  border-color: var(--el-color-primary-light-5);
  background: var(--el-color-primary-light-9);
}
.global-search-results button > span,
.topbar-drawer-list button > span {
  display: grid;
  flex: 1;
  gap: 3px;
  min-width: 0;
}
.global-search-results small,
.topbar-drawer-list small {
  overflow: hidden;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
