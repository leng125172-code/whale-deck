<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue'
import { Box } from '@element-plus/icons-vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest } from '@/services/apiClient'
import { enqueueOperation, planOperation, stageSecret } from '@/services/operations'
import { authSession } from '@/services/authSession'
import type {
  ApplicationImageMetadata,
  ApplicationDetail,
  ApplicationInstallation,
  CatalogApplication,
  ManagedResource,
} from '@/services/contracts'

const catalog = ref<CatalogApplication[]>([])
const route = useRoute()
const router = useRouter()
const installed = ref<ManagedResource[]>([])
const installations = ref<ApplicationInstallation[]>([])
const detail = ref<ApplicationDetail | null>(null)
const detailVisible = ref(false)
const detailLoading = ref(false)
const stale = ref(false)
const loading = ref(false)
const installVisible = ref(false)
const submitting = ref(false)
const inspectingImage = ref(false)
const imageMetadata = ref<ApplicationImageMetadata | null>(null)
const activeTab = ref('installed')
const search = ref('')
const sort = ref('name')
const pendingAction = ref('')
const loadError = ref('')
let loadGeneration = 0
let detailGeneration = 0
const detailMedia = window.matchMedia('(max-width: 600px)')
const compactDetail = ref(detailMedia.matches)
function updateDetailLayout() {
  compactDetail.value = detailMedia.matches
}
onUnmounted(() => {
  loadGeneration++
  detailGeneration++
  detailMedia.removeEventListener('change', updateDetailLayout)
})
const visibleInstalled = computed(() =>
  installed.value
    .filter((item) => {
      const record = installationFor(item)
      const text = [item.name, item.version, record?.displayName, item.attributes.composeProject]
        .join(' ')
        .toLowerCase()
      return (
        text.includes(search.value.trim().toLowerCase()) &&
        (activeTab.value !== 'updates' || hasUpgrade(item))
      )
    })
    .sort((left, right) =>
      sort.value === 'recent'
        ? (installationFor(right)?.installedAtUtc || '').localeCompare(
            installationFor(left)?.installedAtUtc || '',
          )
        : left.name.localeCompare(right.name),
    ),
)
const visibleCatalog = computed(() =>
  catalog.value.filter((item) =>
    `${item.name} ${item.description} ${item.image}`
      .toLowerCase()
      .includes(search.value.trim().toLowerCase()),
  ),
)
function isManaged(item: ManagedResource) {
  return (
    !item.isProtected && item.type === 'ComposeApplication' && item.id.startsWith('application:')
  )
}
function hasUpgrade(item: ManagedResource) {
  const record = installationFor(item)
  return (
    isManaged(item) && !!record?.desiredVersion && record.desiredVersion !== record.installedVersion
  )
}
function isRunning(item: ManagedResource) {
  return item.state.toLowerCase() === 'running'
}
function stateLabel(item: ManagedResource) {
  return (
    (
      {
        running: '已启动',
        stopped: '已停止',
        exited: '已停止',
        restarting: '重启中',
        degraded: '异常',
      } as Record<string, string>
    )[item.state.toLowerCase()] || item.state
  )
}
function catalogFor(item: ManagedResource) {
  return catalog.value.find((entry) => entry.id === installationFor(item)?.catalogAppId)
}
function relatedContainers(item: ManagedResource) {
  return router.push({
    path: '/containers',
    query: {
      project: item.attributes.composeProject || item.id.replace(/^(application|compose):/, ''),
    },
  })
}
const form = reactive({
  slug: '',
  displayName: '',
  image: '',
  ports: '',
  environment: '',
  autoupdate: false,
  versionRange: '*',
  maintenanceWindow: 'Sun@20:00-23:59',
  dependencies: '',
})

async function load() {
  const generation = ++loadGeneration
  loading.value = true
  loadError.value = ''
  try {
    const [popular, apps, records] = await Promise.all([
      apiRequest<{ applications: CatalogApplication[]; isStale: boolean }>('applications/popular'),
      apiRequest<ManagedResource[]>('applications'),
      apiRequest<ApplicationInstallation[]>('applications/installations'),
    ])
    if (generation !== loadGeneration) return
    if (!Array.isArray(popular.applications) || !Array.isArray(apps) || !Array.isArray(records))
      throw new Error('应用接口响应格式无效，请重试或检查后端状态。')
    catalog.value = popular.applications
    stale.value = popular.isStale
    installed.value = apps
    installations.value = records
  } catch (error) {
    if (generation !== loadGeneration) return
    loadError.value = error instanceof Error ? error.message : '无法加载应用'
    if (error instanceof ApiError && [401, 403].includes(error.status)) {
      clearPrivateData()
    }
  } finally {
    if (generation === loadGeneration) loading.value = false
  }
}

