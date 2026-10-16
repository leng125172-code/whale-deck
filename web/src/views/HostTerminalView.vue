<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { Terminal } from '@xterm/xterm'
import { FitAddon } from '@xterm/addon-fit'
import '@xterm/xterm/css/xterm.css'
import { authSession } from '@/services/authSession'
import { createHostTerminal, type TerminalPhase } from '@/services/hostTerminal'

const router = useRouter()
const host = ref<HTMLDivElement | null>(null)
const phase = ref<TerminalPhase>('idle')
const message = ref('尚未连接。点击开始后申请一次性连接许可。')
const warning = ref('')
const terminalSize = ref('80 × 24')
const exitCode = ref<number | undefined>()
const confirming = ref(false)
const screenReader = ref(false)
const busy = computed(() => confirming.value || ['preparing', 'connecting'].includes(phase.value))
const canManage = computed(() => authSession.user.value?.isAdministrator === true)
const statusLabel = computed(
  () =>
    ({
      idle: '未连接',
      preparing: '准备中',
      connecting: '连接中',
      open: '已连接',
      closed: '已结束',
      error: '不可用',
    })[phase.value],
)
let terminal: Terminal | undefined
let fit: FitAddon | undefined
let resizeObserver: ResizeObserver | undefined
let themeObserver: MutationObserver | undefined
let resizeFrame = 0
let unmounting = false

function theme() {
  const style = getComputedStyle(document.documentElement)
  return {
    background: style.getPropertyValue('--el-bg-color').trim() || '#ffffff',
    foreground: style.getPropertyValue('--el-text-color-primary').trim() || '#303133',
    cursor: style.getPropertyValue('--el-color-primary').trim() || '#409eff',
    selectionBackground: document.documentElement.classList.contains('dark')
      ? '#264f78'
      : '#cde3fc',
  }
}
function fitTerminal() {
  if (!terminal || !fit || !host.value?.isConnected || unmounting) return
  const size = fit.proposeDimensions()
  if (!size) return
  const columns = Math.max(2, Math.min(400, size.cols))
  const rows = Math.max(2, Math.min(400, size.rows))
  terminal.resize(columns, rows)
  terminalSize.value = `${columns} × ${rows}`
  client.resize(columns, rows)
}
function scheduleFit() {
  cancelAnimationFrame(resizeFrame)
  resizeFrame = requestAnimationFrame(fitTerminal)
}
function resetTerminal() {
  terminal?.dispose()
  terminal = undefined
  fit = undefined
  if (!host.value || unmounting) return
  host.value.replaceChildren()
  terminal = new Terminal({
    fontFamily:
      getComputedStyle(document.documentElement)
        .getPropertyValue('--whaledeck-font-family')
        .trim() || 'monospace',
    fontSize: 13,
    lineHeight: 1.25,
    scrollback: 1000,
    cursorBlink: false,
    allowProposedApi: false,
    disableStdin: phase.value !== 'open',
    theme: theme(),
    screenReaderMode: screenReader.value,
    convertEol: false,
  })
  fit = new FitAddon()
  terminal.loadAddon(fit)
  terminal.open(host.value)
  terminal.textarea?.setAttribute('aria-label', '宿主终端输入')
  terminal.onData((data) => client.input(data))
  terminal.onBinary((data) => client.input(data, true))
  scheduleFit()
}
async function checkPermission() {
  try {
    await authSession.refresh()
  } catch {
    /* The terminal is already closed and cleared. */
  }
  if (!authSession.authenticated.value)
    await router.replace({ path: '/login', query: { returnUrl: '/host/terminal' } })
  else if (!canManage.value) await router.replace('/')
}
const client = createHostTerminal({
  state(next, text, code) {
    phase.value = next
    message.value = text
    exitCode.value = code
    if (terminal) terminal.options.disableStdin = next !== 'open'
  },
  write(bytes, consumed) {
    if (terminal) terminal.write(bytes, consumed)
    else consumed()
  },
  clear: resetTerminal,
  ready() {
    warning.value = ''
    scheduleFit()
    terminal?.focus()
  },
  notice(text) {
    warning.value = text
  },
  permissionLost() {
    void checkPermission()
  },
})

function start() {
  if (busy.value || phase.value === 'open' || !canManage.value) return
  confirming.value = true
}
async function confirmConnection() {
  if (!confirming.value || unmounting || !canManage.value) return
  confirming.value = false
  warning.value = ''
  await nextTick()
  if (!unmounting && canManage.value) await client.connect()
}
function disconnect() {
  client.disconnect()
}
function clear() {
  terminal?.clear()
  warning.value = ''
  terminal?.focus()
}
watch(screenReader, (enabled) => {
  if (terminal) terminal.options.screenReaderMode = enabled
})
watch(canManage, (allowed) => {
  if (!allowed) {
    confirming.value = false
    client.disconnect()
    resetTerminal()
    phase.value = 'error'
    message.value = '当前账号不具备终端管理权限。'
  }
})
onMounted(() => {
  resetTerminal()
  resizeObserver = new ResizeObserver(scheduleFit)
  if (host.value) resizeObserver.observe(host.value)
  themeObserver = new MutationObserver(() => {
    if (terminal) terminal.options.theme = theme()
    scheduleFit()
  })
  themeObserver.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ['class', 'style'],
  })
  void document.fonts.ready.then(() => {
    if (!unmounting) scheduleFit()
  })
})
onBeforeUnmount(() => {
  unmounting = true
  confirming.value = false
  resizeObserver?.disconnect()
  themeObserver?.disconnect()
  cancelAnimationFrame(resizeFrame)
  client.dispose()
  terminal?.dispose()
  terminal = undefined
})
</script>

