<script setup lang="ts">
import { computed } from 'vue'
import ContainerRecycleArchiveRetirementPanel from './ContainerRecycleArchiveRetirementPanel.vue'
import { readRetirementJob, retirementState } from '@/services/containerRecycleArchiveRetirements'

const props = defineProps<{
  jobId: string
  jobType: string
  state: string
  resultJson: string | null
}>()
const emit = defineEmits<{ changed: [] }>()
const result = computed(() =>
  readRetirementJob(props.resultJson, props.jobId, props.jobType, props.state),
)
const fact = (value: boolean | null) => (value === null ? '未证明' : value ? '是' : '否')
</script>

<template>
  <section aria-label="归档退役安全结果" class="retirement-result">
    <template v-if="result">
      <p>{{ retirementState(result.state) }}</p>
      <dl>
        <dt>退役意图</dt>
        <dd>{{ result.intentId }}</dd>
        <dt>归档意图</dt>
        <dd>{{ result.archiveIntentId }}</dd>
        <dt>保留天数</dt>
        <dd>{{ result.retainDays }} 天</dd>
        <dt>已记录预留入口</dt>
        <dd>{{ fact(result.reservationStarted) }}</dd>
        <dt>已记录预留证明</dt>
        <dd>{{ fact(result.reservationRecorded) }}</dd>
        <dt>已记录执行入口</dt>
        <dd>{{ fact(result.applyStarted) }}</dd>
        <dt>历史回执</dt>
        <dd>{{ result.receiptState ?? '未取得' }}</dd>
        <dt>回执已证明删除</dt>
        <dd>{{ fact(result.retirementApplied) }}</dd>
        <dt>共享保护</dt>
        <dd>{{ fact(result.publishBarrier) }}</dd>
      </dl>
      <p v-if="result.state === 'Unknown' || result.receiptState === 'Unknown'">
        已记录的执行入口和管理员处置均不能单独证明数据删除。
      </p>
      <ContainerRecycleArchiveRetirementPanel
        :container-id="result.containerId"
        :existing-id="result.intentId"
        :recovery-id="
          result.state === 'Unknown' && result.publishBarrier ? result.intentId : undefined
        "
        @changed="emit('changed')"
      />
    </template>
    <p v-else>此任务没有可核实的归档退役安全摘要。</p>
  </section>
</template>

<style scoped>
.retirement-result {
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}
dl {
  display: grid;
  grid-template-columns: max-content minmax(0, 1fr);
  gap: 0.3rem 0.8rem;
}
dd {
  margin: 0;
}
</style>
