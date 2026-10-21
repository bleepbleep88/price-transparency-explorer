<script setup lang="ts">
// Loaded on demand (defineAsyncComponent in LevelLadder.vue), so ECharts is only downloaded for visit-level searches.
import { computed } from 'vue'
import { useTheme } from 'vuetify'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { SVGRenderer } from 'echarts/renderers'
import { dollars } from '@/lib/plainNumbers'
import { levelLabel, type LadderStep } from '../lib/levels'

use([BarChart, GridComponent, TooltipComponent, SVGRenderer])

// One bar per level: its typical price, labelled in dollars. The searched level is the green one; a level with too
// few places has no bar.
const props = defineProps<{ steps: LadderStep[]; current: string }>()
const theme = useTheme()

const option = computed(() => {
  const colors = theme.current.value.colors
  const ink = colors['on-surface']
  return {
    animation: false,
    grid: { left: 8, right: 8, top: 24, bottom: 24, containLabel: true },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'none' },
      formatter: (items: { dataIndex: number }[]) => {
        const step = props.steps[items[0]?.dataIndex ?? 0]
        return step ? `<strong>${levelLabel(step)}</strong><br>${step.text}` : ''
      },
    },
    xAxis: {
      type: 'category',
      data: props.steps.map((step) => levelLabel(step)),
      axisTick: { show: false },
      axisLine: { lineStyle: { color: ink, opacity: 0.3 } },
      axisLabel: {
        color: ink,
        interval: 0,
        formatter: (value: string) => (value === currentLabel.value ? `{current|${value}}` : value),
        rich: { current: { color: ink, fontWeight: 'bold' } },
      },
    },
    yAxis: { type: 'value', min: 0, show: false },
    series: [
      {
        type: 'bar',
        barWidth: '48%',
        data: props.steps.map((step) => ({
          value: step.typical ?? '-',
          itemStyle: { color: colors.primary, opacity: step.code === props.current ? 1 : 0.35, borderRadius: [4, 4, 0, 0] },
        })),
        label: { show: true, position: 'top', color: ink, formatter: ({ value }: { value: number }) => dollars(value) },
      },
    ],
  }
})

const currentLabel = computed(() => {
  const step = props.steps.find((candidate) => candidate.code === props.current)
  return step ? levelLabel(step) : null
})

const label = computed(() =>
  props.steps
    .filter((step) => step.typical != null)
    .map((step) => `${levelLabel(step)} ${dollars(step.typical!)}`)
    .join(', '),
)
</script>

<template>
  <div role="img" :aria-label="`Typical price by level: ${label}`" data-testid="level-ladder-chart">
    <v-chart :option="option" autoresize class="level-ladder-chart" />
  </div>
</template>

<style scoped>
.level-ladder-chart {
  height: 200px;
  width: 100%;
}
</style>
