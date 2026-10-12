<script setup lang="ts">
import { computed, nextTick, onUnmounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError, apiRequest } from '@/services/apiClient'
import { enqueueOperation, stageSecret, type OperationPlan } from '@/services/operations'
import { authSession } from '@/services/authSession'
import type { ApplicationConfiguration, ApplicationConfigurationMount } from '@/services/contracts'

const props = defineProps<{ modelValue: boolean; resourceId: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean]; submitted: [jobId: string] }>()
type OverrideMode = 'keep' | 'replace' | 'default'
interface ServiceForm {
  name: string
  image: string
  cpus: number
  memoryMb: number
  restartPolicy: string
  portsText: string
  networks: string[]
  networkMode: string
  mounts: ApplicationConfigurationMount[]
  commandMode: OverrideMode
  commandText: string
  entrypointMode: OverrideMode
  entrypointText: string
}
const snapshot = ref<ApplicationConfiguration | null>(null)
const services = ref<ServiceForm[]>([])
const activeService = ref('')
const loading = ref(false)
const submitting = ref(false)
const error = ref('')
const editorBody = ref<HTMLElement | null>(null)
watch(error, async (value) => {
  if (value) {
    await nextTick()
    editorBody.value?.scrollTo({ top: 0, behavior: 'instant' })
  }
})
const autoUpdate = ref(false)
const versionPolicy = ref('*')
const maintenanceWindow = ref('Sun@20:00-23:59')
const dependencies = ref('')
const apply = ref(false)
const environment = ref<{ name: string; value: string }[]>([])
const removeEnvironment = ref<string[]>([])
let generation = 0
const slug = computed(() => props.resourceId.replace(/^application:/, ''))
const environmentNames = computed(() =>
  [
    ...new Set([
      ...(snapshot.value?.environmentNames ?? []),
      ...(snapshot.value?.services.flatMap((item) => item.environmentNames) ?? []),
    ]),
  ].sort(),
)
const lines = (value: string) =>
  value
    .split('\n')
    .map((item) => item.trim())
    .filter(Boolean)
const same = (left: unknown, right: unknown) => JSON.stringify(left) === JSON.stringify(right)

