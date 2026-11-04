<script setup lang="ts">
import { computed } from 'vue'
import { readRecursiveAclRestoreJobResult } from '@/services/containerRecursiveAclRestoreJobResults'
import {
  recursiveAclRestoreState,
  recursiveAclRestoreResult,
} from '@/services/containerRecursiveAclRestores'
const props = defineProps<{
  jobId: string
  jobType: string
  state: string
  resultJson: string | null
}>()
const result = computed(() =>
  readRecursiveAclRestoreJobResult(props.resultJson, props.jobType, props.state, props.jobId),
)
const fact = (value: boolean | null) => (value === null ? '未证明' : value ? '是' : '否')
const count = (value: number | null) => (value === null ? '未证明' : String(value))
</script>

<template>
  <section aria-label="递归ACL恢复安全结果" class="restore-job-result">
    <template v-if="result">
      <p>{{ recursiveAclRestoreState(result.state) }}</p>
      <dl>
        <dt>原意图</dt>
        <dd>{{ result.intentId }}</dd>
        <dt>阶段</dt>
        <dd>{{ result.stage === 'planning' ? '预检' : '执行' }}</dd>
        <dt>恢复ACL类型</dt>
        <dd>{{ result.aclType ?? '未证明' }}</dd>
      </dl>
      <dl aria-label="本次恢复计划">
        <dt>计划对象</dt>
        <dd>{{ count(result.entryCount) }}</dd>
        <dt>文件</dt>
        <dd>{{ count(result.fileCount) }}</dd>
        <dt>目录（含根）</dt>
        <dd>{{ count(result.directoryCount) }}</dd>
        <dt>计划修改</dt>
        <dd>{{ count(result.changeCount) }}</dd>
      </dl>
      <dl aria-label="本次恢复实际结果">
        <dt>已完成核对</dt>
        <dd>{{ count(result.completedCount) }}</dd>
        <dt>实际修改</dt>
        <dd>{{ count(result.changedCount) }}</dd>
        <dt>已中止</dt>
        <dd>{{ count(result.abortedCount) }}</dd>
        <dt>未开始</dt>
        <dd>{{ count(result.notStartedCount) }}</dd>
        <dt>待证明</dt>
        <dd>{{ count(result.unknownCount) }}</dd>
      </dl>
      <dl aria-label="恢复来源原计数">
        <dt>来源对象</dt>
        <dd>{{ count(result.sourceEntryCount) }}</dd>
        <dt>来源文件</dt>
        <dd>{{ count(result.sourceFileCount) }}</dd>
        <dt>来源目录</dt>
        <dd>{{ count(result.sourceDirectoryCount) }}</dd>
        <dt>来源原修改</dt>
        <dd>{{ count(result.sourceChangedCount) }}</dd>
      </dl>
      <dl>
        <dt>原回执</dt>
        <dd>{{ result.receiptState ?? '未取得' }}</dd>
        <dt>当时对象绑定有效</dt>
        <dd>{{ fact(result.bindingCurrent) }}</dd>
        <dt>可能已有历史</dt>
        <dd>{{ fact(result.historyMayExist) }}</dd>
        <dt>历史已验证</dt>
        <dd>{{ fact(result.historyVerified) }}</dd>
        <dt>当时历史可用</dt>
        <dd>{{ fact(result.historyAvailable) }}</dd>
        <dt>当时发布围栏保留</dt>
        <dd>{{ fact(result.publishBarrier) }}</dd>
        <dt>结果码</dt>
        <dd>{{ result.code }}</dd>
      </dl>
      <p>{{ recursiveAclRestoreResult(result.code) }}</p>
      <p v-if="result.state === 'Acknowledged'">
        管理员处置仅释放业务保护，原计数与不确定性保留，不代表整树恢复成功。
      </p>
      <p>
        这是该任务保存时的历史事实，不证明当前目录树或HDD仍可用，也不代表原用户现在仍有权限。来源原修改数与本次实际修改数分别记录。
      </p>
    </template>
    <p v-else>
      此任务未记录可验证的递归ACL恢复安全摘要；请核对原任务和HDD历史，不将缺失结果视为零修改或成功。
    </p>
  </section>
</template>

<style scoped>
.restore-job-result {
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}
p {
  margin: 0 0 12px;
}
dl {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 2fr);
  gap: 8px 12px;
  margin: 0 0 12px;
}
dt {
  color: var(--el-text-color-secondary);
}
dd {
  min-width: 0;
  margin: 0;
}
@media (max-width: 600px) {
  dl {
    grid-template-columns: minmax(0, 1fr);
    gap: 4px;
  }
  dd {
    margin-bottom: 8px;
  }
}
</style>
