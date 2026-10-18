<script setup lang="ts">
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { enqueueOperation, planOperation } from '@/services/operations'
import PlatformBuildCard from '@/components/PlatformBuildCard.vue'
const running = ref('')
async function simple(action: string) {
  running.value = action
  try {
    const job = await enqueueOperation('platform', action, null, {}, false)
    ElMessage.success(`任务已提交：${job.id}`)
  } finally {
    running.value = ''
  }
}
async function critical(action: 'apply-update' | 'rollback') {
  running.value = action
  try {
    const plan = await planOperation('platform', action, null, {})
    await ElMessageBox.confirm(
      [
        ...plan.changes,
        ...plan.warnings,
        action === 'rollback'
          ? '将切换到最近一次可回滚版本。'
          : '将按已批准的更新计划维护 Whale Deck 核心组件。',
      ].join('\n'),
      action === 'rollback' ? '平台回滚确认' : '平台更新确认',
      { type: 'warning' },
    )
    const job = await enqueueOperation('platform', action, null, {}, true, plan.planHash)
    ElMessage.success(`维护任务已提交：${job.id}`)
  } finally {
    running.value = ''
  }
}
</script>
<template>
  <div class="maintenance-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">PLATFORM</span>
        <h1>平台维护</h1>
        <p>诊断、兼容性预检、核心更新和回滚使用宿主机稳定维护通道。</p>
      </div>
    </header>
    <section class="maintenance-grid">
      <PlatformBuildCard />
      <article class="panel maintenance-card">
        <h2>平台自检</h2>
        <p>检查 Agent、database-platform、容器健康与基础配置。</p>
        <el-button type="primary" :loading="running === 'diagnose'" @click="simple('diagnose')"
          >运行自检</el-button
        >
      </article>
      <article class="panel maintenance-card">
        <h2>诊断包</h2>
        <p>生成受限、脱敏的诊断信息；不会收集 Secret 和完整环境变量。</p>
        <el-button :loading="running === 'diagnostic-bundle'" @click="simple('diagnostic-bundle')"
          >生成诊断包</el-button
        >
      </article>
      <article class="panel maintenance-card">
        <h2>更新计划</h2>
        <p>检查两个批准仓库及组件兼容状态，不在预检阶段修改系统。</p>
        <el-button :loading="running === 'plan-update'" @click="simple('plan-update')"
          >生成更新计划</el-button
        >
      </article>
      <article class="panel maintenance-card maintenance-card--danger">
        <h2>核心维护</h2>
        <p>执行更新或回滚会造成短暂服务中断，维护入口仍由 systemd 托管。</p>
        <div>
          <el-button
            type="primary"
            :loading="running === 'apply-update'"
            @click="critical('apply-update')"
            >执行更新</el-button
          ><el-button
            type="danger"
            plain
            :loading="running === 'rollback'"
            @click="critical('rollback')"
            >回滚</el-button
          >
        </div>
      </article>
    </section>
  </div>
</template>
<style scoped>
.maintenance-page {
  display: grid;
  gap: 18px;
}
.maintenance-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}
.maintenance-card {
  padding: 22px;
}
.maintenance-card h2 {
  margin: 0;
  font-size: 16px;
}
.maintenance-card p {
  min-height: 48px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.7;
}
.maintenance-card--danger {
  border-color: var(--el-color-warning-light-5);
}
@media (max-width: 720px) {
  .maintenance-grid {
    grid-template-columns: 1fr;
  }
}
</style>
