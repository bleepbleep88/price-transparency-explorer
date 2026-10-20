<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from 'vuetify'
import { use } from 'echarts/core'
import { BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { SVGRenderer } from 'echarts/renderers'
import VChart from 'vue-echarts'
import { wholeNumber } from '@/lib/format'
import type { RatioBand } from '../api/types'
import { share } from '../lib/insightText'

// Loaded on demand (defineAsyncComponent), so ECharts only downloads when a comparison is shown. The numbers are also
// in a table next to the chart, which is what screen readers get.
use([BarChart, GridComponent, TooltipComponent, SVGRenderer])

const props = defineProps<{ bands: RatioBand[] }>()
const theme = useTheme()

function rgba(hex: string, alpha: number): string {
  const value = Number.parseInt(hex.replace('#', '').slice(0, 6), 16)
  return `rgba(${(value >> 16) & 255}, ${(value >> 8) & 255}, ${value & 255}, ${alpha})`
}

/** A theme color as a hex string (the Ledger theme defines every one used here as hex). */
function hex(name: string, fallback: string): string {
  const value = theme.current.value.colors[name]
  return typeof value === 'string' ? value : fallback
}

const option = computed(() => {
  const ink = hex('on-surface', '#1C2321')
  const fill = { lower: hex('low', ink), same: rgba(ink, 0.35), higher: hex('high', ink) }
  const total = props.bands.reduce((sum, band) => sum + band.count, 0)
  return {
    animation: false,
    textStyle: { fontFamily: "'IBM Plex Sans Variable', 'IBM Plex Sans', system-ui, sans-serif", color: ink },
    grid: { left: 8, right: 56, top: 8, bottom: 8, containLabel: true },
    tooltip: {
      trigger: 'item',
      formatter: (item: { name: string; value: number }) => `${item.name}: ${wholeNumber(item.value)} pairs (${share(item.value, total)})`,
    },
    xAxis: { type: 'value', show: false },
    yAxis: {
      type: 'category',
      inverse: true,
      data: props.bands.map((band) => band.label),
      axisTick: { show: false },
      axisLine: { lineStyle: { color: rgba(ink, 0.4) } },
      axisLabel: { color: ink },
    },
    series: [
      {
        type: 'bar',
        barMaxWidth: 18,
        data: props.bands.map((band) => ({ value: band.count, itemStyle: { color: fill[band.direction] } })),
        label: { show: true, position: 'right', color: ink, formatter: (item: { value: number }) => wholeNumber(item.value) },
      },
    ],
  }
})
</script>

<template>
  <div class="ratio-chart" aria-hidden="true" data-testid="ratio-chart">
    <v-chart :option="option" autoresize />
  </div>
</template>

<style scoped>
.ratio-chart {
  height: 300px;
}
</style>
