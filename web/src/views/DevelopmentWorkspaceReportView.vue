<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElCard } from 'element-plus'
import { ApiError } from '@/services/apiClient'
import { authSession } from '@/services/authSession'
import {
  readRunReport,
  workspaceError,
  workspacePath,
  type RunReport,
} from '@/services/developmentWorkspaces'

const route = useRoute()
const id = computed(() => String(route.params.workspaceId ?? ''))
const runId = computed(() => String(route.params.runId ?? ''))
const report = ref<RunReport | null>(null)
const digest = ref('')
const error = ref('')
const loading = ref(false)
const downloading = ref(false)
let lifetime: AbortController

watch(
  () => [id.value, runId.value, authSession.user.value?.subject],
  (_, __, onCleanup) => {
    const controller = new AbortController()
    lifetime = controller
    onCleanup(() => controller.abort())
    loading.value = false
    downloading.value = false
    void load()
  },
  { immediate: true },
)

function failure(reason: unknown) {
  if (reason instanceof ApiError) {
    if ([401, 403, 404].includes(reason.status)) {
      report.value = null
      digest.value = ''
    }
    error.value = workspaceError(reason)
  } else if (
    reason instanceof Error &&
    ['报告完整性核验失败。', '报告超过下载上限。'].includes(reason.message)
  ) {
    error.value = reason.message
  } else error.value = '无法读取运行报告，请刷新后重试。'
}

async function load() {
  if (loading.value) return
  const signal = lifetime.signal
  const workspaceId = id.value
  const target = runId.value
  loading.value = true
  report.value = null
  digest.value = ''
  error.value = ''
  try {
    const result = await readRunReport(workspaceId, target, 'json', signal)
    if (signal.aborted) return
    const value = JSON.parse(
      new TextDecoder('utf-8', { fatal: true }).decode(result.bytes),
    ) as RunReport
    if (
      value.workspaceId !== workspaceId.toLowerCase() ||
      value.runId !== target.toLowerCase() ||
      !value.source ||
      !value.environment ||
      !Array.isArray(value.steps)
    )
      throw new Error('报告完整性核验失败。')
    report.value = value
    digest.value = result.digest
  } catch (reason) {
    if (!signal.aborted) failure(reason)
  } finally {
    if (!signal.aborted) loading.value = false
  }
}

async function download(format: 'json' | 'markdown') {
  if (downloading.value || !report.value) return
  const signal = lifetime.signal
  const target = runId.value
  downloading.value = true
  error.value = ''
  try {
    const result = await readRunReport(id.value, target, format, signal)
    if (signal.aborted) return
    const url = URL.createObjectURL(
      new Blob([result.bytes], {
        type: format === 'json' ? 'application/json' : 'text/markdown;charset=utf-8',
      }),
    )
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `workspace-run-${target}.${format === 'json' ? 'json' : 'md'}`
    anchor.click()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
  } catch (reason) {
    if (!signal.aborted) failure(reason)
  } finally {
    if (!signal.aborted) downloading.value = false
  }
}

const states: Record<string, string> = {
  Succeeded: '成功',
  Failed: '失败',
  Canceled: '已取消',
  Unknown: '未知',
  Interrupted: '中断',
  TimedOut: '超时',
  Skipped: '跳过',
  Matched: '摘要匹配',
  Mismatch: '摘要不匹配',
  Missing: '缺失',
  Unverified: '无法核验',
  Present: '已采集',
  Rejected: '采集被拒绝',
  TooLarge: '超限',
  Unavailable: '不可用',
  IntegrityFailure: '完整性核验失败',
  Parsed: '已解析',
  Invalid: '格式无效',
}
const label = (value: string) => states[value] ?? value
</script>

<template>
  <section class="workspace-report" aria-labelledby="workspace-report-title">
    <header>
      <RouterLink :to="workspacePath">开发空间</RouterLink>
      <h1 id="workspace-report-title">运行报告</h1>
      <p>
        <code>{{ runId }}</code>
      </p>
    </header>
    <el-alert v-if="error" :title="error" :closable="false" type="error" show-icon />
    <el-button v-if="!report" :loading="loading" @click="load">重新读取</el-button>

    <template v-if="report">
      <el-card shadow="never">
        <template #header><strong>运行结论</strong></template>
        <dl>
          <dt>状态</dt>
          <dd>{{ label(report.outcome) }}</dd>
          <dt>退出码</dt>
          <dd>{{ report.exitCode ?? '未知' }}</dd>
          <dt>结算时间</dt>
          <dd>{{ new Date(report.finalizedAtUtc).toLocaleString() }}</dd>
          <dt>保留至</dt>
          <dd>{{ new Date(report.retainUntilUtc).toLocaleString() }}</dd>
          <dt>源码指纹</dt>
          <dd>
            <code>{{ report.source.fingerprint ?? '未知' }}</code>
          </dd>
          <dt>Git 提交</dt>
          <dd>
            <code>{{ report.source.gitCommit ?? '未知' }}</code>
          </dd>
          <dt>环境摘要</dt>
          <dd>
            <code>{{ report.environment.digest }}</code>
          </dd>
          <dt>报告 SHA-256</dt>
          <dd>
            <code>{{ digest }}</code>
          </dd>
        </dl>
        <div class="report-actions">
          <el-button :disabled="downloading" @click="download('json')">下载 JSON</el-button>
          <el-button :disabled="downloading" @click="download('markdown')">下载 Markdown</el-button>
        </div>
      </el-card>

      <el-card v-for="step in report.steps" :key="step.stepId" shadow="never">
        <template #header
          ><strong>{{ step.key }} · {{ label(step.outcome) }}</strong></template
        >
        <p v-if="!step.tests.length">此步骤没有测试工件。</p>
        <div v-for="test in step.tests" :key="test.artifactId" class="report-test">
          <el-tag effect="plain">{{ test.format }}</el-tag>
          <span>{{ label(test.integrity) }}</span>
          <span v-if="test.collection">{{ label(test.collection) }}</span>
          <span v-if="test.result">{{ label(test.result.state) }}</span>
          <p v-if="test.result?.counts">
            通过 {{ test.result.counts.passed }} · 失败 {{ test.result.counts.failed }} · 错误
            {{ test.result.counts.errors }} · 跳过 {{ test.result.counts.skipped }}
          </p>
        </div>
      </el-card>
    </template>
  </section>
</template>

<style scoped>
.workspace-report {
  display: grid;
  gap: 16px;
  overflow-wrap: anywhere;
}
.workspace-report h1 {
  margin: 10px 0 0;
}
.workspace-report header p,
.workspace-report dt {
  color: var(--el-text-color-secondary);
}
.workspace-report dl {
  display: grid;
  grid-template-columns: minmax(88px, max-content) minmax(0, 1fr);
  gap: 8px 16px;
}
.workspace-report dd {
  margin: 0;
}
.report-actions,
.report-test {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}
.report-actions > .el-button {
  margin-left: 0;
}
.report-test p {
  flex-basis: 100%;
  margin: 4px 0 10px;
}
</style>
