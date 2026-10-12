<script setup lang="ts">
import type { ContainerInspect } from '@/services/contracts'
defineProps<{ inspect: ContainerInspect }>()
</script>

<template>
  <section class="container-relations" aria-label="关联资源">
    <h3>关联资源</h3>
    <el-alert
      v-if="!inspect.relationships"
      type="info"
      :closable="false"
      title="当前 Agent 未提供结构化关联信息，请更新 Agent；以下仅显示旧版摘要。"
    />
    <template v-else>
      <el-alert
        v-if="inspect.relationships.isTruncated"
        type="warning"
        :closable="false"
        title="关联信息超出上限，部分内容已截断。"
      />
      <div class="relation-group">
        <h4>镜像</h4>
        <router-link
          v-if="inspect.relationships.imageId"
          :to="{
            path: '/docker/images',
            query: { resourceId: 'image:' + inspect.relationships.imageId },
          }"
        >
          {{ inspect.image }}
        </router-link>
        <span v-else>{{ inspect.image }}</span>
        <code>{{ inspect.relationships.imageId || '镜像标识不可用' }}</code>
      </div>
      <div v-if="inspect.relationships.composeProject" class="relation-group">
        <h4>Compose 项目</h4>
        <router-link
          :to="{
            path: '/compose-projects',
            query: { project: inspect.relationships.composeProject },
          }"
        >
          {{ inspect.relationships.composeProject }}
        </router-link>
        <small
          >服务：{{ inspect.relationships.composeService || '未提供' }} ·
          管理权限由项目注册表决定</small
        >
      </div>
      <div class="relation-group">
        <h4>
          网络 <small>模式：{{ inspect.relationships.networkMode || '未提供' }}</small>
        </h4>
        <p v-if="!inspect.relationships.endpoints.length">没有独立网络端点</p>
        <article
          v-for="endpoint in inspect.relationships.endpoints"
          :key="endpoint.name"
          class="relation-item"
        >
          <router-link
            v-if="endpoint.networkId"
            :to="{
              path: '/docker/networks',
              query: { resourceId: 'network:' + endpoint.networkId },
            }"
            >{{ endpoint.name }}</router-link
          >
          <span v-else>{{ endpoint.name }} · 网络标识不可用</span>
          <small
            >IPv4：{{
              endpoint.ipv4Address
                ? endpoint.ipv4Address + '/' + endpoint.ipv4PrefixLength
                : '未分配'
            }}
            · 网关：{{ endpoint.ipv4Gateway || '—' }}</small
          >
          <small v-if="endpoint.ipv6Address"
            >IPv6：{{ endpoint.ipv6Address }}/{{ endpoint.ipv6PrefixLength }} · 网关：{{
              endpoint.ipv6Gateway || '—'
            }}</small
          >
          <small>MAC：{{ endpoint.macAddress || '—' }}</small>
        </article>
      </div>
      <div class="relation-group">
        <h4>挂载</h4>
        <p v-if="!inspect.relationships.mounts.length">没有挂载</p>
        <article
          v-for="(mount, index) in inspect.relationships.mounts"
          :key="index"
          class="relation-item"
        >
          <div>
            <el-tag size="small" effect="plain">{{ mount.type }}</el-tag>
            <el-tag size="small" :type="mount.readWrite ? 'warning' : 'info'" effect="plain">{{
              mount.readWrite ? '读写' : '只读'
            }}</el-tag>
            <router-link
              v-if="mount.type === 'volume' && mount.name"
              :to="{ path: '/docker/volumes', query: { resourceId: 'volume:' + mount.name } }"
              >{{ mount.name }}</router-link
            >
          </div>
          <code
            >来源：{{ mount.source || (mount.type === 'tmpfs' ? '内存文件系统' : '未提供') }}</code
          >
          <code>容器路径：{{ mount.destination }}</code>
        </article>
      </div>
      <div class="relation-group">
        <h4>端口</h4>
        <p v-if="!inspect.relationships.portBindings.length">没有声明或发布端口</p>
        <article
          v-for="(port, index) in inspect.relationships.portBindings"
          :key="index"
          class="relation-item"
        >
          <code
            >{{
              port.published
                ? (port.hostAddress.includes(':')
                    ? '[' + port.hostAddress + ']'
                    : port.hostAddress || '所有地址') +
                  ':' +
                  (port.hostPort || '自动分配') +
                  ' → '
                : ''
            }}{{ port.containerPort }}</code
          >
          <small>{{
            port.published
              ? '宿主机映射配置；不等于正在监听，实际访问仍受容器状态与网络策略限制'
              : '仅容器声明，未发布到宿主机'
          }}</small>
        </article>
      </div>
    </template>
  </section>
</template>

<style scoped>
.container-relations,
.relation-group,
.relation-item {
  display: grid;
  gap: 8px;
  min-width: 0;
}
.container-relations {
  margin-top: 20px;
  gap: 14px;
}
h3 {
  font-size: 16px;
  font-weight: 400;
  margin: 0;
}
h4 {
  font-size: 14px;
  margin: 0;
}
.relation-group {
  padding: 12px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
}
.relation-item {
  border-top: 1px solid var(--el-border-color-lighter);
  padding-top: 8px;
}
.relation-item > div {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  align-items: center;
}
a,
code,
small,
p {
  overflow-wrap: anywhere;
  word-break: break-word;
  white-space: normal;
  min-width: 0;
}
code {
  font-size: 12px;
}
small,
p {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin: 0;
}
a {
  color: var(--el-color-primary);
}
</style>