function openInstall(item?: CatalogApplication) {
  Object.assign(form, {
    slug: item?.id || '',
    displayName: item?.name || '',
    image: item?.image || '',
    ports: '',
    environment: '',
    autoupdate: false,
    versionRange: '*',
    maintenanceWindow: 'Sun@20:00-23:59',
    dependencies: '',
  })
  imageMetadata.value = null
  installVisible.value = true
}

async function inspectImage() {
  if (!form.image.trim()) return
  inspectingImage.value = true
  try {
    imageMetadata.value = await apiRequest<ApplicationImageMetadata>(
      'applications/image-metadata',
      {
        method: 'POST',
        signal: AbortSignal.timeout(180_000),
        body: JSON.stringify({ image: form.image.trim(), pullIfMissing: true }),
      },
    )
    if (!form.environment.trim() && imageMetadata.value.environment.length) {
      form.environment = imageMetadata.value.environment.join('\n')
    }
    ElMessage.success('已读取 OCI 镜像声明')
  } finally {
    inspectingImage.value = false
  }
}

async function install() {
  submitting.value = true
  try {
    const parameters: Record<string, string> = { slug: form.slug.trim(), image: form.image.trim() }
    parameters.catalogAppId = form.slug.trim()
    parameters.templateId = itemTemplateId()
    parameters.templateVersion = '1'
    parameters.displayName = form.displayName.trim() || form.slug.trim()
    parameters.autoupdate = String(form.autoupdate)
    parameters.versionRange = form.versionRange.trim() || '*'
    parameters.maintenanceWindow = form.maintenanceWindow.trim()
    if (form.dependencies.trim()) parameters.dependencies = form.dependencies.trim()
    if (form.ports.trim()) parameters.ports = form.ports.trim()
    if (form.environment.trim()) {
      const ticket = await stageSecret(form.environment)
      parameters.inputTicket = ticket.token
      parameters.inputKind = 'environment'
    }
    const plan = await planOperation('applications', 'install', '', parameters)
    await ElMessageBox.confirm(
      [...plan.changes, ...plan.warnings].join('\n') || '将生成受控 Compose 配置并启动应用。',
      '安装确认',
      { type: 'warning', confirmButtonText: '安装' },
    )
    const job = await enqueueOperation(
      'applications',
      'install',
      '',
      parameters,
      true,
      plan.planHash,
    )
    installVisible.value = false
    ElMessage.success(`安装任务已提交：${job.id}`)
  } finally {
    submitting.value = false
  }
}

function itemTemplateId() {
  return catalog.value.some((item) => item.id === form.slug.trim()) ? 'xuanyuan' : 'custom-oci'
}

function installationFor(item: ManagedResource) {
  const slug = item.id.replace(/^application:/, '')
  return installations.value.find((record) => record.catalogAppId === slug)
}

async function openDetail(id: string) {
  if (detailVisible.value && detail.value?.installation.id === id) return
  const generation = ++detailGeneration
  detailLoading.value = true
  detailVisible.value = true
  detail.value = null
  try {
    const response = await apiRequest<ApplicationDetail>(
      `applications/installations/${encodeURIComponent(id)}`,
    )
    if (generation !== detailGeneration || !detailVisible.value) return
    detail.value = response
    if (route.params.id !== id) {
      const target = { name: 'application-detail', params: { id } }
      if (route.name === 'application-detail') await router.replace(target)
      else await router.push(target)
    }
  } catch (error) {
    if (generation !== detailGeneration) return
    if (error instanceof ApiError && [401, 403].includes(error.status)) clearPrivateData()
    ElMessage.error(error instanceof Error ? error.message : '应用详情读取失败')
  } finally {
    if (generation === detailGeneration) detailLoading.value = false
  }
}

async function syncDetailFromRoute() {
  const id = typeof route.params.id === 'string' ? route.params.id : ''
  if (!id || loadError.value || loading.value) return
  const resource = installed.value.find((item) => item.id === id && isManaged(item))
  const record = resource
    ? installationFor(resource)
    : installations.value.find((item) => item.id === id)
  if (record) await openDetail(record.id)
  else ElMessage.warning('应用安装记录尚未同步或已被移除，请刷新后重试。')
}

