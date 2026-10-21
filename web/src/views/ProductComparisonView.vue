<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { mdiRefresh, mdiSwapHorizontal } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { plural, wholeNumber } from '@/lib/format'
import { billName, providerName, settingName } from '@/lib/plainNames'
import { dollars, MIN_PROVIDERS_FOR_RANGE, MIN_PROVIDERS_FOR_TYPICAL } from '@/lib/plainNumbers'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import PageHeader from '@/components/simple/PageHeader.vue'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import { useReferenceLists, useSnapshots } from '@/features/rates/composables/useRateQueries'
import type { ComparisonPair, CompareSort } from '@/features/insights/api/types'
import AnswerTile from '@/features/insights/components/AnswerTile.vue'
import FacilityChargesSwitch from '@/features/insights/components/FacilityChargesSwitch.vue'
import IncludeSwitch from '@/features/insights/components/IncludeSwitch.vue'
import InsightSection from '@/features/insights/components/InsightSection.vue'
import StatisticsNote from '@/features/insights/components/StatisticsNote.vue'
import { useProductComparison } from '@/features/insights/composables/useInsightQueries'
import { searchLocation } from '@/features/insights/lib/insightLinks'
import { COMPARE_PAGE_SIZE, mergeInsightQuery, parseCompareQuery, pickProducts, type InsightPatch } from '@/features/insights/lib/insightParams'
import { serviceName, share, statisticsLine } from '@/features/insights/lib/insightText'
import { FACILITY_CHARGE_REASON, groupLine, HEALTH_CENTER_REASON, pairWords, paysWords, planName, serviceLabel, tellApart } from '@/features/insights/lib/plainWords'

const route = useRoute()
const router = useRouter()

const params = computed(() => parseCompareQuery(route.query))
const snapshots = useSnapshots()
const reference = useReferenceLists()

// The plan menus come from the loaded files, so they still work when a stale or mistyped plan made the request fail.
const snapshot = computed(() => {
  const list = snapshots.data.value ?? []
  return list.find((s) => s.snapshotId === params.value.snapshot) ?? list.find((s) => s.isLatest) ?? null
})
// A plan from an old or mistyped link that isn't loaded: say so once, drop it, and compare the plans that are.
const planMissing = ref(false)
const unknownPlan = computed(() => {
  const keys = snapshot.value?.products.map((p) => p.productKey)
  return keys != null && [params.value.product, params.value.versus].some((key) => key != null && !keys.includes(key))
})
watch(
  unknownPlan,
  (unknown) => {
    if (!unknown) return
    planMissing.value = true
    void router.replace({ name: 'compare', query: mergeInsightQuery(route.query, { product: null, versus: null }, 'difference', COMPARE_PAGE_SIZE) })
  },
  { immediate: true },
)
// With a plan in the link, wait for the loaded plans to check it, so a mistyped one never shows an error first.
const comparison = useProductComparison(
  params,
  computed(() => !unknownPlan.value && (!(params.value.product || params.value.versus) || snapshot.value != null)),
)

const result = computed(() => (comparison.isError.value ? undefined : comparison.data.value))
const apiError = computed(() => (comparison.error.value instanceof ApiError ? comparison.error.value : null))
const problem = computed(() => (apiError.value ? (Object.values(apiError.value.fieldErrors).flat()[0] ?? apiError.value.message) : null))
const filtered = computed(() => Boolean(params.value.code || params.value.category || params.value.setting))
// Health centers (federally qualified, rural): paid by visit rules the files don't explain, so left out unless asked for.
const healthCenterPlace = computed(() => params.value.setting === 'fqhc' || params.value.setting === 'rhc')
// Until the first statistics build there is nothing to compare; the note says so, and the page claims nothing else.
const saved = computed(() => result.value?.statistics.builtAt != null)
// Everyday plan names on the main view ("Ambetter"); the pickers and Details keep the full ones.
const product = computed(() => planName(result.value?.product.label ?? 'The first plan'))
const versus = computed(() => planName(result.value?.versus.label ?? 'the second'))
const providers = computed(() => result.value?.summary.providers ?? 0)

