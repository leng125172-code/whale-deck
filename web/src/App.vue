<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterView, useRouter } from 'vue-router'
import BootScreen from '@/components/BootScreen.vue'
import { authSession } from '@/services/authSession'

const minimumBootDuration = 3000
const router = useRouter()
const isBooting = ref(true)

onMounted(async () => {
  await Promise.all([
    authSession.initialize(),
    router.isReady(),
    new Promise((resolve) => window.setTimeout(resolve, minimumBootDuration)),
  ])

  isBooting.value = false
})
</script>

<template>
  <RouterView v-slot="{ Component }">
    <Transition name="boot-fade" mode="out-in">
      <BootScreen v-if="isBooting" key="boot" />
      <component :is="Component" v-else />
    </Transition>
  </RouterView>
</template>
