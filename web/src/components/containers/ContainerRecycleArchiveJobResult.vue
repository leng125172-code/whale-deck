<script setup lang="ts">
import { computed } from 'vue'
import { readRecycleArchiveJobResult } from '@/services/containerRecycleArchiveJobResults'
const props = defineProps<{
  jobId: string
  jobType: string
  state: string
  resultJson: string | null
}>()
const result = computed(() =>
  readRecycleArchiveJobResult(props.resultJson, props.jobId, props.jobType, props.state),
)
const titles: Record<string, string> = {
  Ready: '预检完成，尚未从HDD复制',
  Expired: '归档预检已过期',
  Reserving: '正在预留归档保护，尚未开始复制',
  Applying: '已进入归档复制，结果仍待核实',
  Succeeded: '归档复制已完成',
  Canceled: '已取消归档恢复',
  Failed: '归档任务未完成',
  Unknown: '归档结果未知，仍需核实原任务',
  Acknowledged: '管理员已明确处置未知归档结果',
}
const fact = (v: boolean | null) => (v === null ? '未证明' : v ? '是' : '否')
</script>
<template>
  <section aria-label="归档复制安全结果" class="recycle-archive-job-result">
    <template v-if="result"
      ><p>
        {{
          result.stage === 'planning' && result.state === 'Failed'
            ? '预检失败，未进入复制执行'
            : result.state === 'Reserving' && result.reservationRecorded
              ? '归档保护已预留，尚未开始复制'
              : titles[result.state]
        }}
      </p>
      <dl>
        <dt>本归档意图</dt>
        <dd>{{ result.intentId }}</dd>
        <dt>来源清理意图</dt>
        <dd>{{ result.sourceIntentId }}</dd>
        <dt>来源清理执行</dt>
        <dd>{{ result.purgeOperationId }}</dd>
        <dt>原回收意图</dt>
        <dd>{{ result.originalIntentId }}</dd>
        <dt>原回收执行</dt>
        <dd>{{ result.sourceOperationId }}</dd>
        <dt>阶段</dt>
        <dd>{{ result.stage === 'planning' ? '预检' : '执行' }}</dd>
        <dt>历史回执</dt>
        <dd>{{ result.receiptState ?? '未取得' }}</dd>
        <dt>预留入口曾记录</dt>
        <dd>{{ fact(result.reservationStarted) }}</dd>
        <dt>预留证明曾记录</dt>
        <dd>{{ fact(result.reservationRecorded) }}</dd>
        <dt>复制入口曾记录</dt>
        <dd>{{ fact(result.applyStarted) }}</dd>
        <dt>回执曾证明本操作完成复制</dt>
        <dd>{{ fact(result.archiveCopied) }}</dd>
        <dt>回执时来源保护存在</dt>
        <dd>{{ fact(result.sourceFenced) }}</dd>
        <dt>回执时本操作持有保护</dt>
        <dd>{{ fact(result.ownedFence) }}</dd>
        <dt>回执时副本绑定有效</dt>
        <dd>{{ fact(result.bindingCurrent) }}</dd>
        <dt>保存时保留写入保护</dt>
        <dd>{{ fact(result.publishBarrier) }}</dd>
        <dt>结果码</dt>
        <dd>{{ result.code }}</dd>
      </dl>
      <p v-if="result.state === 'Unknown' || result.receiptState === 'Unknown'">
        未知事实不能证明已恢复；解除保护不证明复制成功，也不会重复复制。
      </p>
      <p>
        这是保存时的历史事实，预留或复制入口不等于复制成功。当前文件可能已变化或消失，历史复制结果仍保留；原回收/清理任务和HDD备份不改写。
      </p>
    </template>
    <p v-else>此任务没有可核实的归档复制安全摘要，请查询原归档任务。</p>
  </section>
</template>
<style scoped>
.recycle-archive-job-result {
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
