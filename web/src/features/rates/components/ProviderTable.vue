<script setup lang="ts">
import { computed } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiFileSearchOutline } from '@mdi/js'
import { money, plural } from '@/lib/format'
import { capitalize, feeLabel, positionParts, typicalPrice } from '@/lib/plainNumbers'
import type { ModifierRef, RateGroup, RateRow, RateRowsPage } from '../api/types'
import { modifierText } from '../lib/lenses'
import type { SortKey } from '../lib/searchParams'

const props = defineProps<{
  rows: RateRowsPage
  groups: RateGroup[]
  sort: SortKey
  loading?: boolean
  /** The search named a provider (NPI, EIN or name): its rows get the copper "your provider" edge. */
  yours?: boolean
}>()
const emit = defineEmits<{
  updateOptions: [options: { page: number; pageSize: number; sort: SortKey }]
  openSource: [row: RateRow]
  openNpis: [row: RateRow]
}>()

const { smAndDown } = useDisplay()

const headers = [
  { title: 'Provider', key: 'provider', sortable: true },
  { title: 'Fee, setting and plan', key: 'group', sortable: false },
  { title: 'Price', key: 'rate', sortable: true, align: 'end' as const },
  // Sorts by the price relative to typical (the API's vsMedian), so a row far above typical in a cheap service ranks high too.
  { title: 'Compared with typical', key: 'vsMedian', sortable: true },
  { title: 'NPIs', key: 'npis', sortable: false, align: 'end' as const },
  { title: 'Source', key: 'actions', sortable: false, align: 'end' as const },
]

const groupsByKey = computed(() => new Map(props.groups.map((group) => [group.key, group])))
const items = computed(() => props.rows.items.map((row) => ({ ...row, id: `${row.groupKey}::${row.providerKey}`, compared: compared(row) })))

const sortBy = computed(() => {
  const descending = props.sort.startsWith('-')
  return [{ key: descending ? props.sort.slice(1) : props.sort, order: descending ? ('desc' as const) : ('asc' as const) }]
})

function groupLabel(row: RateRow): string {
  const group = groupsByKey.value.get(row.groupKey)
  if (!group) return '—'
  return `${feeLabel(group.feeType)} · ${group.settingLabel}`
}

/** "Lower than most" plus "$23.34 less than typical" for the row's own price; null under 5 providers. */
function compared(row: RateRow): { label: string | null; difference: string | null } {
  const group = groupsByKey.value.get(row.groupKey)
  const typical = group ? typicalPrice({ ...group.stats, providers: group.providerCount }) : null
  const { label, difference } = positionParts(row.band, row.rate, typical)
  return { label, difference: difference ? capitalize(difference) : null }
}

function modifiersOf(row: RateRow): ModifierRef[] {
  return groupsByKey.value.get(row.groupKey)?.modifiers ?? []
}

/** "modifier 25" or "modifiers 25, 95, GT": short in the cell, with the plain labels in a tooltip. */
function modifierCodes(modifiers: ModifierRef[]): string {
  return `${modifiers.length === 1 ? 'modifier' : 'modifiers'} ${modifiers.map((modifier) => modifier.code).join(', ')}`
}

// Stamped like a ledger mark: outlined in the band's color; typical stays neutral ink.
const bandColor: Record<string, string | undefined> = { low: 'low', typical: undefined, high: 'high' }

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
    density="compact"
    hover
    :row-props="rowProps"
    data-testid="provider-table"
    @update:options="onOptions"
  >
    <template #[`item.provider`]="{ item }">
      <div class="py-1" data-testid="provider-row">
        <v-tooltip v-if="item.otherNames.length" location="top" max-width="360">
          <template #activator="{ props: tip }">
            <span v-bind="tip" class="font-weight-medium has-tooltip" tabindex="0">{{ item.businessName }}</span>
          </template>
          Also listed as: {{ item.otherNames.join('; ') }}
        </v-tooltip>
        <span v-else class="font-weight-medium">{{ item.businessName }}</span>
        <div class="d-flex flex-wrap align-center ga-1 text-body-small text-medium-emphasis">
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
            text="At least one of these rates is a contract with the whole tax ID: the file lists no NPIs for it, so it applies to anyone billing under this tax ID."
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
        <div class="text-body-medium">
          {{ groupLabel(item) }}
          <template v-if="modifiersOf(item).length">
            ·
            <v-tooltip :text="modifierText(modifiersOf(item))" location="top" max-width="320">
              <template #activator="{ props: tip }">
                <span v-bind="tip" class="has-tooltip" tabindex="0" data-testid="row-modifiers">{{ modifierCodes(modifiersOf(item)) }}</span>
              </template>
            </v-tooltip>
          </template>
        </div>
        <div class="text-body-small text-medium-emphasis">{{ groupsByKey.get(item.groupKey)?.productLabel }}</div>
      </div>
    </template>

    <template #[`item.rate`]="{ item }">
      <div class="text-no-wrap">
        <span class="font-weight-medium" data-testid="row-rate">{{ money(item.rate) }}</span>
        <div v-if="item.multipleRates" class="text-body-small text-medium-emphasis">
          {{ money(item.rateLow) }} to {{ money(item.rateHigh) }} ({{ plural(item.rateCount, 'price') }})
        </div>
      </div>
    </template>

    <template #[`item.vsMedian`]="{ item }">
      <div class="py-1" data-testid="row-compared">
        <template v-if="item.compared.label">
          <v-chip :color="bandColor[item.band ?? '']" size="small" variant="outlined" data-testid="band-chip">
            {{ item.compared.label }}
          </v-chip>
          <div v-if="item.compared.difference" class="text-body-small text-medium-emphasis" data-testid="row-difference">
            {{ item.compared.difference }}
          </div>
        </template>
        <span v-else class="text-body-small text-medium-emphasis">Too few to compare</span>
      </div>
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
      <span v-else class="text-body-small text-medium-emphasis">None listed</span>
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
      <p class="text-body-medium text-medium-emphasis py-4">No providers match. Try clearing a filter.</p>
    </template>
  </v-data-table-server>
</template>