const productItems = computed(() => (snapshot.value?.products ?? []).map((p) => ({ title: p.productLabel, value: p.productKey })))
const productKey = computed(() => params.value.product ?? result.value?.product.key ?? null)
const versusKey = computed(() => params.value.versus ?? result.value?.versus.key ?? null)
const settingItems = computed(() => (reference.data.value?.settings ?? []).map((s) => ({ title: settingName(s.key, s.label), value: s.key })))
const categoryItems = computed(() => (result.value?.categories ?? []).map((c) => ({ title: c.label, value: c.key })))
const sorts = computed<{ value: CompareSort; title: string; heading: string }[]>(() => [
  { value: 'difference', title: 'Biggest', heading: 'Biggest differences' },
  { value: 'ratio', title: `Lower in ${product.value}`, heading: `Where ${product.value} pays less` },
  { value: '-ratio', title: `Higher in ${product.value}`, heading: `Where ${product.value} pays more` },
])
const heading = computed(() => sorts.value.find((s) => s.value === params.value.sort)?.heading ?? 'Biggest differences')
const pages = computed(() => (result.value ? Math.ceil(result.value.pairs.total / result.value.pairs.pageSize) : 0))

// A code arrives from a search's link; it shows as a chip that can be removed, under the name the rows give it.
const codeChip = computed(() => {
  const code = params.value.code
  if (!code) return null
  const pair = result.value?.pairs.items[0]
  return pair ? serviceLabel(pair) : code
})

function update(patch: InsightPatch) {
  planMissing.value = false
  return router.push({ name: 'compare', query: mergeInsightQuery(route.query, patch, 'difference', COMPARE_PAGE_SIZE) })
}

function clearFilters() {
  void update({ code: null, category: null, setting: null })
}

function choose(side: 'product' | 'versus', key: string | null) {
  if (!key) return
  const [current, other] = [productKey.value, versusKey.value]
  if (current && other) {
    void update(pickProducts({ product: current, versus: other }, side, key))
    return
  }
  // Without both sides known (a failed request), set this side; the API picks the other when there are two plans.
  const otherSide = side === 'product' ? 'versus' : 'product'
  const otherKey = side === 'product' ? other : current
  void update({ [side]: key, [otherSide]: otherKey === key ? null : otherKey })
}

function swap() {
  if (productKey.value && versusKey.value) void update({ product: versusKey.value, versus: productKey.value })
}

// The one answer: how often the two plans pay the same, from 5 places (as everywhere); under that, each price.
const same = computed(() => {
  const summary = result.value?.summary
  if (!summary?.pairs || summary.providers < MIN_PROVIDERS_FOR_TYPICAL) return null
  return {
    figure: share(summary.identical, summary.pairs),
    line: `of ${plural(summary.pairs, 'price')}`,
    split: `${product.value}: lower ${share(summary.lower, summary.pairs)} · higher ${share(summary.higher, summary.pairs)}`,
  }
})

/** For Details: the middle of the comparisons from 5 places, and where most of them fall from 10. */
const middle = computed(() => {
  const summary = result.value?.summary
  if (!summary || summary.providers < MIN_PROVIDERS_FOR_TYPICAL) return null
  const lines = [`Usually ${product.value} pays ${paysWords(summary.median) ?? '—'}.`]
  if (summary.providers >= MIN_PROVIDERS_FOR_RANGE && summary.p10 != null && summary.p90 != null) {
    const [low, high] = [paysWords(summary.p10), paysWords(summary.p90)]
    lines.push(`Most of the time it pays ${low === high ? low : `${low} to ${high}`}.`)
  }
  return lines.join(' ')
})

// A modifier shows only when it tells a price apart from another of the same place, service and bill on this page.
const apart = computed(() =>
  tellApart(result.value?.pairs.items ?? [], (pair) => [pair.providerKey, pair.code, pair.feeType, pair.settingKey].join('|')),
)

