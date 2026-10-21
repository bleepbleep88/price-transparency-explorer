<script setup lang="ts">
import { computed, ref } from 'vue'
import { shortDate } from '@/lib/format'
import { feeLabel } from '@/lib/plainNumbers'
import { billLine, providerName } from '@/lib/plainNames'
import { planName } from '@/features/rates/lib/answer'
import { serviceLabel } from '@/features/insights/lib/plainWords'
import { modifierText } from '@/features/rates/lib/lenses'
import type { HistorySnapshot, RateChange, RateChangesPage } from '../api/types'
import { changeText, priceMove } from '../lib/changesText'

// The biggest changes as a short list (decision 90): name, service, "$old → $new". A row opens its details on click:
// plan, code, tax ID, how many rates, and a link to the provider's rows.
const props = defineProps<{
  rows: RateChangesPage
  from: HistorySnapshot
  to: HistorySnapshot
  loading?: boolean
}>()
const emit = defineEmits<{ page: [page: number] }>()

const items = computed(() => props.rows.items.map((row) => ({ ...row, id: `${row.groupKey}::${row.providerKey}` })))
const pages = computed(() => Math.max(1, Math.ceil(props.rows.total / props.rows.pageSize)))
const open = ref<string | null>(null)

function toggle(id: string) {
  open.value = open.value === id ? null : id
}

/**
 * The service, about 10 words a row with the name and prices (decision 90): "Therapy session, 45 minutes". When another
 * row on the page has the same provider and service, what tells them apart is added: the bill ("Doctor · in the office",
 * "Surgery center") and/or the plan. A row's details always name both.
 */
const serviceLines = computed(() => {
  const rows = props.rows.items
  const keyOf = (row: RateChange) => `${providerName(row.businessName)}|${serviceLabel(row)}`
  return rows.map((row) => {
    const service = serviceLabel(row)
    const twins = rows.filter((other) => keyOf(other) === keyOf(row))
    if (twins.length < 2) return service
    const bills = new Set(twins.map((twin) => billLine(twin)))
    const plans = new Set(twins.map((twin) => twin.productLabel))
    const tell = [bills.size > 1 ? billLine(row) : '', plans.size > 1 ? planName(row.productLabel) : '']
    return [service, ...tell.filter(Boolean)].join(' · ')
  })
})

/** Up in red, down in green (decision 90's colours); new and gone in ink. */
function tone(row: RateChange): string | undefined {
  if (row.kind !== 'changed' || row.change == null) return undefined
  return row.change > 0 ? 'text-high' : 'text-low'
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
</script>

<template>
  <div class="d-flex flex-column ga-2" :style="{ opacity: loading ? 0.6 : 1 }" data-testid="changes-table">
    <p v-if="!items.length" class="text-medium-emphasis" data-testid="changes-none">No changes match.</p>
    <ol v-else class="change-list">
      <li v-for="(item, index) in items" :key="item.id" data-testid="change-row">
        <button
          type="button"
          class="change-row"
          :aria-expanded="open === item.id"
          :aria-controls="`change-${item.id}`"
          @click="toggle(item.id)"
        >
          <span class="who">
            <span class="name">{{ providerName(item.businessName) }}</span>
            <span class="service" data-testid="change-service">{{ serviceLines[index] }}</span>
          </span>
          <span class="move" :class="tone(item)" data-testid="change-text">{{ priceMove(item) }}</span>
        </button>
        <div v-if="open === item.id" :id="`change-${item.id}`" class="change-details" data-testid="change-details">
          <span data-testid="change-bill">{{ billLine(item) }}</span>
          <span>{{ changeText(item) }}</span>
          <span>
            {{ item.productLabel }} · {{ feeLabel(item.feeType) }} · {{ item.settingLabel
            }}<template v-if="item.modifiers.length"> · {{ modifierText(item.modifiers) }}</template>
          </span>
          <span>{{ item.codeType }} {{ item.code }}<template v-if="item.codeName"> · {{ item.codeName }}</template></span>
          <span>{{ item.businessName }} · tax ID {{ item.einDisplay }}<template v-if="item.einMasked"> (not verified)</template></span>
          <span v-if="(item.fromRates ?? 0) > 1 || (item.toRates ?? 0) > 1" data-testid="several-rates">
            Several rates: {{ item.fromRates ?? 0 }} then {{ item.toRates ?? 0 }}. The one shown is its clinicians' typical rate.
          </span>
          <span v-if="item.listedElsewhere" data-testid="listed-elsewhere">
            Also in the {{ otherFile(item) }} file, with other modifiers or setting.
          </span>
          <router-link :to="searchLink(item)" class="search-link" data-testid="change-search">See these rates</router-link>
        </div>
      </li>
    </ol>
    <v-pagination
      v-if="pages > 1"
      :model-value="rows.page"
      :length="pages"
      :total-visible="5"
      density="comfortable"
      rounded
      aria-label="Pages of changes"
      data-testid="changes-pages"
      @update:model-value="(page) => emit('page', page)"
    />
  </div>
</template>

<style scoped>
.change-list {
  list-style: none;
  padding: 0;
}

.change-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 16px;
  width: 100%;
  padding: 8px 0;
  text-align: left;
  color: inherit;
  font: inherit;
  background: none;
  border: 0;
  border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  cursor: pointer;
}

.change-row:hover .name {
  text-decoration: underline;
}

.change-row:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}

.who {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.name {
  font-weight: 500;
  overflow-wrap: anywhere;
}

.service {
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
  overflow-wrap: anywhere;
}

.move {
  font-weight: 600;
  white-space: nowrap;
}

.change-details {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 8px 0 16px;
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
  border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}

.search-link {
  align-self: flex-start;
  font-weight: 600;
  color: rgb(var(--v-theme-primary));
}
</style>
