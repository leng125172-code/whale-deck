import { createRouter, createWebHistory } from 'vue-router'
import AppShell from '@/layouts/AppShell.vue'
import { authSession } from '@/services/authSession'
import LoginView from '@/views/LoginView.vue'
import OverviewView from '@/views/OverviewView.vue'
import StateView from '@/views/StateView.vue'

const PortalView = () => import('@/views/PortalView.vue')
const ResourceListView = () => import('@/views/ResourceListView.vue')
const JobsView = () => import('@/views/JobsView.vue')
const RecordsView = () => import('@/views/RecordsView.vue')
const ContainersView = () => import('@/views/ContainersView.vue')
const ApplicationsView = () => import('@/views/ApplicationsView.vue')
const BackupsView = () => import('@/views/BackupsView.vue')
const ConfigRepositoryView = () => import('@/views/ConfigRepositoryView.vue')
const DatabasesView = () => import('@/views/DatabasesView.vue')
const DockerSettingsView = () => import('@/views/DockerSettingsView.vue')
const IdentityView = () => import('@/views/IdentityView.vue')
const PlatformMaintenanceView = () => import('@/views/PlatformMaintenanceView.vue')
const SchedulesView = () => import('@/views/SchedulesView.vue')
const AlertsView = () => import('@/views/AlertsView.vue')
const SettingsView = () => import('@/views/SettingsView.vue')
const NotificationSettingsView = () => import('@/views/NotificationSettingsView.vue')
const HostResourcesView = () => import('@/views/HostResourcesView.vue')
const HostLogsView = () => import('@/views/HostLogsView.vue')
const HostProcessesView = () => import('@/views/HostProcessesView.vue')
const HostUpdatesView = () => import('@/views/HostUpdatesView.vue')
const ServicesView = () => import('@/views/ServicesView.vue')
const AgentStatusView = () => import('@/views/AgentStatusView.vue')

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: LoginView,
    },
    {
      path: '/',
      component: AppShell,
      meta: { requiresAuth: true },
      children: [
        {
          path: '',
          name: 'overview',
          component: OverviewView,
        },
        { path: 'portal', name: 'portal', component: PortalView, meta: { title: '我的门户' } },
        {
          path: 'settings/notifications',
          name: 'notification-settings',
          component: NotificationSettingsView,
          meta: { administratorOnly: true, title: '通知管理' },
        },
        {
          path: 'services',
          name: 'services',
          component: ServicesView,
          meta: { title: '内部服务' },
        },
        {
          path: 'containers',
          name: 'containers',
          component: ContainersView,
          meta: { administratorOnly: true, title: '容器' },
        },
        {
          path: 'docker/images',
          name: 'images',
          component: ResourceListView,
          meta: { administratorOnly: true, title: '镜像', endpoint: 'images' },
        },
        {
          path: 'docker/networks',
          name: 'networks',
          component: ResourceListView,
          meta: { administratorOnly: true, title: '网络', endpoint: 'networks' },
        },
        {
          path: 'docker/volumes',
          name: 'volumes',
          component: ResourceListView,
          meta: { administratorOnly: true, title: '数据卷', endpoint: 'volumes' },
        },
        {
          path: 'docker/settings',
          name: 'docker-settings',
          component: DockerSettingsView,
          meta: { administratorOnly: true, title: 'Docker 设置' },
        },
        {
          path: 'compose-projects',
          name: 'compose-projects',
          component: () => import('@/views/ComposeProjectsView.vue'),
          meta: { administratorOnly: true, title: 'Compose 项目', endpoint: 'compose-projects' },
        },
        {
          path: 'databases',
          name: 'databases',
          component: DatabasesView,
          meta: { administratorOnly: true, title: '数据库平台' },
        },
        {
          path: 'applications',
          name: 'applications',
          component: ApplicationsView,
          meta: { administratorOnly: true, title: '应用' },
        },
        {
          path: 'applications/:id',
          name: 'application-detail',
          component: ApplicationsView,
          meta: { administratorOnly: true, title: '应用详情' },
        },
        {
          path: 'systemd',
          name: 'systemd',
          component: ResourceListView,
          meta: { administratorOnly: true, title: '系统服务', endpoint: 'systemd' },
        },
        {
          path: 'host/resources',
          name: 'host-resources',
          component: HostResourcesView,
          meta: { administratorOnly: true, title: '主机设备' },
        },
        {
          path: 'host/logs',
          name: 'host-logs',
          component: HostLogsView,
          meta: { administratorOnly: true, title: '系统日志' },
        },
        {
          path: 'host/processes',
          name: 'host-processes',
          component: HostProcessesView,
          meta: { administratorOnly: true, title: '主机进程' },
        },
        {
          path: 'host/updates',
          name: 'host-updates',
          component: HostUpdatesView,
          meta: { administratorOnly: true, title: '系统更新' },
        },
        {
          path: 'operations/agents',
          name: 'agents',
          component: AgentStatusView,
          meta: { administratorOnly: true, title: '宿主机 Agent' },
        },
        {
          path: 'identity/users',
          name: 'users',
          component: IdentityView,
          meta: { administratorOnly: true, title: 'Authentik 用户', identityMode: 'users' },
        },
        {
          path: 'identity/groups',
          name: 'groups',
          component: IdentityView,
          meta: { administratorOnly: true, title: 'Authentik 用户组', identityMode: 'groups' },
        },
        {
          path: 'identity/sso',
          name: 'sso',
          component: IdentityView,
          meta: { administratorOnly: true, title: 'SSO 应用', identityMode: 'sso' },
        },
        {
          path: 'jobs',
          name: 'jobs',
          component: JobsView,
          meta: { administratorOnly: true, title: '任务中心' },
        },
        {
          path: 'schedules',
          name: 'schedules',
          component: SchedulesView,
          meta: { administratorOnly: true, title: '计划任务' },
        },
        {
          path: 'backups',
          name: 'backups',
          component: BackupsView,
          meta: { administratorOnly: true, title: '备份策略' },
        },
        {
          path: 'alerts',
          name: 'alerts',
          component: AlertsView,
          meta: { administratorOnly: true, title: '告警' },
        },
        {
          path: 'audit',
          name: 'audit',
          component: RecordsView,
          meta: { administratorOnly: true, title: '审计日志', endpoint: 'audit?take=200' },
        },
        {
          path: 'settings',
          name: 'settings',
          component: SettingsView,
          meta: { administratorOnly: true, title: '平台设置' },
        },
        {
          path: 'settings/config-repository',
          name: 'config-repository',
          component: ConfigRepositoryView,
          meta: { administratorOnly: true, title: '配置仓库' },
        },
        {
          path: 'platform/maintenance',
          name: 'platform-maintenance',
          component: PlatformMaintenanceView,
          meta: { administratorOnly: true, title: '平台维护' },
        },
      ],
    },
    {
      path: '/forbidden',
      name: 'forbidden',
      component: StateView,
      props: {
        code: '403',
        title: '没有管理权限',
        description: '当前账号可以使用首页和个人门户，但不能管理工作站。',
      },
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: StateView,
      props: {
        code: '404',
        title: '页面不存在',
        description: '这个地址没有对应的 Whale Deck 页面。',
      },
    },
  ],
})

router.beforeEach((to) => {
  if (authSession.initialized.value && to.meta.requiresAuth && !authSession.authenticated.value) {
    return { name: 'login', query: { returnUrl: to.fullPath } }
  }

  if (authSession.initialized.value && to.name === 'login' && authSession.authenticated.value) {
    return { name: 'overview' }
  }

  if (
    authSession.initialized.value &&
    to.meta.administratorOnly &&
    !authSession.user.value?.isAdministrator
  ) {
    return { name: 'forbidden' }
  }
})

export default router