async function closeDetail() {
  detailGeneration++
  detailLoading.value = false
  detailVisible.value = false
  detail.value = null
  if (route.name === 'application-detail') await router.push({ name: 'applications' })
}

function clearPrivateData() {
  loadGeneration++
  detailGeneration++
  catalog.value = []
  installed.value = []
  installations.value = []
  detail.value = null
  detailVisible.value = false
  detailLoading.value = false
  installVisible.value = false
  imageMetadata.value = null
  form.environment = ''
  loading.value = false
}
watch(
  () => [authSession.user.value?.subject, authSession.user.value?.isAdministrator],
  () => {
    clearPrivateData()
    if (authSession.user.value?.isAdministrator) void load()
  },
)

async function lifecycle(
  item: ManagedResource,
  action: 'start' | 'stop' | 'restart' | 'update' | 'reinstall' | 'uninstall',
) {
  if (!isManaged(item) || pendingAction.value) return
  pendingAction.value = item.id
  try {
    const slug = item.id.replace(/^application:/, '')
    const parameters = { slug, deleteVolumes: 'false' }
    let planHash: string | null = null
    if (['update', 'reinstall', 'uninstall'].includes(action)) {
      const plan = await planOperation('applications', action, '', parameters)
      planHash = plan.planHash
      await ElMessageBox.confirm(
        [...plan.changes, ...plan.warnings].join('\n') || `${action} ${slug}，卸载默认保留数据卷。`,
        '应用操作确认',
        { type: action === 'uninstall' ? 'error' : 'warning' },
      )
    } else if (action !== 'start') {
      await ElMessageBox.confirm(
        `确认${action === 'stop' ? '停止' : '重启'} ${slug}？`,
        '应用操作确认',
        { type: 'warning' },
      )
    }
    const job = await enqueueOperation('applications', action, '', parameters, true, planHash)
    ElMessage.success(`任务已提交：${job.id}`)
  } catch (error) {
    if (error !== 'cancel' && error !== 'close')
      ElMessage.error(error instanceof Error ? error.message : '应用操作失败')
  } finally {
    pendingAction.value = ''
  }
}

onMounted(async () => {
  detailMedia.addEventListener('change', updateDetailLayout)
  await load()
  if (route.query.action === 'install' && typeof route.query.app === 'string') {
    const item = catalog.value.find((candidate) => candidate.id === route.query.app)
    if (item && !item.installed) openInstall(item)
  }
  await syncDetailFromRoute()
})

watch(
  () => route.params.id,
  async () => {
    if (route.name === 'application-detail') await syncDetailFromRoute()
    else await closeDetail()
  },
)
</script>