/** "Colonoscopy · Surgery center", "Office visit, level 3 · Doctor · in the office · per unit". */
function pairLine(pair: ComparisonPair): string {
  const { name, where } = billName(pair)
  return [serviceLabel(pair), name, where, apart.value.get(pair), pair.perUnit ? 'per unit' : null].filter(Boolean).join(' · ')
}

function tone(pair: ComparisonPair): string {
  if (pair.same || pair.productValue === pair.versusValue) return ''
  return pair.productValue < pair.versusValue ? 'low' : 'high'
}

/** A share for a kind of service with 5+ places; under that, the plain count ("1 of 1"), so one price isn't "100%". */
function categoryShare(part: number, item: { pairs: number; providers: number }): string {
  return item.providers >= MIN_PROVIDERS_FOR_TYPICAL ? share(part, item.pairs) : `${wholeNumber(part)} of ${wholeNumber(item.pairs)}`
}

const kinds = computed(() => [...(result.value?.byCategory ?? [])].sort((a, b) => b.pairs - a.pairs))
</script>

<template>
  <v-container class="py-10 d-flex flex-column ga-6" max-width="960">
    <PageHeader title="Compare plans" subtitle="Same place, same service, two plans." />
    <p v-if="planMissing" class="text-medium-emphasis" data-testid="compare-plan-missing">That plan isn't loaded. Showing both plans.</p>
    <StatisticsNote v-if="result" :basis="result.statistics" />

    <div class="d-flex flex-column ga-4">
      <div class="plans">
        <v-select
          :model-value="productKey"
          :items="productItems"
          label="Plan"
          hide-details="auto"
          density="compact"
          :error-messages="apiError?.fieldErrors.product ?? []"
          data-testid="compare-product"
          @update:model-value="(value) => choose('product', value)"
        />
        <v-btn
          :icon="mdiSwapHorizontal"
          variant="text"
          aria-label="Swap the two plans"
          :disabled="!productKey || !versusKey"
          data-testid="compare-swap"
          @click="swap"
        />
        <v-select
          :model-value="versusKey"
          :items="productItems"
          label="Against"
          hide-details="auto"
          density="compact"
          :error-messages="apiError?.fieldErrors.versus ?? []"
          data-testid="compare-versus"
          @update:model-value="(value) => choose('versus', value)"
        />
      </div>
      <div class="filters">
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
      </div>
      <div class="d-flex flex-wrap align-center ga-4">
        <v-chip
          v-if="codeChip"
          variant="outlined"
          closable
          :close-label="`Show every service, not only ${codeChip}`"
          data-testid="compare-code-chip"
          @click:close="update({ code: null })"
        >
          {{ codeChip }}
        </v-chip>
        <FacilityChargesSwitch
          :model-value="params.includeFacilityCharges ?? false"
          :count="result?.facilityCharges ?? 0"
          @update:model-value="(value) => update({ includeFacilityCharges: value ? 'true' : null })"
        />
        <!-- Picking a health center as the place already includes them. -->
        <IncludeSwitch
          v-if="!healthCenterPlace"
          :model-value="params.includeHealthCenters ?? false"
          :count="result?.healthCenters ?? 0"
          label="Include health centers"
          testid="health-centers"
          @update:model-value="(value) => update({ includeHealthCenters: value ? 'true' : null })"
        />
      </div>
    </div>

    <LoadingPanel v-if="apiError?.isLoading" />
    <v-alert v-else-if="apiError?.status === 400" type="warning" variant="tonal" data-testid="compare-problem">
      {{ problem }}
      <template #append>
        <v-btn variant="text" :to="{ name: 'compare' }" data-testid="compare-start-over">Start over</v-btn>
      </template>
    </v-alert>
    <v-alert v-else-if="apiError" type="error" variant="tonal">
      {{ apiError.message }}
      <template #append>
        <v-btn variant="text" :prepend-icon="mdiRefresh" @click="comparison.refetch()">Try again</v-btn>
      </template>
    </v-alert>
    <v-skeleton-loader v-else-if="comparison.isPending.value" type="card, table" />

    <template v-else-if="result && saved">
      <div v-if="!result.summary.pairs" class="d-flex flex-column align-start ga-2" data-testid="compare-empty">
        <p class="text-body-large">{{ result.healthCenters ? 'Only health centers have this in both plans.' : 'No place has this in both plans.' }}</p>
        <div class="d-flex flex-wrap ga-2">
          <v-btn
            v-if="result.healthCenters"
            variant="tonal"
            color="primary"
            data-testid="compare-include-health-centers"
            @click="update({ includeHealthCenters: 'true' })"
          >
            Include health centers
          </v-btn>
          <v-btn v-if="filtered" variant="tonal" color="primary" @click="clearFilters">Clear filters</v-btn>
        </div>
      </div>

      <div v-else class="d-flex flex-column ga-10" :style="{ opacity: comparison.isPlaceholderData.value ? 0.6 : 1 }">
        <AnswerTile v-if="same" label="Same price in both plans" :figure="same.figure" :line="same.line" data-testid="compare-same">
          <span class="text-body-medium text-medium-emphasis" data-testid="compare-split">{{ same.split }}</span>
        </AnswerTile>
        <AnswerTile
          v-else
          label="Too few to sum up"
          :figure="`Only ${plural(providers, 'place')}`"
          line="Each price is below."
          data-testid="compare-few"
        />

        <InsightSection :title="same ? heading : null">
          <v-chip-group
            v-if="same"
            :model-value="params.sort"
            mandatory
            selected-class="chip-on"
          column
            aria-label="Order"
            class="py-0"
            @update:model-value="(value: CompareSort) => update({ sort: value })"
          >
            <v-chip
              v-for="sort in sorts"
              :key="sort.value"
              :value="sort.value"
              variant="outlined"
              :aria-pressed="params.sort === sort.value"
              :data-testid="`compare-sort-${sort.value}`"
            >
              {{ sort.title }}
            </v-chip>
          </v-chip-group>

          <v-table class="pairs-table" data-testid="compare-pairs">
            <thead>
              <tr>
                <th scope="col">Place</th>
                <th scope="col" class="text-end plan-col">{{ product }}</th>
                <th scope="col" class="text-end plan-col">{{ versus }}</th>
                <th scope="col" class="text-end">{{ product }} pays</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="pair in result.pairs.items" :key="`${pair.providerKey}|${pair.productSearch.group}`" data-testid="compare-pair">
                <td class="py-2">
                  <div class="font-weight-medium">{{ providerName(pair.businessName) }}</div>
                  <div class="text-body-medium text-medium-emphasis">
                    {{ pairLine(pair) }}
                  </div>
                  <!-- On a phone the two prices sit under the name. -->
                  <div class="text-body-medium d-sm-none">{{ product }} {{ dollars(pair.productValue) }} · {{ versus }} {{ dollars(pair.versusValue) }}</div>
                </td>
                <!-- Each amount opens the search that shows it. -->
                <td class="text-end plan-col">
                  <router-link
                    :to="searchLocation(pair.productSearch)"
                    class="amount"
                    :aria-label="`${dollars(pair.productValue)} in ${product}: see it in search`"
                    data-testid="pair-product-value"
                  >{{ dollars(pair.productValue) }}</router-link>
                </td>
                <td class="text-end plan-col">
                  <router-link
                    :to="searchLocation(pair.versusSearch)"
                    class="amount"
                    :aria-label="`${dollars(pair.versusValue)} in ${versus}: see it in search`"
                    data-testid="pair-versus-value"
                  >{{ dollars(pair.versusValue) }}</router-link>
                </td>
                <td class="text-end pays" :class="tone(pair)" data-testid="pair-ratio">{{ pairWords(pair) }}</td>
              </tr>
            </tbody>
          </v-table>
          <v-pagination
            v-if="pages > 1"
            :model-value="result.pairs.page"
            :length="pages"
            :total-visible="5"
            density="comfortable"
            @update:model-value="(page) => update({ page })"
          />
        </InsightSection>
      </div>

      <DetailsPanel testid="compare-details">
        <p>{{ statisticsLine(result.statistics) }}</p>
        <p>
          Each price is one place's rate for one service at one kind of place, in both plans.
          {{ plural(result.summary.pairs, 'price') }} from {{ plural(result.summary.providers, 'place') }} and
          {{ plural(result.summary.codes, 'billing code') }}.
        </p>
        <p v-if="middle">{{ middle }}</p>
        <v-table v-if="same" density="compact" class="details-table" data-testid="compare-bands">
          <thead>
            <tr>
              <th scope="col">{{ product }} pays</th>
              <th scope="col" class="text-end">Prices</th>
              <th scope="col" class="text-end">Share</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="band in result.distribution" :key="band.key" data-testid="ratio-band">
              <td>{{ band.label }}</td>
              <td class="text-end">{{ wholeNumber(band.count) }}</td>
              <td class="text-end">{{ share(band.count, result.summary.pairs) }}</td>
            </tr>
          </tbody>
        </v-table>
        <v-table v-if="same" density="compact" class="details-table" data-testid="compare-by-category">
          <thead>
            <tr>
              <th scope="col">Kind of service</th>
              <th scope="col" class="text-end">Prices</th>
              <th scope="col" class="text-end">Same</th>
              <th scope="col" class="text-end">Lower</th>
              <th scope="col" class="text-end">Higher</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="kind in kinds" :key="kind.key">
              <td>{{ kind.label }}</td>
              <td class="text-end">{{ wholeNumber(kind.pairs) }}</td>
              <td class="text-end">{{ categoryShare(kind.identical, kind) }}</td>
              <td class="text-end">{{ categoryShare(kind.lower, kind) }}</td>
              <td class="text-end">{{ categoryShare(kind.higher, kind) }}</td>
            </tr>
          </tbody>
        </v-table>
        <p v-for="note in result.notes.filter((note) => !note.includes('facility charges') && !note.includes('health centers'))" :key="note" data-testid="compare-note">{{ note }}</p>
        <p>{{ FACILITY_CHARGE_REASON }}</p>
        <p data-testid="compare-health-reason">{{ HEALTH_CENTER_REASON }}</p>
        <div v-if="result.pairs.items.length">
          <p class="font-weight-medium">The prices on this page, by tax ID and billing code</p>
          <ul class="detail-list">
            <li v-for="pair in result.pairs.items" :key="`${pair.providerKey}|${pair.productSearch.group}`">
              {{ pair.businessName }} · EIN {{ pair.einDisplay }} · {{ serviceName(pair) }} · {{ groupLine(pair) }} · {{ pair.unit }}
            </li>
          </ul>
        </div>
      </DetailsPanel>
    </template>
  </v-container>
