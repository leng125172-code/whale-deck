<script setup lang="ts">
import TerminalSessionPanel from '@/components/TerminalSessionPanel.vue'
defineProps<{ containerId: string }>()
</script>

<template><TerminalSessionPanel :key="containerId" :container-id="containerId" /></template>