function clear() {
  generation++
  snapshot.value = null
  services.value = []
  environment.value = []
  removeEnvironment.value = []
  loading.value = false
  error.value = ''
}
function close() {
  if (!submitting.value) emit('update:modelValue', false)
}
async function load() {
  const current = ++generation
  loading.value = true
  error.value = ''
  snapshot.value = null
  services.value = []
  environment.value = []
  removeEnvironment.value = []
  try {
    const result = await apiRequest<ApplicationConfiguration>(
      `applications/configuration?resourceId=${encodeURIComponent(props.resourceId)}`,
    )
    if (current !== generation || !props.modelValue) return
    snapshot.value = result
    services.value = result.services.map((item) => ({
      name: item.name,
      image: item.image,
      cpus: item.cpus,
      memoryMb: item.memoryMb,
      restartPolicy: item.restartPolicy,
      portsText: item.ports.join('\n'),
      networks: [...item.networks],
      networkMode: item.networkMode,
      mounts: item.mounts.map((mount) => ({ ...mount })),
      commandMode: 'keep',
      commandText: '',
      entrypointMode: 'keep',
      entrypointText: '',
    }))
    activeService.value = services.value[0]?.name ?? ''
    autoUpdate.value = result.autoUpdate
    versionPolicy.value = result.versionPolicy
    maintenanceWindow.value = result.maintenanceWindow
    dependencies.value = result.dependencies.join(',')
    apply.value = false
  } catch (reason) {
    if (current !== generation) return
    error.value = reason instanceof Error ? reason.message : '应用配置读取失败'
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
      clear()
      emit('update:modelValue', false)
      ElMessage.error('管理权限已失效，配置输入已清除。')
    }
  } finally {
    if (current === generation) loading.value = false
  }
}
function command(value: string): string[] {
  let parsed: unknown
  try {
    parsed = JSON.parse(value)
  } catch {
    throw new Error('命令与 Entrypoint 请填写 JSON 字符串数组，例如 ["app", "--option"]。')
  }
  if (!Array.isArray(parsed) || parsed.some((item) => typeof item !== 'string'))
    throw new Error('命令与 Entrypoint 只能是字符串数组。')
  return parsed as string[]
}
function candidate() {
  if (!snapshot.value) throw new Error('请先读取配置。')
  const patches: Record<string, Record<string, unknown>> = {}
  for (const item of services.value) {
    const source = snapshot.value.services.find((service) => service.name === item.name)!
    const patch: Record<string, unknown> = {}
    for (const field of ['image', 'cpus', 'memoryMb', 'restartPolicy', 'networkMode'] as const)
      if (item[field] !== source[field]) patch[field] = item[field]
    if (!same(lines(item.portsText), source.ports)) patch.ports = lines(item.portsText)
    if (!same(item.networks, source.networks)) patch.networks = item.networks
    if (!same(item.mounts, source.mounts)) patch.volumes = item.mounts
    for (const field of ['command', 'entrypoint'] as const) {
      const mode = item[field === 'command' ? 'commandMode' : 'entrypointMode']
      if (mode !== 'keep')
        patch[field] =
          mode === 'default'
            ? null
            : command(item[field === 'command' ? 'commandText' : 'entrypointText'])
    }
    if (Object.keys(patch).length) patches[item.name] = patch
  }
  const result: Record<string, unknown> = { services: patches, apply: apply.value }
  if (environment.value.length || removeEnvironment.value.length) {
    const set: Record<string, string> = {}
    for (const item of environment.value) {
      const name = item.name.trim()
      if (
        !/^[A-Za-z_][A-Za-z0-9_]{0,127}$/.test(name) ||
        name in set ||
        removeEnvironment.value.includes(name)
      )
        throw new Error('环境变量名称无效、重复或同时被设置与移除。')
      set[name] = item.value
    }
    result.environment = { set, remove: removeEnvironment.value }
  }
  if (autoUpdate.value !== snapshot.value.autoUpdate) result.autoUpdate = autoUpdate.value
  if (versionPolicy.value !== snapshot.value.versionPolicy)
    result.versionPolicy = versionPolicy.value
  if (maintenanceWindow.value !== snapshot.value.maintenanceWindow)
    result.maintenanceWindow = maintenanceWindow.value
  const deps = dependencies.value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)
  if (!same(deps, snapshot.value.dependencies)) result.dependencies = deps
  return result
}
async function save() {
  if (!snapshot.value || submitting.value) return
  const current = generation
  const subject = authSession.user.value?.subject
  submitting.value = true
  error.value = ''
  try {
    const input = JSON.stringify(candidate())
    if (input.length > 65_536) throw new Error('配置输入过大，请减少命令或环境变量内容。')
    const ticket = await stageSecret(input)
    if (current !== generation || !props.modelValue) return
    const plan = await apiRequest<OperationPlan>('applications/configuration/plan', {
      method: 'POST',
      body: JSON.stringify({
        resourceId: props.resourceId,
        expectedFingerprint: snapshot.value.fingerprint,
        inputTicket: ticket.token,
      }),
    })
    await ElMessageBox.confirm(
      [...plan.changes, ...plan.warnings].join('\n'),
      apply.value ? '保存配置并重部署' : '仅保存配置',
      {
        type: 'warning',
        confirmButtonText: '确认提交',
        cancelButtonText: '取消',
        customClass: 'configuration-confirm',
      },
    )
    if (
      current !== generation ||
      !props.modelValue ||
      subject !== authSession.user.value?.subject ||
      !authSession.user.value?.isAdministrator
    )
      return
    const job = await enqueueOperation(
      'applications',
      'configure',
      props.resourceId,
      {
        slug: slug.value,
        expectedFingerprint: snapshot.value.fingerprint,
        inputTicket: ticket.token,
        inputKind: 'environment',
      },
      true,
      plan.planHash,
    )
    emit('submitted', job.id)
    emit('update:modelValue', false)
    ElMessage.success('配置任务已提交，可在任务中心查看结果。')
  } catch (reason) {
    if (current !== generation || reason === 'cancel' || reason === 'close') return
    error.value = reason instanceof Error ? reason.message : '配置提交失败，请重新读取后确认。'
    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
      clear()
      emit('update:modelValue', false)
    }
  } finally {
    submitting.value = false
  }
}
watch(
  () => [props.modelValue, props.resourceId],
  () => {
    if (props.modelValue) void load()
    else clear()
  },
  { immediate: true },
)
watch(
  () => [authSession.user.value?.subject, authSession.user.value?.isAdministrator],
  () => {
    clear()
    emit('update:modelValue', false)
  },
)
onUnmounted(clear)
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="应用参数"
    width="min(920px, 96vw)"
    top="5vh"
    destroy-on-close
    :close-on-click-modal="false"
    :close-on-press-escape="!submitting"
    :show-close="!submitting"
    @update:model-value="close"
  >
    <div ref="editorBody" class="configuration-editor" v-loading="loading">
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <el-button v-if="error" :disabled="submitting" @click="load">重新读取配置</el-button>
      <template v-if="snapshot">
        <el-alert
          title="只提交修改字段。已有密码、环境值和命令不回显；仅保存不会改变运行中的容器。"
          type="info"
          :closable="false"
        />
        <el-tabs v-model="activeService">
          <el-tab-pane
            v-for="service in services"
            :key="service.name"
            :name="service.name"
            :label="service.name"
          >
            <el-form label-position="top" :disabled="submitting" class="configuration-form">
              <el-form-item label="镜像" class="full"
                ><el-input v-model="service.image" :aria-label="`${service.name} 镜像`" />
                <small>保存并重部署仅使用已拉取镜像，不隐式构建或拉取。</small></el-form-item
              >
              <el-form-item label="CPU 上限（核，0 不限制）"
                ><el-input-number
                  v-model="service.cpus"
                  :min="0"
                  :max="4096"
                  :step="0.25"
                  :aria-label="`${service.name} CPU 上限`"
              /></el-form-item>
              <el-form-item label="内存上限（MiB，0 不限制）"
                ><el-input-number
                  v-model="service.memoryMb"
                  :min="0"
                  :max="1048576"
                  :step="64"
                  :aria-label="`${service.name} 内存上限`"
              /></el-form-item>
              <el-form-item label="重启策略"
                ><el-select
                  v-model="service.restartPolicy"
                  :aria-label="`${service.name} 重启策略`"
                >
                  <el-option
                    v-for="policy in ['no', 'always', 'unless-stopped', 'on-failure']"
                    :key="policy"
                    :label="policy"
                    :value="policy" /></el-select
              ></el-form-item>
              <el-form-item label="网络模式"
                ><el-select v-model="service.networkMode" :aria-label="`${service.name} 网络模式`"
                  ><el-option label="桥接 / 批准网络" value="bridge" /><el-option
                    label="禁用网络"
                    value="none" /></el-select
              ></el-form-item>
              <el-form-item label="端口映射（每行一项）" class="full"
                ><el-input
                  v-model="service.portsText"
                  type="textarea"
                  :rows="3"
                  :aria-label="`${service.name} 端口映射`"
                  placeholder="0.0.0.0:8088:80/tcp"
              /></el-form-item>
              <el-form-item v-if="service.networkMode !== 'none'" label="批准网络" class="full"
                ><el-select
                  v-model="service.networks"
                  multiple
                  :aria-label="`${service.name} 批准网络`"
                >
                  <el-option
                    v-for="network in snapshot.approvedNetworks"
                    :key="network"
                    :label="network"
                    :value="network" /></el-select
              ></el-form-item>
              <el-form-item label="Cmd 覆盖" class="full"
                ><small>{{
                  snapshot.services.find((item) => item.name === service.name)?.commandPresent
                    ? '当前有覆盖，原内容不回显。'
                    : '当前使用镜像默认 Cmd。'
                }}</small
                ><el-radio-group
                  v-model="service.commandMode"
                  :aria-label="`${service.name} Cmd 覆盖`"
                  ><el-radio-button value="keep">保留原设置</el-radio-button
                  ><el-radio-button value="replace">替换</el-radio-button
                  ><el-radio-button value="default">恢复镜像默认</el-radio-button></el-radio-group
                >
                <el-input
                  v-if="service.commandMode === 'replace'"
                  v-model="service.commandText"
                  type="textarea"
                  :rows="2"
                  :aria-label="`${service.name} Cmd 字符串数组`"
                  placeholder='["app", "--option"]'
              /></el-form-item>
              <el-form-item label="Entrypoint 覆盖" class="full"
                ><small>{{
                  snapshot.services.find((item) => item.name === service.name)?.entrypointPresent
                    ? '当前有覆盖，原内容不回显。'
                    : '当前使用镜像默认 Entrypoint。'
                }}</small
                ><el-radio-group
                  v-model="service.entrypointMode"
                  :aria-label="`${service.name} Entrypoint 覆盖`"
                  ><el-radio-button value="keep">保留原设置</el-radio-button
                  ><el-radio-button value="replace">替换</el-radio-button
                  ><el-radio-button value="default">恢复镜像默认</el-radio-button></el-radio-group
                >
                <el-input
                  v-if="service.entrypointMode === 'replace'"
                  v-model="service.entrypointText"
                  type="textarea"
                  :rows="2"
                  :aria-label="`${service.name} Entrypoint 字符串数组`"
                  placeholder='["/entrypoint.sh"]'
              /></el-form-item>
              <section class="full configuration-mounts">
                <h3>数据挂载</h3>
                <p>只允许本应用命名卷、固定 data 目录和 tmpfs；移除挂载不删除已有数据。</p>
                <div
                  v-for="(mount, index) in service.mounts"
                  :key="index"
                  class="configuration-mount"
                >
                  <el-select
                    v-model="mount.type"
                    :aria-label="`挂载 ${index + 1} 类型`"
                    @change="
                      mount.source =
                        mount.type === 'bind'
                          ? snapshot.ownedDataDirectory
                          : mount.type === 'tmpfs'
                            ? ''
                            : `whaledeck-app-${slug}-data`
                    "
                    ><el-option label="命名卷" value="volume" /><el-option
                      label="固定 data 目录"
                      value="bind" /><el-option label="tmpfs" value="tmpfs"
                  /></el-select>
                  <el-input
                    v-if="mount.type !== 'tmpfs'"
                    v-model="mount.source"
                    :readonly="mount.type === 'bind'"
                    :aria-label="`挂载 ${index + 1} 来源`"
                  />
                  <el-input
                    v-model="mount.target"
                    :aria-label="`挂载 ${index + 1} 容器路径`"
                    placeholder="/var/lib/app"
                  />
                  <el-checkbox v-model="mount.readOnly">只读</el-checkbox
                  ><el-button type="danger" plain @click="service.mounts.splice(index, 1)"
                    >移除挂载</el-button
                  >
                </div>
                <el-button
                  @click="
                    service.mounts.push({
                      type: 'volume',
                      source: `whaledeck-app-${slug}-data`,
                      target: '/data',
                      readOnly: false,
                    })
                  "
                  >添加挂载</el-button
                >
              </section>
            </el-form>
          </el-tab-pane>
        </el-tabs>
        <section class="configuration-environment">
          <h3>应用环境覆盖</h3>
          <p>
            已有名称：{{
              environmentNames.join('、') || '无'
            }}。修改覆盖对本应用所有服务生效；移除后可能使用镜像默认值。
          </p>
          <div v-for="(entry, index) in environment" :key="index" class="configuration-env-entry">
            <el-input
              v-model="entry.name"
              :disabled="submitting"
              :aria-label="`环境变量 ${index + 1} 名称`"
              placeholder="KEY"
            /><el-input
              v-model="entry.value"
              :disabled="submitting"
              type="textarea"
              :rows="2"
              :aria-label="`环境变量 ${index + 1} 新值`"
              placeholder="新值，支持空值与换行"
            /><el-button
              type="danger"
              plain
              :disabled="submitting"
              @click="environment.splice(index, 1)"
              >移除输入</el-button
            >
          </div>
          <el-button :disabled="submitting" @click="environment.push({ name: '', value: '' })"
            >添加环境覆盖</el-button
          >
          <el-form label-position="top"
            ><el-form-item label="移除已有覆盖"
              ><el-select
                v-model="removeEnvironment"
                :disabled="submitting"
                multiple
                aria-label="移除已有环境覆盖"
                ><el-option
                  v-for="name in environmentNames"
                  :key="name"
                  :label="name"
                  :value="name" /></el-select></el-form-item
          ></el-form>
        </section>
        <el-form label-position="top" :disabled="submitting" class="configuration-form">
          <el-form-item label="允许自动更新"
            ><el-switch v-model="autoUpdate" aria-label="允许自动更新应用"
          /></el-form-item>
          <el-form-item label="版本范围"
            ><el-input v-model="versionPolicy" aria-label="应用版本范围"
          /></el-form-item>
          <el-form-item label="维护窗口"
            ><el-input v-model="maintenanceWindow" aria-label="应用维护窗口"
          /></el-form-item>
          <el-form-item label="已安装依赖（逗号分隔）"
            ><el-input v-model="dependencies" aria-label="应用依赖"
          /></el-form-item>
          <el-form-item label="提交方式" class="full"
            ><el-radio-group v-model="apply" aria-label="配置提交方式"
              ><el-radio-button :value="false">仅保存</el-radio-button
              ><el-radio-button :value="true">保存并重部署</el-radio-button></el-radio-group
            ><small>{{
              apply
                ? '会替换本应用容器，数据保留，等待健康检查完成。'
                : '当前容器保持不变，下次受控重部署才应用新配置。'
            }}</small></el-form-item
          >
        </el-form>
      </template>
    </div>
    <template #footer
      ><el-button :disabled="submitting" @click="close">取消</el-button
      ><el-button
        type="primary"
        :loading="submitting"
        :disabled="!snapshot || loading"
        @click="save"
        >预检并提交</el-button
      ></template
    >
  </el-dialog>
