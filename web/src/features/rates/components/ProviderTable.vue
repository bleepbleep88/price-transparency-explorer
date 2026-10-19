<script setup lang="ts">
import { computed } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiFileSearchOutline } from '@mdi/js'
import { money, plural, times } from '@/lib/format'
import type { RateGroup, RateRow, RateRowsPage } from '../api/types'
import type { SortKey } from '../lib/searchParams'

const props = defineProps<{ rows: RateRowsPage; groups: RateGroup[]; sort: SortKey; loading?: boolean }>()
const emit = defineEmits<{
  updateOptions: [options: { page: number; pageSize: number; sort: SortKey }]
  openSource: [row: RateRow]
  openNpis: [row: RateRow]
}>()

const { smAndDown } = useDisplay()

const headers = [
  { title: 'Provider', key: 'provider', sortable: true },
  { title: 'Group', key: 'group', sortable: false },
  { title: 'Rate', key: 'rate', sortable: true, align: 'end' as const },
  { title: 'vs median', key: 'vsMedian', sortable: true, align: 'end' as const },
  { title: 'Position', key: 'band', sortable: false },
  { title: 'NPIs', key: 'npis', sortable: false, align: 'end' as const },
  { title: 'Source', key: 'actions', sortable: false, align: 'end' as const },
]

const groupsByKey = computed(() => new Map(props.groups.map((group) => [group.key, group])))
const items = computed(() => props.rows.items.map((row) => ({ ...row, id: `${row.groupKey}::${row.providerKey}` })))

const sortBy = computed(() => {
  const descending = props.sort.startsWith('-')
  return [{ key: descending ? props.sort.slice(1) : props.sort, order: descending ? ('desc' as const) : ('asc' as const) }]
})

function groupLabel(row: RateRow): string {
  const group = groupsByKey.value.get(row.groupKey)
  if (!group) return '—'
  const fee = group.feeType === 'professional' ? 'Professional' : 'Facility'
  const modifiers = group.modifiers.length ? ` · ${group.modifiers.map((m) => m.code).join(', ')}` : ''
  return `${fee} · ${group.settingLabel}${modifiers}`
}

const bandView: Record<string, { label: string; color: string }> = {
  low: { label: 'Low', color: 'success' },
  typical: { label: 'Typical', color: 'default' },
  high: { label: 'High', color: 'warning' },
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
    density="compact"
    hover
    data-testid="provider-table"
    @update:options="onOptions"
  >
    <template #[`item.provider`]="{ item }">
      <div class="py-1" data-testid="provider-row">
        <v-tooltip v-if="item.otherNames.length" location="top" max-width="360">
          <template #activator="{ props: tip }">
            <span v-bind="tip" class="font-weight-medium text-decoration-underline-dotted">{{ item.businessName }}</span>
          </template>
          Also listed as: {{ item.otherNames.join('; ') }}
        </v-tooltip>
        <span v-else class="font-weight-medium">{{ item.businessName }}</span>
        <div class="d-flex flex-wrap align-center ga-1 text-caption text-medium-emphasis">
          <span data-testid="row-ein">EIN {{ item.einDisplay }}</span>
          <v-tooltip
            v-if="item.einMasked"
            text="The IRS never issues this prefix, so the number may be a Social Security number published by mistake. It's masked here; searching by the full number still works."
            max-width="340"
            location="top"
          >
            <template #activator="{ props: tip }">
              <v-chip v-bind="tip" size="x-small" color="warning" variant="tonal">Unverified tax ID</v-chip>
            </template>
          </v-tooltip>
          <v-tooltip
            v-if="item.einLevel"
            text="The file lists no individual NPIs: the rate applies to anyone billing under this tax ID."
            max-width="340"
            location="top"
          >
            <template #activator="{ props: tip }">
              <v-chip v-bind="tip" size="x-small" variant="tonal">Whole tax ID</v-chip>
            </template>
          </v-tooltip>
        </div>
      </div>
    </template>

    <template #[`item.group`]="{ item }">
      <div class="py-1">
        <div class="text-body-2">{{ groupLabel(item) }}</div>
        <div class="text-caption text-medium-emphasis">{{ groupsByKey.get(item.groupKey)?.productLabel }}</div>
      </div>
    </template>

    <template #[`item.rate`]="{ item }">
      <div class="text-no-wrap">
        <span class="font-weight-medium" data-testid="row-rate">{{ money(item.rate) }}</span>
        <div v-if="item.multipleRates" class="text-caption text-medium-emphasis">
          {{ money(item.rateLow) }} to {{ money(item.rateHigh) }} ({{ plural(item.rateCount, 'rate') }})
        </div>
      </div>
    </template>

    <template #[`item.vsMedian`]="{ item }">
      {{ times(item.vsMedian) }}
    </template>

    <template #[`item.band`]="{ item }">
      <v-chip v-if="item.band" :color="bandView[item.band]?.color" size="small" variant="tonal">
        {{ bandView[item.band]?.label }}
      </v-chip>
      <span v-else class="text-caption text-medium-emphasis">Small group</span>
    </template>

    <template #[`item.npis`]="{ item }">
      <v-btn
        v-if="item.npiCount > 0"
        variant="text"
        size="small"
        :aria-label="`List the ${item.npiCount} NPIs for ${item.businessName}`"
        @click="emit('openNpis', item)"
      >
        {{ item.npiCount }}
      </v-btn>
      <span v-else class="text-caption text-medium-emphasis">None listed</span>
    </template>

    <template #[`item.actions`]="{ item }">
      <v-btn
        :icon="mdiFileSearchOutline"
        variant="text"
        size="small"
        :aria-label="`Where ${item.businessName}'s rate comes from`"
        data-testid="open-source"
        @click="emit('openSource', item)"
      />
    </template>

    <template #no-data>
      <p class="text-body-2 text-medium-emphasis py-4">No providers match. Try clearing a filter.</p>
    </template>
  </v-data-table-server>
</template>
