<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { money, plural, wholeNumber } from '@/lib/format'
import { settingName } from '@/lib/plainNames'
import { differenceDollars, dollars, gapOf, minus, type PriceSummary } from '@/lib/plainNumbers'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import PageHeader from '@/components/simple/PageHeader.vue'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import { useReferenceLists, useSnapshots } from '@/features/rates/composables/useRateQueries'
import type { GapRow } from '@/features/insights/api/types'
import FacilityChargesSwitch from '@/features/insights/components/FacilityChargesSwitch.vue'
import StatisticsNote from '@/features/insights/components/StatisticsNote.vue'
import { useGapLeaderboard } from '@/features/insights/composables/useInsightQueries'
import { searchLocation } from '@/features/insights/lib/insightLinks'
import { mergeInsightQuery, parseGapQuery, type InsightPatch } from '@/features/insights/lib/insightParams'
import { serviceName, statisticsLine } from '@/features/insights/lib/insightText'
import { cents, FACILITY_CHARGE_REASON, groupLine, rangeText, rowLine, serviceLabel, spreadDollars } from '@/features/insights/lib/plainWords'

const route = useRoute()
const router = useRouter()

const params = computed(() => parseGapQuery(route.query))
const leaderboard = useGapLeaderboard(params)
const snapshots = useSnapshots()
const reference = useReferenceLists()

const result = computed(() => (leaderboard.isError.value ? undefined : leaderboard.data.value))
const apiError = computed(() => (leaderboard.error.value instanceof ApiError ? leaderboard.error.value : null))
const problem = computed(() => (apiError.value ? (Object.values(apiError.value.fieldErrors).flat()[0] ?? apiError.value.message) : null))
const filtered = computed(() => Boolean(params.value.product || params.value.category || params.value.setting || params.value.feeType || params.value.code))
// Until the first statistics build there is nothing to rank; the note says so, and the page claims nothing else.
const saved = computed(() => result.value?.statistics.builtAt != null)
const byRange = computed(() => params.value.sort === 'range')

const snapshot = computed(() => {
  const list = snapshots.data.value ?? []
  return list.find((s) => s.snapshotId === params.value.snapshot) ?? list.find((s) => s.isLatest) ?? null
})
const productItems = computed(() => (snapshot.value?.products ?? []).map((p) => ({ title: p.productLabel, value: p.productKey })))
const settingItems = computed(() => (reference.data.value?.settings ?? []).map((s) => ({ title: settingName(s.key, s.label), value: s.key })))
const categoryItems = computed(() => (result.value?.categories ?? []).map((c) => ({ title: c.label, value: c.key })))
const billItems = [
  { title: 'Place', value: 'facility' },
  { title: 'Doctor', value: 'professional' },
]
const pages = computed(() => (result.value ? Math.ceil(result.value.rows.total / result.value.rows.pageSize) : 0))

// A code arrives from a search's link; it shows as a chip that can be removed, under the name the rows give it.
const codeChip = computed(() => {
  const code = params.value.code
  if (!code) return null
  const row = result.value?.rows.items[0]
  return row ? serviceLabel(row) : code
})

function update(patch: InsightPatch) {
  return router.push({ name: 'gaps', query: mergeInsightQuery(route.query, patch, 'gap') })
}

function clearFilters() {
  void update({ product: null, category: null, setting: null, feeType: null, code: null })
}

const summaryOf = (row: GapRow): PriceSummary => ({ providers: row.providerCount, ...row.stats })

// When whole dollars would show the typical price and the cheapest quarter as the same ($269 and $269), the row shows
// both to the cent so the difference beside them adds up.
function toTheCent(row: GapRow): boolean {
  return row.lowerQuartileGap > 0 && gapOf(summaryOf(row)) === 0
}

function figure(row: GapRow, value: number | null): string {
  if (value == null) return '—'
  return toTheCent(row) ? money(value) : dollars(value)
}

/** Typical minus the cheapest quarter, as the search shows it for the same service: the figures beside it add up. */
function difference(row: GapRow): string {
  const { median, p25 } = row.stats
  const gap = toTheCent(row) && median != null && p25 != null ? minus(cents(median), cents(p25)) : (gapOf(summaryOf(row)) ?? row.lowerQuartileGap)
  return differenceDollars(gap)
}

function most(row: GapRow): string {
  const { p10, p90 } = row.stats
  return p10 == null || p90 == null ? '—' : rangeText(p10, p90, dollars)
}

