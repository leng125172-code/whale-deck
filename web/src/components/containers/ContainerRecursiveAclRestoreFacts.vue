<script setup lang="ts">
import { aclModeLabel } from '@/services/containerAcls'
import type { ContainerRecursiveAclRestoreIntent } from '@/services/containerRecursiveAclRestores'
defineProps<{ row: ContainerRecursiveAclRestoreIntent }>()
</script>
<template>
  <section class="recursive-facts" aria-label="递归ACL恢复计划和实际结果">
    <p v-if="row.restoreSource">
      类型：{{ row.restoreSource?.changedAclType ?? '待预检' }}；{{
        row.restoreSource?.changedAclType === 'access'
          ? '逐项恢复来源访问ACL'
          : '仅目录继承，文件不变'
      }}
    </p>
    <p v-else>恢复类型及逐项目标待预检核实。</p>
    <dl v-if="row.entryCount !== null" class="recursive-facts__counts">
      <div>
        <dt>预检对象</dt>
        <dd>{{ row.entryCount }}</dd>
      </div>
      <div>
        <dt>本次预计恢复</dt>
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
      原根目录UID/GID {{ row.rootUid }}:{{ row.rootGid }}；执行身份 {{ row.executorUid }}:{{
        row.executorGid
      }}。原根模式
      {{
        aclModeLabel(row.rootMode === null ? null : row.rootMode & 511)
      }}。ACL数字ID不改变inode所有者；access模式随mask变化，default不改变访问模式。
    </p>
    <template v-if="row.restoreSource">
      <p>
        恢复来源：<code>{{ row.restoreSource.captureId }}</code
        >；捕获时间 {{ new Date(row.restoreSource.createdAtUnixMs).toLocaleString() }}
      </p>
      <p>
        来源实例：{{ row.restoreSource.sourceInstanceId }}；来源原预计变更
        {{ row.restoreSource.changeCount }}，不等于本次预计恢复 {{ row.changeCount }}。
      </p>
      <p>
        来源历史摘要：<code>{{ row.restoreSource.historyDigest }}</code>
      </p>
      <p>
        来源清单摘要：<code>{{ row.restoreSource.manifestDigest }}</code>
      </p>
      <p>
        逐项恢复来源的{{ row.restoreSource.changedAclType }}
        ACL，按相对路径和对象类型匹配异构条目；不恢复历史inode或所有者，另一类当前ACL保持。完整目标可在逐项恢复前像中分页核对。
      </p>
    </template>
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
      title="部分对象已经恢复。实际计数和历史保留，不自动续做或回滚；解除保护须管理员核实并明确处置。"
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