<template>
  <div class="applications-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">APPLICATIONS</span>
        <h1>应用管理</h1>
        <p>安装模板来自轩辕热门目录；应用写入独立目录并受 Whale Deck 管理。</p>
      </div>
      <div class="page-heading__actions">
        <el-button :loading="loading" @click="load">刷新</el-button
        ><el-button type="primary" @click="openInstall()">自定义安装</el-button>
      </div>
    </header>
    <el-alert v-if="loadError" :title="loadError" type="error" :closable="false" show-icon />
    <el-alert
      v-if="stale"
      title="热门目录当前使用最近一次有效快照"
      type="warning"
      show-icon
      :closable="false"
    />
    <section class="panel app-toolbar">
      <el-radio-group v-model="activeTab" aria-label="应用视图">
        <el-radio-button value="installed">已安装</el-radio-button>
        <el-radio-button value="catalog">热门目录</el-radio-button>
        <el-radio-button value="updates">待更新</el-radio-button>
      </el-radio-group>
      <div class="app-toolbar__search">
        <el-select v-if="activeTab !== 'catalog'" v-model="sort" aria-label="应用排序"
          ><el-option label="名称排序" value="name" /><el-option label="最近安装" value="recent"
        /></el-select>
        <el-input v-model="search" placeholder="搜索名称或镜像" clearable aria-label="搜索应用" />
      </div>
    </section>
    <section v-if="activeTab === 'catalog'" class="panel app-section">
      <div class="panel__header">
        <div>
          <span class="panel__eyebrow">XUANYUAN</span>
          <h2>热门应用</h2>
        </div>
      </div>
      <div class="catalog-grid">
        <article v-for="item in visibleCatalog" :key="item.id" class="catalog-card">
          <div class="catalog-card__icon">
            <img v-if="item.iconUrl" :src="item.iconUrl" alt="" /><el-icon v-else :size="28"
              ><Box
            /></el-icon>
          </div>
          <div>
            <strong>{{ item.name }}</strong>
            <p>{{ item.description }}</p>
            <small>{{ item.version }} · {{ item.image }}</small>
          </div>
          <el-button type="primary" plain :disabled="item.installed" @click="openInstall(item)">{{
            item.installed ? '已安装' : '安装'
          }}</el-button>
        </article>
      </div>
    </section>
    <section v-else class="panel app-section">
      <div class="panel__header">
        <div>
          <span class="panel__eyebrow">INSTALLED</span>
          <h2>
            {{ activeTab === 'updates' ? '待更新应用' : '已安装应用' }}
            <small>{{ visibleInstalled.length }}</small>
          </h2>
        </div>
      </div>
      <el-empty
        v-if="!loading && visibleInstalled.length === 0"
        :description="activeTab === 'updates' ? '暂无已确认的版本变更' : '没有匹配的应用'"
      />
      <div v-else class="installed-list">
        <article v-for="item in visibleInstalled" :key="item.id" class="installed-row">
          <div class="installed-row__icon catalog-card__icon">
            <img
              v-if="catalogFor(item)?.iconUrl"
              :src="catalogFor(item)?.iconUrl || undefined"
              alt=""
            /><el-icon v-else :size="30"><Box /></el-icon>
          </div>
          <div class="installed-row__content">
            <div class="installed-row__title">
              <strong>{{ installationFor(item)?.displayName || item.name }}</strong>
              <el-tag size="small" :type="isRunning(item) ? 'success' : 'info'">{{
                stateLabel(item)
              }}</el-tag>
              <el-tag v-if="!isManaged(item)" size="small" type="warning" effect="plain">{{
                item.isProtected ? '受保护' : '未注册 · 只读'
              }}</el-tag>
            </div>
            <p v-if="catalogFor(item)?.description">{{ catalogFor(item)?.description }}</p>
            <small class="installed-row__image" :title="item.version"
              >镜像 / 版本：{{
                installationFor(item)?.installedVersion || item.version || '未提供'
              }}</small
            >
            <small v-if="item.attributes.containerCount"
              >关联容器：{{ item.attributes.containerCount }}</small
            >
            <small
              v-if="item.attributes.ports"
              :title="item.attributes.ports"
              class="installed-row__image"
              >端口：{{ item.attributes.ports }}</small
            >
            <small v-if="installationFor(item)"
              >安装于 {{ new Date(installationFor(item)!.installedAtUtc).toLocaleString() }} ·
              自动更新{{ installationFor(item)!.autoUpdateEnabled ? '已开启' : '已关闭' }}</small
            >
          </div>
          <div class="installed-row__actions">
            <el-button link type="primary" @click="relatedContainers(item)">关联容器</el-button>
            <el-button
              v-if="installationFor(item)"
              link
              type="primary"
              @click="openDetail(installationFor(item)?.id || '')"
              >详情</el-button
            >
            <template v-if="isManaged(item)">
              <el-button
                link
                :type="isRunning(item) ? 'danger' : 'primary'"
                :disabled="!!pendingAction"
                @click="lifecycle(item, isRunning(item) ? 'stop' : 'start')"
                >{{ isRunning(item) ? '停止' : '启动' }}</el-button
              >
              <el-button
                link
                :disabled="!isRunning(item) || !!pendingAction"
                @click="lifecycle(item, 'restart')"
                >重启</el-button
              >
              <el-dropdown
                trigger="click"
                :disabled="!!pendingAction"
                @command="lifecycle(item, $event)"
              >
                <el-button link type="primary">更多</el-button>
                <template #dropdown
                  ><el-dropdown-menu
                    ><el-dropdown-item command="update">更新</el-dropdown-item
                    ><el-dropdown-item command="reinstall">重装</el-dropdown-item
                    ><el-dropdown-item command="uninstall" divided
                      >卸载（保留数据卷）</el-dropdown-item
                    ></el-dropdown-menu
                  ></template
                >
              </el-dropdown>
            </template>
          </div>
        </article>
      </div>
    </section>

    <el-dialog v-model="installVisible" title="安装应用" width="min(640px, calc(100vw - 32px))">
      <el-form label-position="top"
        ><el-form-item label="应用标识"
          ><el-input v-model="form.slug" placeholder="my-app" /></el-form-item
        ><el-form-item label="显示名称"
          ><el-input v-model="form.displayName" placeholder="My App" /></el-form-item
        ><el-form-item label="OCI 镜像"
          ><el-input v-model="form.image"
            ><template #append
              ><el-button :loading="inspectingImage" @click="inspectImage"
                >分析镜像</el-button
              ></template
            ></el-input
          ></el-form-item
        ><el-alert
          v-if="imageMetadata"
          class="image-metadata"
          type="info"
          :closable="false"
          show-icon
        >
          <template #title
            >镜像声明已载入，未显式覆盖的 Entrypoint / Cmd 会由 OCI 镜像继承。</template
          >
          <div class="image-metadata__grid">
            <span>端口：{{ imageMetadata.exposedPorts.join(', ') || '无' }}</span>
            <span>卷：{{ imageMetadata.volumes.join(', ') || '无' }}</span>
            <span>Entrypoint：{{ imageMetadata.entrypoint.join(' ') || '默认' }}</span>
            <span>Cmd：{{ imageMetadata.command.join(' ') || '默认' }}</span>
          </div> </el-alert
        ><el-form-item label="端口映射"
          ><el-input v-model="form.ports" placeholder="0.0.0.0:8088:80/tcp" /><small
            class="form-tip"
            >多个映射用逗号分隔；0.0.0.0 对所有网卡开放，127.0.0.1 仅供本机访问。</small
          ></el-form-item
        ><el-form-item label="环境变量（可选）"
          ><el-input
            v-model="form.environment"
            type="textarea"
            :rows="5"
            placeholder="KEY=value"
          /><small class="form-tip"
            >内容经一次性 Secret 传输，不写入任务或审计。</small
          ></el-form-item
        ><el-form-item label="自动更新"
          ><el-switch v-model="form.autoupdate" /><small class="form-tip"
            >只有启用后写入 autoupdate 标签，计划任务才允许更新此应用。</small
          ></el-form-item
        ><template v-if="form.autoupdate"
          ><el-form-item label="允许版本范围"
            ><el-input v-model="form.versionRange" placeholder="*、1.4.*、>=1.4.0 或 1.4.2" /><small
              class="form-tip"
              >镜像无可识别版本时只能使用 *；更新前会再次校验。</small
            ></el-form-item
          ><el-form-item label="维护窗口"
            ><el-input v-model="form.maintenanceWindow" placeholder="Sun@20:00-23:59" /><small
              class="form-tip"
              >使用 Asia/Shanghai 时区，自动更新仅在该窗口执行。</small
            ></el-form-item
          ><el-form-item label="依赖应用（可选）"
            ><el-input
              v-model="form.dependencies"
              placeholder="postgres-exporter,metrics-gateway"
            /><small class="form-tip">逗号分隔；依赖未安装时自动更新会被阻止。</small></el-form-item
          ></template
        ></el-form
      >
      <template #footer
        ><el-button @click="installVisible = false">取消</el-button
        ><el-button
          type="primary"
          :loading="submitting"
          :disabled="!form.slug || !form.image"
          @click="install"
          >预检并安装</el-button
        ></template
      >
    </el-dialog>

    <el-drawer
      v-model="detailVisible"
      title="应用详情"
      size="min(760px, 96vw)"
      destroy-on-close
      @closed="closeDetail"
    >
      <div v-loading="detailLoading" class="application-detail">
        <template v-if="detail">
          <el-descriptions :column="compactDetail ? 1 : 2" border>
            <el-descriptions-item label="应用">{{
              detail.installation.displayName
            }}</el-descriptions-item>
            <el-descriptions-item label="状态">{{
              detail.installation.state
            }}</el-descriptions-item>
            <el-descriptions-item label="当前版本">{{
              detail.installation.installedVersion
            }}</el-descriptions-item>
            <el-descriptions-item label="自动更新">{{
              detail.installation.autoUpdateEnabled ? '启用' : '关闭'
            }}</el-descriptions-item>
            <el-descriptions-item label="模板"
              >{{ detail.installation.templateId }} /
              {{ detail.installation.templateVersion }}</el-descriptions-item
            >
            <el-descriptions-item label="安装时间">{{
              new Date(detail.installation.installedAtUtc).toLocaleString()
            }}</el-descriptions-item>
          </el-descriptions>

          <section>
            <h3>关联资源</h3>
            <el-table :data="detail.resources" empty-text="暂无关联资源">
              <el-table-column prop="role" label="角色" width="130" />
              <el-table-column prop="name" label="资源" min-width="160" />
              <el-table-column prop="type" label="类型" width="140" />
              <el-table-column prop="state" label="状态" width="110" />
              <el-table-column
                prop="version"
                label="版本 / 镜像"
                min-width="200"
                show-overflow-tooltip
              />
            </el-table>
          </section>

          <section>
            <h3>更新历史</h3>
            <el-table :data="detail.updateHistory" empty-text="暂无更新记录">
              <el-table-column label="时间" min-width="170">
                <template #default="{ row }">{{
                  new Date(row.startedAtUtc).toLocaleString()
                }}</template>
              </el-table-column>
              <el-table-column prop="result" label="结果" width="100" />
              <el-table-column prop="versionPolicy" label="版本策略" width="110" />
              <el-table-column
                prop="newImageDigest"
                label="镜像摘要"
                min-width="230"
                show-overflow-tooltip
              />
              <el-table-column label="回滚" width="80">
                <template #default="{ row }">{{ row.wasRolledBack ? '是' : '否' }}</template>
              </el-table-column>
            </el-table>
          </section>
        </template>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.applications-page {
  display: grid;
  gap: 18px;
  min-width: 0;
}
.app-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 18px;
}
.app-toolbar__search {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  max-width: 500px;
  min-width: 0;
  flex: 1 1 320px;
}
.app-toolbar__search .el-select {
  flex: 1 1 130px;
  min-width: 0;
}
.app-toolbar__search .el-input {
  flex: 2 1 180px;
  min-width: 0;
}
.app-section {
  overflow: hidden;
}
.catalog-grid,
.installed-list {
  display: grid;
}
.catalog-card {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 14px;
  padding: 14px 18px;
  border-bottom: 1px solid var(--el-border-color-extra-light);
}
.catalog-card:last-child {
  border-bottom: 0;
}
.catalog-card__icon {
  display: grid;
  width: 46px;
  height: 46px;
  place-items: center;
  border-radius: 10px;
  background: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
  font-weight: 700;
}
.catalog-card__icon img {
  width: 34px;
  height: 34px;
  object-fit: contain;
}
.catalog-card p {
  margin: 3px 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.catalog-card small,
.installed-row small,
.form-tip {
  display: block;
  color: var(--el-text-color-placeholder);
  font-size: 11px;
}
.installed-list {
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 420px), 1fr));
  gap: 16px;
  padding: 18px;
}
.installed-row {
  display: grid;
  grid-template-columns: 52px minmax(0, 1fr);
  align-items: start;
  gap: 12px;
  padding: 16px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
  min-width: 0;
}
.installed-row__content {
  display: grid;
  gap: 5px;
  min-width: 0;
}
.installed-row__title {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.installed-row__title strong {
  font-size: 14px;
  overflow-wrap: anywhere;
}
.installed-row__title .el-tag {
  flex: 0 0 auto;
}
.installed-row__content p {
  margin: 0;
  font-size: 13px;
  color: var(--el-text-color-secondary);
  overflow-wrap: anywhere;
}
.installed-row__image {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.installed-row small {
  font-size: 12px;
  overflow-wrap: anywhere;
}
.installed-row__actions {
  grid-column: 1 / -1;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--el-border-color-extra-light);
}
.installed-row__actions .el-button + .el-button {
  margin-left: 0;
}
.image-metadata {
  margin-bottom: 14px;
}
.image-metadata__grid {
  display: grid;
  gap: 4px;
  margin-top: 8px;
  overflow-wrap: anywhere;
  color: var(--el-text-color-regular);
  font-size: 12px;
}
.application-detail {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 22px;
  min-height: 180px;
}
.application-detail > * {
  min-width: 0;
}
.application-detail :deep(.el-descriptions__table) {
  table-layout: fixed;
}
.application-detail :deep(.el-descriptions__label) {
  width: 90px;
}
.application-detail :deep(.el-descriptions__content) {
  overflow-wrap: anywhere;
}
.application-detail h3 {
  margin: 0 0 10px;
  font-size: 14px;
}
@media (max-width: 760px) {
  .catalog-card {
    grid-template-columns: auto minmax(0, 1fr);
  }
  .catalog-card > .el-button {
    grid-column: 2;
  }
  .installed-list {
    padding: 12px;
  }
}
</style>