function spread(row: GapRow): string {
  const { p10, p90 } = row.stats
  return p10 == null || p90 == null ? '—' : spreadDollars(p10, p90)
}
</script>

<template>
  <v-container class="py-10 d-flex flex-column ga-6" max-width="960">
    <PageHeader title="Price gaps" subtitle="Where shopping around saves the most." />
    <StatisticsNote v-if="result" :basis="result.statistics" />

    <div class="d-flex flex-column ga-4">
      <div class="filters">
        <v-select
          :model-value="params.product ?? null"
          :items="productItems"
          label="Plan"
          placeholder="All plans"
          persistent-placeholder
          clearable
          hide-details="auto"
          density="compact"
          :error-messages="apiError?.fieldErrors.product ?? []"
          data-testid="gaps-plan"
          @update:model-value="(value) => update({ product: value })"
        />
        <v-select
          :model-value="params.category ?? null"
          :items="categoryItems"
          label="Kind"
          placeholder="All kinds"
          persistent-placeholder
          clearable
          hide-details="auto"
          density="compact"
          :error-messages="apiError?.fieldErrors.category ?? []"
          data-testid="gaps-category"
          @update:model-value="(value) => update({ category: value })"
        />
        <v-select
          :model-value="params.setting ?? null"
          :items="settingItems"
          label="Place"
          placeholder="All places"
          persistent-placeholder
          clearable
          hide-details="auto"
          density="compact"
          :error-messages="apiError?.fieldErrors.setting ?? []"
          @update:model-value="(value) => update({ setting: value })"
        />
        <v-select
          :model-value="params.feeType ?? null"
          :items="billItems"
          label="Bill"
          placeholder="Both"
          persistent-placeholder
          clearable
          hide-details="auto"
          density="compact"
          :error-messages="apiError?.fieldErrors.feeType ?? []"
          @update:model-value="(value) => update({ feeType: value })"
        />
      </div>

      <div class="d-flex flex-wrap align-center ga-4">
        <v-chip-group
          :model-value="params.sort === 'range' ? 'range' : 'gap'"
          mandatory
          selected-class="chip-on"
          column
          aria-label="Rank by"
          class="py-0"
          @update:model-value="(value) => update({ sort: value })"
        >
          <v-chip value="gap" variant="outlined" :aria-pressed="!byRange" data-testid="sort-gap">Biggest gap</v-chip>
          <v-chip value="range" variant="outlined" :aria-pressed="byRange" data-testid="sort-range">Widest range</v-chip>
        </v-chip-group>
        <v-chip
          v-if="codeChip"
          variant="outlined"
          closable
          :close-label="`Show every service, not only ${codeChip}`"
          data-testid="gaps-code-chip"
          @click:close="update({ code: null })"
        >
          {{ codeChip }}
        </v-chip>
        <FacilityChargesSwitch
          :model-value="params.includeFacilityCharges ?? false"
          :count="result?.facilityCharges ?? 0"
          @update:model-value="(value) => update({ includeFacilityCharges: value ? 'true' : null })"
        />
      </div>
    </div>

    <LoadingPanel v-if="apiError?.isLoading" />
    <v-alert v-else-if="apiError?.status === 400" type="warning" variant="tonal">
      {{ problem }}
      <template #append>
        <v-btn variant="text" :to="{ name: 'gaps' }">Start over</v-btn>
      </template>
    </v-alert>
    <v-alert v-else-if="apiError" type="error" variant="tonal" data-testid="gaps-error">
      {{ apiError.message }}
      <template #append>
        <v-btn variant="text" :prepend-icon="mdiRefresh" @click="leaderboard.refetch()">Try again</v-btn>
      </template>
    </v-alert>
    <v-skeleton-loader v-else-if="leaderboard.isPending.value" type="table" />

    <template v-else-if="result && saved">
      <div v-if="!result.rows.items.length" class="d-flex flex-column align-start ga-2" data-testid="gaps-no-data">
        <template v-if="result.rows.total > 0">
          <p class="text-body-large">Past the last page.</p>
          <v-btn variant="tonal" color="primary" data-testid="gaps-first-page" @click="update({ page: null })">Go to page 1</v-btn>
        </template>
        <template v-else>
          <p class="text-body-large">{{ filtered ? 'No gaps for these filters.' : 'No gaps yet.' }}</p>
          <v-btn v-if="filtered" variant="tonal" color="primary" @click="clearFilters">Clear filters</v-btn>
        </template>
      </div>

      <div v-else class="d-flex flex-column ga-4" :style="{ opacity: leaderboard.isPlaceholderData.value ? 0.6 : 1 }">
        <v-table class="gaps-table" data-testid="gaps-table">
          <thead>
            <tr>
              <th scope="col">Service</th>
              <th scope="col" class="text-end figure-col">Typical</th>
              <th scope="col" class="text-end figure-col">{{ byRange ? 'Most' : 'Cheapest quarter' }}</th>
              <th scope="col" class="text-end">{{ byRange ? 'Spread' : 'Difference' }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in result.rows.items" :key="row.groupKey" data-testid="gap-row">
              <td class="py-2">
                <router-link :to="searchLocation(row.search)" class="service-link" data-testid="gap-search">
                  {{ serviceLabel(row) }}
                </router-link>
                <span class="code"> {{ row.code }}</span>
                <div class="text-body-medium text-medium-emphasis">{{ rowLine(row) }}</div>
                <!-- On a phone the two middle figures sit under the name. -->
                <div class="text-body-medium d-sm-none">
                  Typical {{ figure(row, row.stats.median) }} ·
                  {{ byRange ? `Most ${most(row)}` : `Cheapest quarter ${figure(row, row.stats.p25)}` }}
                </div>
              </td>
              <td class="text-end figure-col" data-testid="gap-typical">{{ figure(row, row.stats.median) }}</td>
              <td class="text-end figure-col">{{ byRange ? most(row) : figure(row, row.stats.p25) }}</td>
              <td class="text-end gap-figure" data-testid="gap-value">{{ byRange ? spread(row) : difference(row) }}</td>
            </tr>
          </tbody>
        </v-table>
        <v-pagination
          v-if="pages > 1"
          :model-value="result.rows.page"
          :length="pages"
          :total-visible="5"
          density="comfortable"
          @update:model-value="(page) => update({ page })"
        />
      </div>

      <DetailsPanel testid="gaps-details">
        <p>{{ statisticsLine(result.statistics) }}</p>
        <p>
          Each row is one service, at one kind of place, under one plan, with 10 or more places paid different amounts.
          "Typical" is the middle price. "Cheapest quarter": a quarter of places are paid this or less. "Difference":
          typical minus that. "Most" leaves out the highest and lowest tenth; "Spread" is how far apart its ends are.
        </p>
        <p>
          {{ plural(result.rows.total, 'service') }} ranked.<template v-if="result.noDifference">
            {{ wholeNumber(result.noDifference) }} more have no gap: the cheapest quarter get the typical price.</template>
        </p>
        <p>The files don't say how often a service happens, so these are per visit or service, never yearly totals.</p>
        <p>{{ FACILITY_CHARGE_REASON }}</p>
        <div v-if="result.rows.items.length">
          <p class="font-weight-medium">The rows on this page, by billing code</p>
          <ul class="detail-list">
            <li v-for="row in result.rows.items" :key="row.groupKey">
              {{ serviceName(row) }} · {{ groupLine(row) }} · {{ row.unit }} · {{ wholeNumber(row.providerCount) }} places
            </li>
          </ul>
        </div>
      </DetailsPanel>
    </template>
  </v-container>
</template>

<style scoped>
.filters {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
}

@media (max-width: 599px) {
  .filters {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .figure-col {
    display: none;
  }
}

.chip-on {
  color: rgb(var(--v-theme-primary));
  border-color: rgb(var(--v-theme-primary));
  background: rgba(var(--v-theme-primary), 0.1);
  font-weight: 600;
}

.gaps-table {
  background: transparent;
}

.gaps-table td,
.gaps-table th {
  font-size: 1rem;
  vertical-align: top;
}

.gaps-table th {
  font-size: 0.875rem;
  font-weight: 600;
}

.service-link {
  color: inherit;
  font-weight: 600;
  text-decoration: none;
}

.service-link:hover {
  text-decoration: underline;
}

.code {
  margin-left: 8px;
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.figure-col {
  white-space: nowrap;
}

.gap-figure {
  white-space: nowrap;
  font-weight: 600;
  color: rgb(var(--v-theme-low));
}

.detail-list {
  padding-left: 16px;
}
</style>
