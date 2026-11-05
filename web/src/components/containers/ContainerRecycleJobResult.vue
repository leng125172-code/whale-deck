<script setup lang="ts">
import { computed } from 'vue'
import { readRecycleJobResult } from '@/services/containerRecycleJobResults'
const props = defineProps<{
  jobId: string
  jobType: string
  state: string
  resultJson: string | null
}>()
const result = computed(() =>
  readRecycleJobResult(props.resultJson, props.jobId, props.jobType, props.state),
)
const titles: Record<string, string> = {
  Ready: '预检完成，尚未移除文件',
  Expired: '预检已过期',
  Succeeded: '文件回收已完成',
  Superseded: '已有历史结果，当前原件状态已变化',
  Canceled: '已取消',
  Failed: '任务未完成',
  Unknown: '结果未知，仍需核实原任务',
  Acknowledged: '管理员已明确处置未知结果',
}
const fact = (v: boolean | null) => (v === null ? '未证明' : v ? '是' : '否')
</script>
<template>
  <section aria-label="回收安全结果" class="recycle-job-result">
    <template v-if="result"
      ><p>
        {{
          result.stage === 'planning' && result.state === 'Failed'
            ? '预检失败，未进入回收执行'
            : titles[result.state]
        }}
      </p>
      <dl>
        <dt>原意图</dt>
        <dd>{{ result.intentId }}</dd>
        <dt>阶段</dt>
        <dd>{{ result.stage === 'planning' ? '预检' : '执行' }}</dd>
        <dt>历史回执</dt>
        <dd>{{ result.receiptState ?? '未取得' }}</dd>
        <dt>回执曾证明原件留存</dt>
        <dd>{{ fact(result.sourceRetained) }}</dd>
        <dt>回执曾证明原件恢复</dt>
        <dd>{{ fact(result.sourceRestored) }}</dd>
        <dt>回执时对象绑定有效</dt>
        <dd>{{ fact(result.bindingCurrent) }}</dd>
        <dt>可能存在历史</dt>
        <dd>{{ fact(result.historyMayExist) }}</dd>
        <dt>历史曾验证</dt>
        <dd>{{ fact(result.historyVerified) }}</dd>
        <dt>回执时历史可用</dt>
        <dd>{{ fact(result.historyAvailable) }}</dd>
        <dt>保存时保留写入保护</dt>
        <dd>{{ fact(result.publishBarrier) }}</dd>
        <dt>结果码</dt>
        <dd>{{ result.code }}</dd>
      </dl>
      <p v-if="result.state === 'Unknown' || result.receiptState === 'Unknown'">
        未知事实不代表没有发生文件移除；解除保护不证明回收或恢复成功。
      </p>
      <p>这是该任务保存时的历史事实，不代表当前文件或原用户权限；后续恢复不会改写原回收任务。</p>
    </template>
    <p v-else>此任务没有可核实的回收安全摘要，请查询原文件任务。</p>
  </section>
</template>
<style scoped>
.recycle-job-result {
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
  margin: 0;
  min-width: 0;
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
