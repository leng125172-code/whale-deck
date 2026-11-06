<script setup lang="ts">
import { computed } from 'vue'
import { readRecyclePurgeJobResult } from '@/services/containerRecyclePurgeJobResults'
const props = defineProps<{
  jobId: string
  jobType: string
  state: string
  resultJson: string | null
}>()
const result = computed(() =>
  readRecyclePurgeJobResult(props.resultJson, props.jobId, props.jobType, props.state),
)
const titles: Record<string, string> = {
  Ready: '预检完成，尚未删除留存原件',
  Expired: '清理预检已过期',
  Reserving: '正在预留原件保护，尚未进入删除',
  Applying: '已进入原件清理，结果仍待核实',
  Succeeded: '留存原件清理已完成',
  Canceled: '已取消清理',
  Failed: '清理任务未完成',
  Unknown: '清理结果未知，仍需核实原任务',
  Acknowledged: '管理员已明确处置未知清理结果',
}
const fact = (v: boolean | null) => (v === null ? '未证明' : v ? '是' : '否')
</script>
<template>
  <section aria-label="留存清理安全结果" class="recycle-purge-job-result">
    <template v-if="result"
      ><p>
        {{
          result.stage === 'planning' && result.state === 'Failed'
            ? '预检失败，未进入清理执行'
            : titles[result.state]
        }}
      </p>
      <dl>
        <dt>本清理意图</dt>
        <dd>{{ result.intentId }}</dd>
        <dt>原回收意图</dt>
        <dd>{{ result.sourceIntentId }}</dd>
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
        <dt>删除入口曾记录</dt>
        <dd>{{ fact(result.applyStarted) }}</dd>
        <dt>回执曾证明本操作完成清理</dt>
        <dd>{{ fact(result.purgeApplied) }}</dd>
        <dt>回执时来源保护存在</dt>
        <dd>{{ fact(result.sourceFenced) }}</dd>
        <dt>回执时本操作持有保护</dt>
        <dd>{{ fact(result.ownedFence) }}</dd>
        <dt>保存时保留写入保护</dt>
        <dd>{{ fact(result.publishBarrier) }}</dd>
        <dt>结果码</dt>
        <dd>{{ result.code }}</dd>
      </dl>
      <p v-if="result.state === 'Unknown' || result.receiptState === 'Unknown'">
        未知事实不代表原件仍在；解除保护不证明清理成功，也不会重新删除。
      </p>
      <p>
        这是本清理任务保存时的历史事实；预留证明不等于已经删除，删除入口记录也不等于清理成功。不改写原回收任务；HDD备份保留，当前尚未提供清理后的归档恢复入口。
      </p>
    </template>
    <p v-else>此任务没有可核实的留存清理安全摘要，请查询原清理任务。</p>
  </section>
</template>
<style scoped>
.recycle-purge-job-result {
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
