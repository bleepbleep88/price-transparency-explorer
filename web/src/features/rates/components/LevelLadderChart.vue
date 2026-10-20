<script setup lang="ts">
// Loaded on demand (defineAsyncComponent in LevelLadder.vue), so ECharts is only downloaded for visit-level searches.
import { computed } from 'vue'
import { useTheme } from 'vuetify'
import VChart from 'vue-echarts'
import { use } from 'echarts/core'
import { BarChart, ScatterChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { SVGRenderer } from 'echarts/renderers'
import { dollars } from '@/lib/plainNumbers'
import { levelLabel, type LadderStep } from '../lib/levels'

use([BarChart, ScatterChart, GridComponent, LegendComponent, TooltipComponent, SVGRenderer])

const props = defineProps<{ steps: LadderStep[]; current: string }>()
const theme = useTheme()

const option = computed(() => {
  const colors = theme.current.value.colors
  const ink = colors['on-surface']
  const showMost = props.steps.some((step) => step.most)
  return {
    animation: false,
    grid: { left: 56, right: 16, top: showMost ? 36 : 16, bottom: 44 },
    legend: showMost
      ? { top: 0, left: 0, itemWidth: 14, itemHeight: 10, textStyle: { color: ink }, data: ['Typical price', 'Most providers get between'] }
      : undefined,
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      formatter: (items: { dataIndex: number }[]) => {
        const step = props.steps[items[0]?.dataIndex ?? 0]
        return step ? `<strong>${levelLabel(step)}</strong><br>${step.text}` : ''
      },
    },
    xAxis: {
      type: 'category',
      data: props.steps.map((step) => levelLabel(step)),
      // Every level's label, on two lines so five fit on a phone: "Level 3" over "(99213)".
      axisLabel: {
        color: ink,
        interval: 0,
        formatter: (value: string) => value.replace(' (', '\n('),
        fontWeight: (value: string) => (value.includes(props.current) ? 'bold' : 'normal'),
      },
      axisLine: { lineStyle: { color: ink, opacity: 0.4 } },
    },
    yAxis: {
      type: 'value',
      min: 0,
      axisLabel: { color: ink, formatter: (value: number) => dollars(value) },
      splitLine: { lineStyle: { color: ink, opacity: 0.12 } },
    },
    series: [
      // "Most providers" as a band: an invisible bar up to the low end, then the band to the high end.
      {
        type: 'bar',
        stack: 'most',
        silent: true,
        itemStyle: { color: 'transparent' },
        data: props.steps.map((step) => step.most?.low ?? '-'),
        barWidth: '36%',
      },
      {
        name: 'Most providers get between',
        type: 'bar',
        stack: 'most',
        itemStyle: { color: colors.primary, opacity: 0.22 },
        data: props.steps.map((step) => (step.most ? step.most.high - step.most.low : '-')),
        barWidth: '36%',
      },
      {
        name: 'Typical price',
        type: 'scatter',
        symbolSize: 14,
        itemStyle: { color: colors.primary },
        label: { show: true, position: 'right', color: ink, formatter: ({ value }: { value: number }) => dollars(value) },
        data: props.steps.map((step) => step.typical ?? '-'),
      },
    ],
  }
})

const label = computed(() =>
  props.steps
    .filter((step) => step.typical != null)
    .map((step) => `${levelLabel(step)}: typical price ${dollars(step.typical!)}`)
    .join('; '),
)
</script>

<template>
  <div role="img" :aria-label="`Typical price by visit level. ${label}.`" data-testid="level-ladder-chart">
    <v-chart :option="option" autoresize class="level-ladder-chart" />
  </div>
</template>

<style scoped>
.level-ladder-chart {
  height: 260px;
  width: 100%;
}
</style>
