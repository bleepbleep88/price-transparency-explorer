<script setup lang="ts">
import { computed } from 'vue'
import { useTheme } from 'vuetify'
import { use } from 'echarts/core'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { SVGRenderer } from 'echarts/renderers'
import VChart from 'vue-echarts'
import { money, plural, shortDate } from '@/lib/format'
import type { GroupTrend } from '../api/types'
import { trendSeries } from '../lib/trend'

// Loaded on demand (defineAsyncComponent in TrendLine), so ECharts only downloads once a trend shows. The text next to
// the chart says the same numbers, and that is what screen readers get.
use([LineChart, GridComponent, TooltipComponent, SVGRenderer])

const props = defineProps<{ trend: GroupTrend; selectedId: number | null; showProvider: boolean }>()
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

function tooltip(index: number): string {
  const point = props.trend.points[index]
  if (!point) return ''
  const lines = [`<strong>${shortDate(point.indexDate)}</strong>`]
  if (point.partial) lines.push('This file looks partial')
  lines.push(point.median != null ? `Median ${money(point.median)}` : 'Too few providers for a median')
  if (point.p25 != null && point.p75 != null) lines.push(`Middle half ${money(point.p25)} to ${money(point.p75)}`)
  lines.push(plural(point.providerCount, 'provider'))
  if (props.showProvider && point.provider) lines.push(`Your provider ${money(point.provider.rate)}`)
  return lines.join('<br>')
}

const option = computed(() => {
  const ink = hex('on-surface', '#1C2321')
  const primary = hex('primary', '#0E6B4E')
  const copper = hex('copper', '#C0662B')
  const series = trendSeries(props.trend, props.selectedId, props.showProvider)
  // The snapshot on screen gets the bigger dot.
  const symbolSize = (_: unknown, params: { dataIndex: number }) => (params.dataIndex === series.selected ? 9 : 5)
  return {
    animation: false,
    textStyle: { fontFamily: "'IBM Plex Sans Variable', 'IBM Plex Sans', system-ui, sans-serif", color: ink },
    grid: { left: 4, right: 4, top: 8, bottom: 4, containLabel: true },
    tooltip: {
      trigger: 'axis',
      confine: true,
      formatter: (items: { dataIndex: number }[]) => tooltip(items[0]?.dataIndex ?? -1),
    },
    xAxis: {
      type: 'category',
      data: series.labels,
      boundaryGap: false,
      axisTick: { show: false },
      axisLine: { lineStyle: { color: rgba(ink, 0.3) } },
      axisLabel: { color: rgba(ink, 0.74), fontSize: 11 },
    },
    // From zero, so a 2% move looks like 2%: the text gives the exact figures.
    yAxis: { type: 'value', min: 0, show: false },
    series: [
      { type: 'line', data: series.p25, stack: 'middle-half', symbol: 'none', lineStyle: { opacity: 0 }, silent: true },
      {
        type: 'line',
        data: series.band,
        stack: 'middle-half',
        symbol: 'none',
        lineStyle: { opacity: 0 },
        areaStyle: { color: rgba(primary, 0.16) },
        silent: true,
      },
      { type: 'line', data: series.median, symbolSize, lineStyle: { color: primary, width: 2 }, itemStyle: { color: primary } },
      ...(props.showProvider
        ? [{ type: 'line', data: series.provider, symbolSize, lineStyle: { color: copper, width: 2, type: 'dashed' }, itemStyle: { color: copper } }]
        : []),
    ],
  }
})
</script>

<template>
  <div class="trend-chart" aria-hidden="true" data-testid="trend-chart">
    <v-chart :option="option" autoresize />
  </div>
</template>

<style scoped>
.trend-chart {
  height: 84px;
}
</style>
