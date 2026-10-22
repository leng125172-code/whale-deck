import { createApp } from 'vue'
import { createPinia } from 'pinia'
import {
  ElAlert,
  ElBadge,
  ElButton,
  ElCheckbox,
  ElDescriptions,
  ElDescriptionsItem,
  ElDialog,
  ElDrawer,
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElIcon,
  ElInput,
  ElInputNumber,
  ElLoading,
  ElMenu,
  ElMenuItem,
  ElOption,
  ElProgress,
  ElPopover,
  ElRadioButton,
  ElRadioGroup,
  ElSegmented,
  ElSelect,
  ElSwitch,
  ElTabPane,
  ElTable,
  ElTableColumn,
  ElTabs,
  ElTag,
  ElTimeline,
  ElTimelineItem,
  ElTooltip,
} from 'element-plus'
import 'element-plus/dist/index.css'
import 'element-plus/theme-chalk/dark/css-vars.css'

import App from './App.vue'
import router from './router'
import { authSession } from '@/services/authSession'
import './styles/main.css'

const app = createApp(App)
const stopSessionLifecycle = authSession.bindLifecycle(() => {
  const current = router.currentRoute.value
  if (current.meta.requiresAuth)
    void router.replace({ name: 'login', query: { returnUrl: current.fullPath } })
})
if (import.meta.hot) import.meta.hot.dispose(stopSessionLifecycle)

app.use(createPinia())
app.use(router)
for (const component of [
  ElAlert,
  ElBadge,
  ElButton,
  ElCheckbox,
  ElDescriptions,
  ElDescriptionsItem,
  ElDialog,
  ElDrawer,
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElIcon,
  ElInput,
  ElInputNumber,
  ElMenu,
  ElMenuItem,
  ElOption,
  ElProgress,
  ElPopover,
  ElRadioButton,
  ElRadioGroup,
  ElSegmented,
  ElSelect,
  ElSwitch,
  ElTabPane,
  ElTable,
  ElTableColumn,
  ElTabs,
  ElTag,
  ElTimeline,
  ElTimelineItem,
  ElTooltip,
]) {
  app.component(component.name!, component)
}
app.directive('loading', ElLoading.directive)

app.mount('#app')
