<script setup lang="ts">
import { computed, ref } from 'vue'
import PlaceList, { type PlaceItem } from '@/components/simple/PlaceList.vue'
import { placeName, placesCount, providerName } from '@/lib/plainNames'
import type { DirectorySource, RateGroup, RateRow, RateRowsPage } from '../api/types'
import type { SortKey } from '../lib/searchParams'
import ProviderTable from './ProviderTable.vue'

// The places for the bill picked: the five cheapest (or priciest), then every place on request.
const props = defineProps<{
  rows: RateRowsPage
  groups: RateGroup[]
  group: RateGroup | null
  sort: SortKey
  loading?: boolean
  yours?: boolean
  directory?: DirectorySource | null
}>()
const emit = defineEmits<{
  updateOptions: [options: { page: number; pageSize: number; sort: SortKey }]
  openSource: [row: RateRow]
  openNpis: [row: RateRow]
}>()

const SHOWN = 5
const showAll = ref(false)
const priciest = computed(() => props.sort === '-rate')
// Right after a new pick, the rows still belong to the last bill: wait for this one's.
const stale = computed(() => props.group != null && props.rows.items.some((row) => row.groupKey !== props.group!.key))
const items = computed<PlaceItem[]>(() =>
  props.rows.items.slice(0, SHOWN).map((row) => ({
    key: `${row.groupKey}::${row.providerKey}`,
    name: providerName(row.businessName),
    place: placeName(row.profile?.locations[0]),
    price: row.rate,
    band: row.band === 'low' || row.band === 'high' ? row.band : null,
    yours: props.yours && row.matched,
  })),
)

function order(sort: SortKey) {
  emit('updateOptions', { page: 1, pageSize: props.rows.pageSize, sort })
}

function open(key: string) {
  const row = props.rows.items.find((item) => `${item.groupKey}::${item.providerKey}` === key)
  if (row) emit('openSource', row)
}
</script>

<template>
  <section class="places" aria-labelledby="places-heading" data-testid="places">
    <div class="places-head">
      <h2 id="places-heading" class="section-title">Places</h2>
      <v-btn-toggle
        :model-value="priciest ? '-rate' : 'rate'"
        mandatory
        density="compact"
        variant="outlined"
        divided
        color="primary"
        aria-label="Order"
        @update:model-value="(value: string) => order(value as SortKey)"
      >
        <v-btn value="rate" :aria-pressed="!priciest" data-testid="order-cheapest">Cheapest first</v-btn>
        <v-btn value="-rate" :aria-pressed="priciest" data-testid="order-priciest">Priciest first</v-btn>
      </v-btn-toggle>
    </div>

    <v-skeleton-loader v-if="stale" type="list-item@5" class="bg-transparent" />
    <PlaceList v-else :items="items" :per-unit="group?.perUnit ?? false" @open="open" />

    <v-btn
      v-if="!stale && (rows.total > SHOWN || showAll)"
      variant="text"
      color="primary"
      class="align-self-start px-0"
      data-testid="see-all"
      @click="showAll = !showAll"
    >
      {{ showAll ? 'Show fewer' : `See all ${placesCount(rows.total)}` }}
    </v-btn>

    <ProviderTable
      v-if="showAll && !stale"
      :rows="rows"
      :groups="groups"
      :sort="sort"
      :loading="loading"
      :yours="yours"
      :directory="directory"
      @update-options="emit('updateOptions', $event)"
      @open-source="emit('openSource', $event)"
      @open-npis="emit('openNpis', $event)"
    />
  </section>
</template>

<style scoped>
.places {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.places-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px 16px;
}

.section-title {
  font-family: var(--v-font-body);
  font-size: 1.125rem;
  font-weight: 600;
}
</style>
