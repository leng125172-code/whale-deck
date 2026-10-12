<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useId } from 'vue'
import type { TelemetrySeries } from '@/services/workstationOverview'
import { TELEMETRY_SLOT_COUNT, telemetrySegments } from '@/services/rollingTelemetry'

const props = defineProps<{
  labels: string[]
  series: TelemetrySeries[]
  unit: string
}>()

const chartId = useId().replace(/:/g, '')
const root = ref<HTMLElement | null>(null)
const width = ref(1000)
let observer: ResizeObserver | undefined
const plot = computed(() => ({
  left: 46,
  top: 18,
  width: Math.max(1, width.value - 60),
  height: 190,
}))
onMounted(() => {
  if (typeof ResizeObserver === 'undefined' || !root.value) return
  observer = new ResizeObserver(([entry]) => {
    if (entry && entry.contentRect.width > 0) width.value = entry.contentRect.width
  })
  observer.observe(root.value)
})
onBeforeUnmount(() => observer?.disconnect())

const maximum = computed(() => {
  const highest = Math.max(
    1,
    ...props.series.flatMap((item) =>
      item.values.filter((value): value is number => value !== null),
    ),
  )
  return Math.ceil(highest * 1.15)
})

const gridLines = computed(() =>
  Array.from({ length: 5 }, (_, index) => {
    const ratio = index / 4
    return {
      y: plot.value.top + ratio * plot.value.height,
      value: maximum.value * (1 - ratio),
    }
  }),
)

function point(value: number, index: number) {
  const x = plot.value.left + (index / (TELEMETRY_SLOT_COUNT - 1)) * plot.value.width
  const y = plot.value.top + plot.value.height - (value / maximum.value) * plot.value.height
  return { x, y }
}

function segmentPath(segment: ReturnType<typeof telemetrySegments>[number]) {
  return segment
    .map((sample, index) => {
      const current = point(sample.value, sample.index)
      return `${index === 0 ? 'M' : 'L'} ${current.x.toFixed(2)} ${current.y.toFixed(2)}`
    })
    .join(' ')
}

function linePath(values: Array<number | null>) {
  return telemetrySegments(values).map(segmentPath).join(' ')
}

function areaPath(values: Array<number | null>) {
  return telemetrySegments(values)
    .map((segment) => {
      const first = point(segment[0]!.value, segment[0]!.index)
      const last = point(segment[segment.length - 1]!.value, segment[segment.length - 1]!.index)
      return `${segmentPath(segment)} L ${last.x.toFixed(2)} ${(plot.value.top + plot.value.height).toFixed(2)} L ${first.x.toFixed(2)} ${(plot.value.top + plot.value.height).toFixed(2)} Z`
    })
    .join(' ')
}

function formatTick(value: number) {
  if (value >= 100) return Math.round(value).toString()
  if (value >= 10) return value.toFixed(0)
  return value.toFixed(1)
}
</script>

<template>
  <div
    ref="root"
    class="telemetry-chart"
    role="img"
    :aria-label="`最近 6 分钟监控趋势，每 6 秒刷新，缺失数据留空，单位 ${unit}`"
  >
    <svg :viewBox="`0 0 ${width} 224`" preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <linearGradient
          v-for="(item, index) in series"
          :id="`${chartId}-gradient-${index}`"
          :key="`${item.name}-gradient`"
          x1="0"
          x2="0"
          y1="0"
          y2="1"
        >
          <stop offset="0%" :stop-color="item.color" stop-opacity="0.24" />
          <stop offset="100%" :stop-color="item.color" stop-opacity="0.02" />
        </linearGradient>
      </defs>

      <g v-for="grid in gridLines" :key="grid.y">
        <line
          class="telemetry-chart__grid"
          :x1="plot.left"
          :x2="plot.left + plot.width"
          :y1="grid.y"
          :y2="grid.y"
        />
        <text class="telemetry-chart__tick" x="37" :y="grid.y + 4" text-anchor="end">
          {{ formatTick(grid.value) }}
        </text>
      </g>

      <path
        v-for="(item, index) in series"
        :key="`${item.name}-area`"
        :d="areaPath(item.values)"
        :fill="`url(#${chartId}-gradient-${index})`"
      />
      <path
        v-for="item in series"
        :key="`${item.name}-line`"
        class="telemetry-chart__line"
        :d="linePath(item.values)"
        fill="none"
        :stroke="item.color"
      />
    </svg>
    <div class="telemetry-chart__axis" aria-hidden="true">
      <span>6 分钟前</span><span class="telemetry-chart__midpoint">3 分钟前</span><span>现在</span>
    </div>
  </div>
</template>

<style scoped>
.telemetry-chart {
  overflow: hidden;
  width: 100%;
  min-width: 0;
}

.telemetry-chart svg {
  display: block;
  width: 100%;
  height: 224px;
}

.telemetry-chart__grid {
  stroke: var(--el-border-color-lighter);
  stroke-dasharray: 4 5;
  vector-effect: non-scaling-stroke;
}

.telemetry-chart__line {
  stroke-linecap: round;
  stroke-linejoin: round;
  stroke-width: 2.2;
  vector-effect: non-scaling-stroke;
}

.telemetry-chart__tick {
  fill: var(--el-text-color-secondary);
  font-family: var(--whaledeck-font-family);
  font-size: 11px;
}

.telemetry-chart__axis {
  display: flex;
  justify-content: space-between;
  margin: 0 14px 14px 46px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 18px;
}

@media (max-width: 480px) {
  .telemetry-chart__midpoint {
    display: none;
  }
}
</style>
