<script setup lang="ts">
import { aclEntryLabel, aclBits } from '@/services/containerAcls'
import type { ContainerAclEntry } from '@/services/containerAcls'
defineProps<{ entries: ContainerAclEntry[] | null; label: string }>()
</script>
<template>
  <section class="acl-list" :aria-label="label">
    <h4>{{ label }}</h4>
    <p v-if="entries === null">待预检</p>
    <p v-else-if="!entries.length">空（无默认ACL）</p>
    <dl v-else>
      <template v-for="entry in entries" :key="entry.tag + ':' + entry.id">
        <dt>{{ aclEntryLabel(entry) }}</dt>
        <dd>
          <code>{{ aclBits(entry.permissions) }}</code
          ><span
            v-if="
              ['user', 'group', 'group_obj'].includes(entry.tag) &&
              entries.some((e) => e.tag === 'mask')
            "
          >
            · 有效
            {{
              aclBits(entry.permissions & entries.find((e) => e.tag === 'mask')!.permissions)
            }}</span
          >
        </dd>
      </template>
    </dl>
  </section>
</template>
<style scoped>
.acl-list {
  min-width: 0;
  font-size: 13px;
  overflow-wrap: anywhere;
}
.acl-list h4 {
  margin: 8px 0;
}
.acl-list dl {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 6px 12px;
  margin: 8px 0;
}
.acl-list dd {
  margin: 0;
}
.acl-list span {
  color: var(--el-text-color-secondary);
}
</style>