</template>

<style scoped>
.plans {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: start;
  gap: 8px;
}

.filters {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

@media (max-width: 599px) {
  .plans {
    grid-template-columns: minmax(0, 1fr);
    justify-items: stretch;
  }

  .plans .v-btn {
    justify-self: center;
  }

  .plan-col {
    display: none;
  }
}

.chip-on {
  color: rgb(var(--v-theme-primary));
  border-color: rgb(var(--v-theme-primary));
  background: rgba(var(--v-theme-primary), 0.1);
  font-weight: 600;
}

.pairs-table,
.details-table {
  background: transparent;
}

.pairs-table td,
.pairs-table th {
  font-size: 1rem;
  vertical-align: top;
}

.pairs-table th {
  font-size: 0.875rem;
  font-weight: 600;
}

.amount {
  color: inherit;
  white-space: nowrap;
  text-decoration: underline;
  text-decoration-color: rgba(var(--v-theme-on-background), 0.3);
  text-underline-offset: 3px;
}

.amount:hover {
  text-decoration: underline;
}

.pays {
  font-weight: 600;
}

@media (min-width: 600px) {
  .pays {
    white-space: nowrap;
  }
}

.pays.low {
  color: rgb(var(--v-theme-low));
}

.pays.high {
  color: rgb(var(--v-theme-high));
}

.detail-list {
  padding-left: 16px;
}
</style>
