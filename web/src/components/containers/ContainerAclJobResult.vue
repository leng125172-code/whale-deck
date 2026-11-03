<script setup lang="ts">
import { computed } from 'vue'
import { readAclJobResult } from '@/services/containerAclJobResults'
const props = defineProps<{ jobType: string; state: string; resultJson: string | null }>()
const result = computed(() => readAclJobResult(props.resultJson, props.jobType, props.state))
const states: Record<string, string> = {
  Ready: '预检完成，尚未修改 ACL',
  Expired: '预检已过期',
  Succeeded: 'ACL 修改已完成',
  Superseded: '已有完成回执，实例绑定已变化',
  Canceled: '已取消',
  Failed: '执行失败',
  Unknown: '结果未知，仍保留写入保护',
  Acknowledged: '管理员已明确处置未知结果',
}
const title = computed(() =>
  result.value?.state === 'Failed' && result.value.stage === 'planning'
    ? '预检失败，未进入 ACL 修改执行'
    : states[result.value?.state ?? ''],
)
function fact(value: boolean | null) {
  return value === null ? '未证明' : value ? '是' : '否'
}
</script>

<template>
  <section aria-label="ACL 安全结果" class="acl-job-result">
    <template v-if="result">
      <p>{{ title }}</p>
      <dl>
        <dt>原意图</dt>
        <dd>{{ result.intentId }}</dd>
        <dt>阶段</dt>
        <dd>{{ result.stage === 'planning' ? '预检' : '执行' }}</dd>
        <dt>ACL 类型</dt>
        <dd>{{ result.aclType === 'access' ? 'access 访问权限' : 'default 默认继承' }}</dd>
        <dt>目标对象</dt>
        <dd>
          {{ result.kind === 'File' ? '文件' : result.kind === 'Directory' ? '目录' : '未证明' }}
        </dd>
        <dt>目标普通模式</dt>
        <dd>
          {{
            result.targetMode === null ? '未证明' : result.targetMode.toString(8).padStart(4, '0')
          }}
        </dd>
        <dt>预检无需变更</dt>
        <dd>{{ fact(result.noChange) }}</dd>
        <dt>ACL 已确认</dt>
        <dd>{{ fact(result.aclConfirmed) }}</dd>
        <dt>历史回执</dt>
        <dd>{{ result.receiptState ?? '未取得' }}</dd>
        <dt>回执对象绑定有效</dt>
        <dd>{{ fact(result.bindingCurrent) }}</dd>
        <dt>可能已有历史</dt>
        <dd>{{ fact(result.historyMayExist) }}</dd>
        <dt>历史已验证</dt>
        <dd>{{ fact(result.historyVerified) }}</dd>
        <dt>已记录发布版本</dt>
        <dd>{{ fact(result.hasPublishedVersion) }}</dd>
        <dt>结果码</dt>
        <dd>{{ result.code }}</dd>
      </dl>
      <p v-if="result.state === 'Acknowledged'">
        处置仅解除业务保护，不证明 ACL 已恢复或重新执行。
      </p>
      <p>这是该任务保存时的历史事实；对象绑定有效不代表原用户仍有管理权限。</p>
    </template>
    <p v-else>此任务未记录可展示的 ACL 安全摘要，请结合任务状态与原文件任务核对。</p>
  </section>
</template>

<style scoped>
.acl-job-result {
  overflow-wrap: anywhere;
  min-width: 0;
  max-width: 100%;
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
