<script setup lang="ts">
import { computed } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiMagnify } from '@mdi/js'
import { money, shortDate } from '@/lib/format'
import { feeLabel } from '@/lib/plainNumbers'
import { modifierText } from '@/features/rates/lib/lenses'
import type { HistorySnapshot, RateChange, RateChangesPage } from '../api/types'
import { changeText } from '../lib/changesText'
import { DEFAULT_CHANGE_SORT, type ChangeSort } from '../lib/changesParams'

const props = defineProps<{
  rows: RateChangesPage
  sort: ChangeSort
  from: HistorySnapshot
  to: HistorySnapshot
  loading?: boolean
}>()
const emit = defineEmits<{ updateOptions: [options: { page: number; pageSize: number; sort: ChangeSort }] }>()

const { smAndDown } = useDisplay()

// The two rate columns are named by their files' dates.
const headers = computed(() => [
  { title: 'Code', key: 'code', sortable: true },
  { title: 'Fee, setting and plan', key: 'group', sortable: false },
  { title: 'Provider', key: 'provider', sortable: true },
  { title: shortDate(props.from.indexDate), key: 'from', sortable: false, align: 'end' as const },
  { title: shortDate(props.to.indexDate), key: 'to', sortable: false, align: 'end' as const },
  { title: 'Change', key: 'changePct', sortable: true, align: 'end' as const },
  { title: '', key: 'actions', sortable: false, align: 'end' as const },
])

const items = computed(() => props.rows.items.map((row) => ({ ...row, id: `${row.groupKey}::${row.providerKey}` })))

// No column sort means the default: the biggest moves first.
const sortBy = computed(() =>
  props.sort === DEFAULT_CHANGE_SORT
    ? []
    : [{ key: props.sort.replace(/^-/, ''), order: props.sort.startsWith('-') ? ('desc' as const) : ('asc' as const) }],
)

function onOptions(options: { page: number; itemsPerPage: number; sortBy: { key: string; order?: boolean | 'asc' | 'desc' }[] }) {
  const first = options.sortBy[0]
  const sort = (first ? `${first.order === 'desc' ? '-' : ''}${first.key}` : DEFAULT_CHANGE_SORT) as ChangeSort
  if (options.page === props.rows.page && options.itemsPerPage === props.rows.pageSize && sort === props.sort) return
  emit('updateOptions', { page: sort === props.sort ? options.page : 1, pageSize: options.itemsPerPage, sort })
}

/** The provider's rows in that group, in the file that has them (the older one for a dropped rate). */
function searchLink(row: RateChange) {
  const snapshot = row.kind === 'dropped' ? props.from : props.to
  return {
    name: 'search',
    query: {
      code: row.code,
      codeType: row.codeType,
      ein: row.providerKey,
      group: row.groupKey,
      ...(snapshot.isLatest ? {} : { snapshot: String(snapshot.snapshotId) }),
    },
  }
}

function otherFile(row: RateChange): string {
  return shortDate((row.kind === 'new' ? props.from : props.to).indexDate)
}

function direction(row: RateChange): string | undefined {
  if (row.kind !== 'changed' || row.change == null) return undefined
  return row.change > 0 ? 'text-high' : 'text-low'
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
    :items-per-page-options="[25, 50, 100]"
    :sort-by="sortBy"
    :loading="loading"
    :mobile="smAndDown"
    density="compact"
    hover
    data-testid="changes-table"
    @update:options="onOptions"
  >
    <template #[`item.code`]="{ item }">
      <div class="py-1" data-testid="change-row">
        <span class="font-weight-medium text-no-wrap">{{ item.codeType }} {{ item.code }}</span>
        <div v-if="item.codeName" class="text-body-small text-medium-emphasis">{{ item.codeName }}</div>
      </div>
    </template>

    <template #[`item.group`]="{ item }">
      <div class="py-1">
        <div class="text-body-medium">
          {{ feeLabel(item.feeType) }} · {{ item.settingLabel }}<template v-if="item.modifiers.length"> · {{ modifierText(item.modifiers) }}</template>
        </div>
        <div class="text-body-small text-medium-emphasis">{{ item.productLabel }}</div>
      </div>
    </template>

    <template #[`item.provider`]="{ item }">
      <div class="py-1">
        <span class="font-weight-medium">{{ item.businessName }}</span>
        <div class="d-flex flex-wrap align-center ga-1 text-body-small text-medium-emphasis">
          <span>EIN {{ item.einDisplay }}</span>
          <v-chip v-if="item.einMasked" size="x-small" color="warning" variant="tonal">Unverified tax ID</v-chip>
        </div>
      </div>
    </template>

    <template #[`item.from`]="{ item }">
      <span class="text-no-wrap">{{ money(item.from) }}</span>
      <div v-if="(item.fromRates ?? 0) > 1" class="text-body-small text-medium-emphasis text-no-wrap">({{ item.fromRates }} rates)</div>
    </template>

    <template #[`item.to`]="{ item }">
      <span class="text-no-wrap font-weight-medium">{{ money(item.to) }}</span>
      <div v-if="(item.toRates ?? 0) > 1" class="text-body-small text-medium-emphasis text-no-wrap">({{ item.toRates }} rates)</div>
    </template>

    <template #[`item.changePct`]="{ item }">
      <div class="d-flex flex-column align-end ga-1 py-1">
        <span class="text-no-wrap font-weight-medium" :class="direction(item)" data-testid="change-text">{{ changeText(item) }}</span>
        <v-tooltip
          v-if="item.listedElsewhere"
          :text="`The ${otherFile(item)} file lists this provider for the same service with other modifiers or in another setting.`"
          max-width="320"
          location="top"
        >
          <template #activator="{ props: tip }">
            <v-chip v-bind="tip" size="x-small" variant="outlined" data-testid="listed-elsewhere">Listed differently</v-chip>
          </template>
        </v-tooltip>
        <v-tooltip
          v-else-if="item.kind === 'changed' && ((item.fromRates ?? 0) > 1 || (item.toRates ?? 0) > 1)"
          text="The provider has several rates here. The one shown is the middle of its clinicians' rates, so it can also move when a rate is added or removed, not only when a price changes."
          max-width="340"
          location="top"
        >
          <template #activator="{ props: tip }">
            <v-chip v-bind="tip" size="x-small" variant="outlined">Several rates</v-chip>
          </template>
        </v-tooltip>
      </div>
    </template>

    <template #[`item.actions`]="{ item }">
      <v-btn
        :to="searchLink(item)"
        :prepend-icon="mdiMagnify"
        variant="text"
        size="small"
        :aria-label="`Search ${item.codeType} ${item.code} for ${item.businessName}`"
        data-testid="change-search"
      >
        Search
      </v-btn>
    </template>

    <template #no-data>
      <p class="text-body-medium text-medium-emphasis py-4">No changes match. Try clearing a filter.</p>
    </template>
  </v-data-table-server>
</template>
