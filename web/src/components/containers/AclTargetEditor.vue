<script setup lang="ts">
import { ref } from 'vue'
import { aclEntryLabel, parseAclId } from '@/services/containerAcls'
import type { ContainerAclEntry } from '@/services/containerAcls'
const props = defineProps<{ modelValue: ContainerAclEntry[]; label: string; disabled: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [entries: ContainerAclEntry[]] }>()
const addTag = ref<'user' | 'group' | 'mask'>('user')
function replace(index: number, patch: Partial<ContainerAclEntry>) {
  if (props.disabled) return
  emit(
    'update:modelValue',
    props.modelValue.map((e, n) => ({ ...e, ...(n === index ? patch : {}) })),
  )
}
function add() {
  if (props.disabled || props.modelValue.length >= 64) return
  const entries = [
    ...props.modelValue.map((e) => ({ ...e })),
    { tag: addTag.value, id: addTag.value === 'mask' ? null : Number.NaN, permissions: 0 },
  ]
  emit('update:modelValue', entries)
}
function remove(index: number) {
  if (!props.disabled)
    emit(
      'update:modelValue',
      props.modelValue.filter((_, n) => n !== index).map((e) => ({ ...e })),
    )
}
</script>
<template>
  <section class="acl-target-editor" :aria-label="label">
    <h4>{{ label }}</h4>
    <div v-for="(entry, index) in modelValue" :key="index" class="acl-target-editor__row">
      <span>{{ aclEntryLabel(entry) }}</span>
      <el-input
        v-if="entry.tag === 'user' || entry.tag === 'group'"
        :model-value="Number.isNaN(entry.id) ? '' : String(entry.id)"
        :aria-label="`${label} ${entry.tag} ID ${index + 1}`"
        inputmode="numeric"
        maxlength="10"
        placeholder="数值ID，0有效"
        :disabled="disabled"
        @update:model-value="replace(index, { id: parseAclId($event) })"
      />
      <el-select
        :model-value="entry.permissions"
        :aria-label="`${label} 权限 ${index + 1}`"
        :disabled="disabled"
        @update:model-value="replace(index, { permissions: $event })"
      >
        <el-option
          v-for="bits in [0, 1, 2, 3, 4, 5, 6, 7]"
          :key="bits"
          :value="bits"
          :label="`${bits} · ${bits & 4 ? 'r' : '-'}${bits & 2 ? 'w' : '-'}${bits & 1 ? 'x' : '-'}`"
        />
      </el-select>
      <el-button
        v-if="['user', 'group', 'mask'].includes(entry.tag)"
        :disabled="disabled"
        :aria-label="`${label} 移除条目 ${index + 1}`"
        @click="remove(index)"
        >移除</el-button
      >
    </div>
    <div class="acl-target-editor__actions">
      <el-select v-model="addTag" :aria-label="`${label} 新增条目类型`" :disabled="disabled"
        ><el-option value="user" label="指定用户" /><el-option
          value="group"
          label="指定组" /><el-option value="mask" label="mask"
      /></el-select>
      <el-button :disabled="disabled || modelValue.length >= 64" @click="add"
        >添加{{ label }}条目</el-button
      >
    </div>
    <p>完整目标模板，不是原ACL；命名用户/组须有mask。相同条目不允许重复，每组最多64项。</p>
  </section>
</template>
<style scoped>
.acl-target-editor {
  min-width: 0;
  margin-block: 12px;
  font-size: 13px;
}
.acl-target-editor__row,
.acl-target-editor__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-block: 8px;
}
.acl-target-editor__row > span {
  flex: 1 1 110px;
  overflow-wrap: anywhere;
}
.acl-target-editor__row .el-input {
  flex: 1 1 120px;
  width: 120px;
}
.acl-target-editor__row .el-select {
  flex: 1 1 100px;
  width: 100px;
}
.acl-target-editor__actions .el-select {
  width: 130px;
}
.acl-target-editor p {
  color: var(--el-text-color-secondary);
}
</style>
