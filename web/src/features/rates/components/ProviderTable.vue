<script setup lang="ts">
import { computed } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiDotsVertical } from '@mdi/js'
import { money } from '@/lib/format'
import { placeName, providerName } from '@/lib/plainNames'
import { capitalize, positionParts, typicalPrice } from '@/lib/plainNumbers'
import type { DirectorySource, RateGroup, RateRow, RateRowsPage } from '../api/types'
import type { SortKey } from '../lib/searchParams'

// Every place for the bill picked: name and where, price, and how it compares. Who works there and where the price
// comes from open from the row's menu.
const props = defineProps<{
  rows: RateRowsPage
  groups: RateGroup[]
  sort: SortKey
  loading?: boolean
  /** The search named a provider (NPI, EIN or name): its rows get the copper edge. */
  yours?: boolean
  directory?: DirectorySource | null
}>()
const emit = defineEmits<{
  updateOptions: [options: { page: number; pageSize: number; sort: SortKey }]
  openSource: [row: RateRow]
  openNpis: [row: RateRow]
}>()

const { smAndDown } = useDisplay()

const headers = [
  { title: 'Place', key: 'provider', sortable: true },
  { title: 'Price', key: 'rate', sortable: true, align: 'end' as const },
  { title: 'Compared with typical', key: 'vsMedian', sortable: true },
  { title: '', key: 'actions', sortable: false, align: 'end' as const, width: 48 },
]

const groupsByKey = computed(() => new Map(props.groups.map((group) => [group.key, group])))
const items = computed(() =>
  props.rows.items.map((row) => ({
    ...row,
    id: `${row.groupKey}::${row.providerKey}`,
    name: providerName(row.businessName),
    place: placeName(row.profile?.locations[0]),
    compared: compared(row),
  })),
)

const sortBy = computed(() => {
  const descending = props.sort.startsWith('-')
  return [{ key: descending ? props.sort.slice(1) : props.sort, order: descending ? ('desc' as const) : ('asc' as const) }]
})

/** "Lower than most" and "$23.34 less than typical" for the row's own price; nothing under 5 providers. */
function compared(row: RateRow): { label: string | null; difference: string | null } {
  const group = groupsByKey.value.get(row.groupKey)
  const typical = group ? typicalPrice({ ...group.stats, providers: group.providerCount }) : null
  const { label, difference } = positionParts(row.band, row.rate, typical)
  return { label, difference: difference ? capitalize(difference) : null }
}

const bandClass: Record<string, string> = { low: 'text-low', high: 'text-high' }

function rowProps({ item }: { item: RateRow }) {
  return props.yours && item.matched ? { class: 'row-yours' } : {}
}

function onOptions(options: { page: number; itemsPerPage: number; sortBy: { key: string; order?: boolean | 'asc' | 'desc' }[] }) {
  const first = options.sortBy[0]
  const sort = (first ? `${first.order === 'desc' ? '-' : ''}${first.key}` : 'rate') as SortKey
  if (options.page === props.rows.page && options.itemsPerPage === props.rows.pageSize && sort === props.sort) return
  emit('updateOptions', { page: sort === props.sort ? options.page : 1, pageSize: options.itemsPerPage, sort })
}
</script>

<template>
  <v-data-table-server
    :headers="headers"
    :items="items"
    item-value="id"
    :items-length="rows.total"
    :page="rows.page"
    :items-per-page="rows.pageSize"
    :items-per-page-options="[10, 25, 50, 100]"
    :sort-by="sortBy"
    must-sort
    :loading="loading"
    :mobile="smAndDown"
    density="comfortable"
    hover
    :row-props="rowProps"
    data-testid="provider-table"
    @update:options="onOptions"
  >
    <template #[`item.provider`]="{ item }">
      <div class="py-1" data-testid="provider-row">
        <div class="font-weight-medium">{{ item.name }}</div>
        <div v-if="item.place" class="text-body-small text-medium-emphasis" data-testid="row-place">{{ item.place }}</div>
      </div>
    </template>

    <template #[`item.rate`]="{ item }">
      <span class="font-weight-medium text-no-wrap" data-testid="row-rate">{{ money(item.rate) }}</span>
    </template>

    <template #[`item.vsMedian`]="{ item }">
      <div class="py-1" data-testid="row-compared">
        <span v-if="item.compared.label" :class="bandClass[item.band ?? '']" class="font-weight-medium" data-testid="band-label">
          {{ item.compared.label }}
        </span>
        <div v-if="item.compared.difference" :class="item.compared.label ? 'text-body-small text-medium-emphasis' : ''" data-testid="row-difference">
          {{ item.compared.difference }}
        </div>
        <span v-if="!item.compared.label && !item.compared.difference" class="text-body-small text-medium-emphasis">Too few to compare</span>
      </div>
    </template>

    <template #[`item.actions`]="{ item }">
      <v-menu location="bottom end">
        <template #activator="{ props: menu }">
          <v-btn v-bind="menu" :icon="mdiDotsVertical" variant="text" size="small" :aria-label="`More about ${item.name}`" data-testid="row-menu" />
        </template>
        <v-list density="compact">
          <v-list-item v-if="item.npiCount > 0" :title="`Who works here (${item.npiCount})`" @click="emit('openNpis', item)" />
          <v-list-item title="Where this price comes from" data-testid="open-source" @click="emit('openSource', item)" />
          <v-list-item v-for="flag in item.flags" :key="flag.kind" :subtitle="flag.message" title="Check this" class="text-warning" />
        </v-list>
      </v-menu>
    </template>

    <template #no-data>
      <p class="text-body-medium text-medium-emphasis py-4">No places match. Try clearing a filter.</p>
    </template>
  </v-data-table-server>
</template>
