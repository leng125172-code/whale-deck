<script setup lang="ts">
import type { ContainerRecursivePermissionIntent } from '@/services/containerRecursivePermissions'
defineProps<{ row: ContainerRecursivePermissionIntent }>()
</script>
<template>
  <section class="recursive-facts" aria-label="递归权限计划和实际结果">
    <p>文件模式 {{ row.command.fileMode }} · 目录模式 {{ row.command.directoryMode }}</p>
    <dl v-if="row.entryCount !== null" class="recursive-facts__counts">
      <div>
        <dt>预检对象</dt>
        <dd>{{ row.entryCount }}</dd>
      </div>
      <div>
        <dt>预计变更</dt>
        <dd>{{ row.changeCount }}</dd>
      </div>
      <div>
        <dt>文件</dt>
        <dd>{{ row.fileCount }}</dd>
      </div>
      <div>
        <dt>目录（含根）</dt>
        <dd>{{ row.directoryCount }}</dd>
      </div>
    </dl>
    <p v-if="row.entryCount !== null">
      根目录UID/GID {{ row.rootUid }}:{{ row.rootGid }}；执行身份 {{ row.executorUid }}:{{
        row.executorGid
      }}。树内各项保留原所有者。
    </p>
    <p v-if="row.manifestDigest" class="recursive-facts__digest">
      原清单摘要：<code>{{ row.manifestDigest }}</code>
    </p>
    <template v-if="row.receipt">
      <dl class="recursive-facts__counts" aria-label="实际逐项结果">
        <div>
          <dt>已完成核对</dt>
          <dd>{{ row.receipt.completedCount }}</dd>
        </div>
        <div>
          <dt>实际修改</dt>
          <dd>{{ row.receipt.changedCount }}</dd>
        </div>
        <div>
          <dt>已中止</dt>
          <dd>{{ row.receipt.abortedCount }}</dd>
        </div>
        <div>
          <dt>未开始</dt>
          <dd>{{ row.receipt.notStartedCount }}</dd>
        </div>
        <div>
          <dt>待证明</dt>
          <dd>{{ row.receipt.unknownCount }}</dd>
        </div>
      </dl>
      <p>实际修改包含在已完成核对中，不重复累计。</p>
      <p>
        最近回执记录：HDD元数据{{
          row.receipt.historyAvailable ? '可用' : '不可用或未证明可用'
        }}；{{
          row.receipt.bindingCurrent ? '目录树绑定有效' : '未证明当前目录树绑定有效'
        }}。这些是最近回执的观测，不是实时检查。
      </p>
    </template>
    <p v-if="row.executionJobId">
      历史证明：{{
        row.historyVerified
          ? '已验证留存'
          : row.historyMayExist
            ? '可能存在，尚未验证'
            : '未记录写入'
      }}。元数据不包含文件内容；历史验证不代表当前HDD可用。
    </p>
    <el-alert
      v-if="row.state === 'Partial'"
      title="部分对象已经修改。实际计数和历史保留，不自动续做或回滚；解除保护须管理员核实并明确处置。"
      type="warning"
      :closable="false"
    />
    <el-alert
      v-if="row.state === 'Unknown'"
      title="部分结果尚未证明。保护已保留，不得推断零修改或成功；请查询原任务并明确处置。"
      type="warning"
      :closable="false"
    />
  </section>
</template>
<style scoped>
.recursive-facts {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
}
.recursive-facts p {
  margin-block: 8px;
}
.recursive-facts__counts {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin-block: 12px;
}
.recursive-facts__counts > div {
  min-width: 0;
  padding: 8px 10px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
}
.recursive-facts dt {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.recursive-facts dd {
  margin: 4px 0 0;
  font-size: 20px;
  font-variant-numeric: tabular-nums;
}
.recursive-facts__digest {
  font-size: 12px;
}
</style>