</template>

<style scoped>
.configuration-editor {
  display: grid;
  gap: 16px;
  min-width: 0;
  max-height: calc(90dvh - 150px);
  overflow-y: auto;
  scrollbar-gutter: stable;
  padding-inline-end: 4px;
}
.configuration-editor > *,
.configuration-form > * {
  min-width: 0;
}
.configuration-form {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 18px;
}
.full {
  grid-column: 1 / -1;
}
.configuration-form :deep(.el-form-item__content) {
  gap: 8px;
  align-items: flex-start;
}
.configuration-form :deep(.el-input-number),
.configuration-form :deep(.el-select) {
  width: 100%;
}
.configuration-editor h3 {
  margin: 0 0 8px;
  font-size: 14px;
}
.configuration-editor p,
.configuration-editor small {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.6;
  overflow-wrap: anywhere;
}
.configuration-mount,
.configuration-env-entry {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 2fr) auto;
  gap: 8px;
  margin-bottom: 12px;
  padding: 12px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
}
.configuration-mount {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.configuration-environment > .el-form {
  margin-top: 12px;
}
@media (max-width: 600px) {
  .configuration-form,
  .configuration-env-entry,
  .configuration-mount {
    grid-template-columns: minmax(0, 1fr);
  }
  .configuration-editor :deep(.el-radio-group) {
    display: flex;
    flex-wrap: wrap;
  }
}
</style>
<style>
.configuration-confirm {
  width: min(560px, calc(100vw - 32px));
}
.configuration-confirm .el-message-box__message {
  white-space: pre-line;
  max-height: 60dvh;
  overflow-y: auto;
  overflow-wrap: anywhere;
}
</style>