<template>
  <div class="terminal-page">
    <header class="page-heading">
      <div>
        <span class="panel__eyebrow">HOST TERMINAL</span>
        <h1>宿主终端</h1>
        <p>受限维护会话 · 非 root · 不具备 sudo / Docker 权限</p>
      </div>
      <div class="terminal-actions">
        <el-button :disabled="!canManage" @click="clear">清屏</el-button>
        <el-button v-if="phase === 'open'" type="danger" plain @click="disconnect"
          >断开连接</el-button
        >
        <el-button v-else type="primary" :loading="busy" :disabled="!canManage" @click="start">{{
          phase === 'idle' ? '开始连接' : '重新连接'
        }}</el-button>
      </div>
    </header>
    <el-alert
      v-if="warning"
      :title="warning"
      type="warning"
      :closable="true"
      show-icon
      @close="warning = ''"
    />
    <section class="panel terminal-panel" aria-label="受限宿主终端">
      <div class="terminal-toolbar">
        <div class="terminal-indicator" role="status" aria-live="polite">
          <span
            class="terminal-dot"
            :class="{ 'is-open': phase === 'open', 'is-error': phase === 'error' }"
          />
          <span>{{ statusLabel }}</span>
          <span v-if="exitCode !== undefined" class="terminal-exit">退出码 {{ exitCode }}</span>
        </div>
        <div class="terminal-meta">
          <label class="terminal-accessibility"
            ><span>读屏</span
            ><el-switch v-model="screenReader" aria-label="切换终端读屏模式" size="small"
          /></label>
          <span class="terminal-size" aria-label="终端尺寸">{{ terminalSize }}</span>
        </div>
      </div>
      <div ref="host" class="terminal-surface" data-testid="host-terminal-surface" />
      <div class="terminal-message" :class="{ 'is-error': phase === 'error' }" role="status">
        {{ message }}
      </div>
    </section>
    <p class="terminal-hint">
      离开页面会断开会话，重新连接需要再次确认。请勿在终端输入长期凭据；正文仅在当前页面的有界显示缓冲中保留，清屏不等同于撤销已执行的命令。
    </p>
    <el-dialog
      v-model="confirming"
      title="建立宿主终端连接"
      width="min(520px, calc(100vw - 32px))"
      :close-on-click-modal="false"
      append-to-body
      align-center
      destroy-on-close
    >
      <p class="terminal-confirmation">
        将以受限的非 root 宿主身份打开交互终端，不具备 sudo 或 Docker 权限。会话最多 10 分钟，空闲约
        2 分钟会结束；仅记录会话元数据，不记录终端正文。确认连接？
      </p>
      <template #footer>
        <el-button @click="confirming = false">取消</el-button>
        <el-button type="primary" :disabled="!canManage" @click="confirmConnection"
          >确认连接</el-button
        >
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.terminal-page {
  display: grid;
  gap: 16px;
  min-width: 0;
}
.terminal-page .page-heading {
  margin-bottom: 10px;
}
.terminal-actions,
.terminal-toolbar,
.terminal-indicator,
.terminal-meta,
.terminal-accessibility {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  min-width: 0;
}
.terminal-actions .el-button + .el-button {
  margin-left: 0;
}
.terminal-panel {
  min-width: 0;
  overflow: hidden;
}
.terminal-toolbar {
  justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  font-size: 12px;
}
.terminal-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--el-text-color-placeholder);
}
.terminal-dot.is-open {
  background: var(--el-color-success);
}
.terminal-dot.is-error {
  background: var(--el-color-danger);
}
.terminal-size,
.terminal-exit {
  color: var(--el-text-color-secondary);
}
.terminal-surface {
  height: clamp(160px, calc(100dvh - 370px), 760px);
  min-width: 0;
  overflow: hidden;
  padding: 12px;
  background: var(--el-bg-color);
}
.terminal-surface :deep(.xterm) {
  height: 100%;
}
.terminal-message {
  padding: 10px 16px;
  border-top: 1px solid var(--el-border-color-lighter);
  font-size: 12px;
  color: var(--el-text-color-secondary);
  overflow-wrap: anywhere;
}
.terminal-message.is-error {
  color: var(--el-color-danger);
}
.terminal-hint {
  margin: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.8;
  overflow-wrap: anywhere;
}
.terminal-confirmation {
  margin: 0;
  line-height: 1.8;
  overflow-wrap: anywhere;
}
@media (max-width: 600px) {
  .terminal-actions {
    width: 100%;
  }
  .terminal-surface {
    padding: 8px;
    height: clamp(160px, calc(100dvh - 410px), 560px);
  }
}
</style>
