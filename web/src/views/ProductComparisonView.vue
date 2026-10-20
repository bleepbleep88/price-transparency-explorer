<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import { mdiRefresh, mdiSwapHorizontal } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { money, plural, wholeNumber } from '@/lib/format'
import { MIN_PROVIDERS_FOR_RANGE, MIN_PROVIDERS_FOR_TYPICAL } from '@/lib/plainNumbers'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import { useReferenceLists, useSnapshots } from '@/features/rates/composables/useRateQueries'
import type { ComparisonPair, CompareSort } from '@/features/insights/api/types'
import FacilityChargesSwitch from '@/features/insights/components/FacilityChargesSwitch.vue'
import StatisticsNote from '@/features/insights/components/StatisticsNote.vue'
import { useProductComparison } from '@/features/insights/composables/useInsightQueries'
import { searchLocation } from '@/features/insights/lib/insightLinks'
import {
  INSIGHT_PAGE_SIZES,
  mergeInsightQuery,
  parseCompareQuery,
  pickProducts,
  sortBy,
  sortParam,
  type InsightPatch,
} from '@/features/insights/lib/insightParams'
import { serviceName, share } from '@/features/insights/lib/insightText'
import { groupLine, pairWords, paysWords, unitSuffix } from '@/features/insights/lib/plainWords'

// ECharts is large: load it only when a comparison has something to draw.
const RatioBandsChart = defineAsyncComponent(() => import('@/features/insights/components/RatioBandsChart.vue'))

const route = useRoute()
const router = useRouter()
const { smAndDown } = useDisplay()

const params = computed(() => parseCompareQuery(route.query))
const comparison = useProductComparison(params)
const snapshots = useSnapshots()
const reference = useReferenceLists()

const result = computed(() => (comparison.isError.value ? undefined : comparison.data.value))
const apiError = computed(() => (comparison.error.value instanceof ApiError ? comparison.error.value : null))
const problem = computed(() => (apiError.value ? (Object.values(apiError.value.fieldErrors).flat()[0] ?? apiError.value.message) : null))
const filtered = computed(() => Boolean(params.value.code || params.value.category || params.value.setting))
// Until the first statistics build there is nothing to compare; the note says so, and the page claims nothing else.
const saved = computed(() => result.value?.statistics.builtAt != null)
const product = computed(() => result.value?.product.label ?? 'The first plan')
const versus = computed(() => result.value?.versus.label ?? 'the second')
const providers = computed(() => result.value?.summary.providers ?? 0)

// The plan menus come from the loaded files, so they still work when a stale or mistyped plan made the request fail.
const snapshot = computed(() => {
  const list = snapshots.data.value ?? []
  return list.find((s) => s.snapshotId === params.value.snapshot) ?? list.find((s) => s.isLatest) ?? null
})
const productItems = computed(() => (snapshot.value?.products ?? []).map((p) => ({ title: p.productLabel, value: p.productKey })))
const productKey = computed(() => params.value.product ?? result.value?.product.key ?? null)
const versusKey = computed(() => params.value.versus ?? result.value?.versus.key ?? null)
const settingItems = computed(() => (reference.data.value?.settings ?? []).map((s) => ({ title: s.label, value: s.key })))
const categoryItems = computed(() => (result.value?.categories ?? []).map((c) => ({ title: `${c.label} (${wholeNumber(c.count)})`, value: c.key })))
const sortItems = computed(() => [
  { title: 'Biggest difference either way', value: 'difference' },
  { title: `Where ${product.value} pays least compared with ${versus.value}`, value: 'ratio' },
  { title: `Where ${product.value} pays most compared with ${versus.value}`, value: '-ratio' },
])

const code = ref('')
watch(() => params.value.code, (value) => (code.value = value ?? ''), { immediate: true })

function update(patch: InsightPatch) {
  return router.push({ name: 'compare', query: mergeInsightQuery(route.query, patch, 'difference') })
}

function applyCode() {
  // Clearing the field sets it to null before it loses focus.
  const typed = (code.value ?? '').trim()
  if (typed !== (params.value.code ?? '')) void update({ code: typed || null })
}

function clearCode() {
  code.value = ''
  void update({ code: null })
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

// The middle 80% needs 10 providers and "usually" 5, as everywhere; with fewer the page lists the prices only.
const summaryCards = computed(() => {
  const summary = result.value?.summary
  if (!summary || summary.providers < MIN_PROVIDERS_FOR_TYPICAL) return []
  const of = (part: number) => `${wholeNumber(part)} of ${wholeNumber(summary.pairs)}`
  const cards = [
    { key: 'same', title: `Pays the same as ${versus.value}`, figure: share(summary.identical, summary.pairs), text: of(summary.identical) },
    { key: 'lower', title: 'Pays less', figure: share(summary.lower, summary.pairs), text: of(summary.lower) },
    { key: 'higher', title: 'Pays more', figure: share(summary.higher, summary.pairs), text: of(summary.higher) },
  ]
  const usually = paysWords(summary.median)
  if (summary.providers >= MIN_PROVIDERS_FOR_RANGE && summary.p10 != null && summary.p90 != null) {
    const [low, high] = [paysWords(summary.p10), paysWords(summary.p90)]
    cards.push({ key: 'middle', title: 'Most of the time it pays', figure: low === high ? (low ?? '—') : `${low} to ${high}`, text: usually ? `usually ${usually}` : '' })
  } else if (usually) {
    cards.push({ key: 'middle', title: 'Usually it pays', figure: usually, text: `from ${plural(summary.providers, 'provider')}` })
  }
  return cards
})

const pairHeaders = computed(() => [
  { title: 'Provider', key: 'provider', sortable: false },
  { title: 'Service', key: 'service', sortable: false },
  { title: 'Fee and setting', key: 'group', sortable: false },
  { title: product.value, key: 'productValue', sortable: false, align: 'end' as const },
  { title: versus.value, key: 'versusValue', sortable: false, align: 'end' as const },
  { title: `${product.value} pays`, key: 'ratio', sortable: false, align: 'end' as const },
])

const categoryHeaders = computed(() => [
  { title: 'Kind of service', key: 'label', sortable: true },
  { title: 'Compared', key: 'pairs', sortable: true, align: 'end' as const },
  { title: 'Same price', key: 'identical', sortable: true, align: 'end' as const },
  { title: `${product.value} pays less`, key: 'lower', sortable: true, align: 'end' as const },
  { title: `${product.value} pays more`, key: 'higher', sortable: true, align: 'end' as const },
  { title: 'Usually it pays', key: 'median', sortable: true, align: 'end' as const },
])

/** A share for a kind of service with 5+ providers; under that, the plain count ("1 of 1"), so one price isn't "100%". */
function categoryShare(part: number, item: { pairs: number; providers: number }): string {
  return item.providers >= MIN_PROVIDERS_FOR_TYPICAL ? share(part, item.pairs) : `${wholeNumber(part)} of ${wholeNumber(item.pairs)}`
}

function onOptions(options: { page: number; itemsPerPage: number }) {
  const pairs = result.value?.pairs
  if (!pairs || (options.page === pairs.page && options.itemsPerPage === pairs.pageSize)) return
  void update({ page: options.itemsPerPage === pairs.pageSize ? options.page : 1, pageSize: options.itemsPerPage })
}

/** The kinds table's order lives in the address, replacing the entry so Back leaves the page. */
function sortCategories(value: readonly { key: string; order?: boolean | 'asc' | 'desc' }[]) {
  void router.replace({ name: 'compare', query: mergeInsightQuery(route.query, { categorySort: sortParam(value), page: params.value.page }, 'difference') })
}
</script>

<template>
  <v-container class="py-6" max-width="1280">
    <h1 class="text-headline-medium mb-1">Plan comparison</h1>
    <p class="text-body-medium text-medium-emphasis mb-1">
      The same provider, the same service and the same setting in two plans: what one plan pays compared with the other.
    </p>
    <StatisticsNote v-if="result" :basis="result.statistics" class="mb-3" />

    <v-row density="compact">
      <v-col cols="12" sm="5" md="4">
        <v-select
          :model-value="productKey"
          :items="productItems"
          label="Compare"
          :error-messages="apiError?.fieldErrors.product ?? []"
          data-testid="compare-product"
          @update:model-value="(value) => choose('product', value)"
        />
      </v-col>
      <v-col cols="12" sm="2" md="1" class="d-flex justify-center align-start">
        <v-btn
          :icon="mdiSwapHorizontal"
          variant="text"
          aria-label="Swap the two plans"
          :disabled="!productKey || !versusKey"
          data-testid="compare-swap"
          @click="swap"
        />
      </v-col>
      <v-col cols="12" sm="5" md="4">
        <v-select
          :model-value="versusKey"
          :items="productItems"
          label="With"
          :error-messages="apiError?.fieldErrors.versus ?? []"
          data-testid="compare-versus"
          @update:model-value="(value) => choose('versus', value)"
        />
      </v-col>
    </v-row>
    <v-row density="compact" class="mb-1">
      <v-col cols="12" sm="4" md="3">
        <v-text-field
          v-model="code"
          label="Billing code"
          placeholder="Any code"
          persistent-placeholder
          clearable
          autocomplete="off"
          :error-messages="apiError?.fieldErrors.code ?? []"
          data-testid="compare-code"
          @keydown.enter="applyCode"
          @update:focused="(focused) => !focused && applyCode()"
          @click:clear="clearCode"
        />
      </v-col>
      <v-col cols="12" sm="4" md="3">
        <v-select
          :model-value="params.category ?? null"
          :items="categoryItems"
          label="Kind of service"
          placeholder="All kinds"
          persistent-placeholder
          clearable
          :error-messages="apiError?.fieldErrors.category ?? []"
          @update:model-value="(value) => update({ category: value })"
        />
      </v-col>
      <v-col cols="12" sm="4" md="3">
        <v-select
          :model-value="params.setting ?? null"
          :items="settingItems"
          label="Setting"
          placeholder="All settings"
          persistent-placeholder
          clearable
          :error-messages="apiError?.fieldErrors.setting ?? []"
          @update:model-value="(value) => update({ setting: value })"
        />
      </v-col>
    </v-row>
    <v-btn v-if="filtered" variant="text" size="small" class="mb-2" @click="clearFilters">Clear filters</v-btn>
    <FacilityChargesSwitch
      class="mb-3"
      :model-value="params.includeFacilityCharges ?? false"
      :count="result?.facilityCharges ?? 0"
      @update:model-value="(value) => update({ includeFacilityCharges: value ? 'true' : null })"
    />

    <LoadingPanel v-if="apiError?.isLoading" />
    <v-alert v-else-if="apiError?.status === 400" type="warning" variant="tonal" data-testid="compare-problem">
      {{ problem }}
      <template #append>
        <v-btn variant="text" :to="{ name: 'compare' }" data-testid="compare-start-over">Start over</v-btn>
      </template>
    </v-alert>
    <v-alert v-else-if="apiError" type="error" variant="tonal">
      <p>{{ apiError.message }}</p>
      <template #append>
        <v-btn variant="text" :prepend-icon="mdiRefresh" @click="comparison.refetch()">Try again</v-btn>
      </template>
    </v-alert>
    <v-skeleton-loader v-else-if="comparison.isPending.value" type="card, table" />

    <div v-else-if="result && saved" class="d-flex flex-column ga-4" :style="{ opacity: comparison.isPlaceholderData.value ? 0.6 : 1 }">
      <v-alert v-if="!result.summary.pairs" type="info" variant="tonal" data-testid="compare-empty">
        No provider has this service in both plans{{ filtered ? ' under these filters' : '' }}.
        <template v-if="filtered" #append>
          <v-btn variant="text" @click="clearFilters">Clear filters</v-btn>
        </template>
      </v-alert>

      <template v-else>
        <template v-if="summaryCards.length">
          <p class="text-body-medium">
            What {{ product }} pays compared with {{ versus }}, for the same provider, service and setting:
          </p>
          <v-row>
            <v-col v-for="card in summaryCards" :key="card.key" cols="6" md="3">
              <v-card class="h-100" :data-testid="`compare-${card.key}`">
                <v-card-item>
                  <v-card-subtitle class="text-wrap">{{ card.title }}</v-card-subtitle>
                </v-card-item>
                <v-card-text>
                  <p class="stat-figure text-headline-small">{{ card.figure }}</p>
                  <p class="text-body-medium text-medium-emphasis">{{ card.text }}</p>
                </v-card-text>
              </v-card>
            </v-col>
          </v-row>
        </template>
        <p class="text-body-small text-medium-emphasis" data-testid="compare-counts">
          {{ plural(result.summary.pairs, 'price') }} compared, from {{ plural(result.summary.providers, 'provider') }} and
          {{ plural(result.summary.codes, 'billing code') }}.<template v-if="providers < MIN_PROVIDERS_FOR_TYPICAL">
            That's too few providers to sum up, so each price is listed below.</template>
        </p>

        <template v-if="providers >= MIN_PROVIDERS_FOR_TYPICAL">
          <v-row>
            <v-col cols="12" md="7">
              <v-card class="h-100">
                <v-card-item>
                  <v-card-title tag="h2">How the prices compare</v-card-title>
                  <v-card-subtitle class="text-wrap">What {{ product }} pays compared with {{ versus }}, one price at a time</v-card-subtitle>
                </v-card-item>
                <v-card-text>
                  <RatioBandsChart :bands="result.distribution" />
                </v-card-text>
              </v-card>
            </v-col>
            <v-col cols="12" md="5">
              <v-card class="h-100">
                <v-card-item>
                  <v-card-title tag="h2">In numbers</v-card-title>
                </v-card-item>
                <v-table density="compact">
                  <thead>
                    <tr>
                      <th scope="col">{{ product }} pays</th>
                      <th scope="col" class="text-end">How many</th>
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
              </v-card>
            </v-col>
          </v-row>

          <v-card>
            <v-card-item>
              <v-card-title tag="h2">By kind of service</v-card-title>
            </v-card-item>
            <v-data-table
              :headers="categoryHeaders"
              :items="result.byCategory"
              item-value="key"
              :items-per-page="-1"
              :sort-by="sortBy(params.categorySort)"
              hide-default-footer
              :mobile="smAndDown"
              density="compact"
              data-testid="compare-by-category"
              @update:sort-by="sortCategories"
            >
              <template #[`item.pairs`]="{ item }">{{ wholeNumber(item.pairs) }}</template>
              <template #[`item.identical`]="{ item }">{{ categoryShare(item.identical, item) }}</template>
              <template #[`item.lower`]="{ item }">{{ categoryShare(item.lower, item) }}</template>
              <template #[`item.higher`]="{ item }">{{ categoryShare(item.higher, item) }}</template>
              <template #[`item.median`]="{ item }">
                {{ item.median == null ? 'Too few providers to say' : paysWords(item.median) }}
              </template>
            </v-data-table>
          </v-card>
        </template>

        <v-card>
          <v-card-item>
            <v-card-title tag="h2">Price by price</v-card-title>
          </v-card-item>
          <v-card-text class="pb-0">
            <v-select
              :model-value="params.sort"
              :items="sortItems"
              label="Order"
              style="max-width: 420px"
              data-testid="compare-sort"
              @update:model-value="(value: CompareSort) => update({ sort: value })"
            />
          </v-card-text>
          <v-data-table-server
            :headers="pairHeaders"
            :items="result.pairs.items"
            :item-value="(pair: ComparisonPair) => `${pair.providerKey}|${pair.productSearch.group}`"
            :items-length="result.pairs.total"
            :page="result.pairs.page"
            :items-per-page="result.pairs.pageSize"
            :items-per-page-options="[...INSIGHT_PAGE_SIZES]"
            :loading="comparison.isFetching.value"
            :mobile="smAndDown"
            density="compact"
            hover
            :row-props="() => ({ 'data-testid': 'compare-pair' })"
            @update:options="onOptions"
          >
            <template #[`item.provider`]="{ item }">
              <div class="py-1">
                <div class="font-weight-medium">{{ item.businessName }}</div>
                <div class="text-body-small text-medium-emphasis">EIN {{ item.einDisplay }}</div>
              </div>
            </template>
            <template #[`item.service`]="{ item }">
              <div class="py-1">
                <div>{{ serviceName(item) }}</div>
                <div class="text-body-small text-medium-emphasis">{{ item.categoryLabel }}</div>
              </div>
            </template>
            <template #[`item.group`]="{ item }">
              <span class="text-body-medium">{{ groupLine(item) }}</span>
            </template>
            <!-- Each amount opens the search that shows it. -->
            <template #[`item.productValue`]="{ item }">
              <v-btn
                :to="searchLocation(item.productSearch)"
                variant="text"
                size="small"
                class="px-1 text-none"
                :aria-label="`${money(item.productValue)}: search ${item.codeType} ${item.code} for ${item.businessName} in ${product}`"
                data-testid="pair-product-value"
              >
                {{ money(item.productValue) }}{{ unitSuffix(item) }}
              </v-btn>
            </template>
            <template #[`item.versusValue`]="{ item }">
              <v-btn
                :to="searchLocation(item.versusSearch)"
                variant="text"
                size="small"
                class="px-1 text-none"
                :aria-label="`${money(item.versusValue)}: search ${item.codeType} ${item.code} for ${item.businessName} in ${versus}`"
                data-testid="pair-versus-value"
              >
                {{ money(item.versusValue) }}{{ unitSuffix(item) }}
              </v-btn>
            </template>
            <template #[`item.ratio`]="{ item }">
              <span class="text-no-wrap font-weight-medium" data-testid="pair-ratio">{{ pairWords(item) }}</span>
            </template>
          </v-data-table-server>
        </v-card>
      </template>

      <p v-for="note in result.notes.filter((note) => !note.includes('facility charges'))" :key="note" class="text-body-small text-medium-emphasis" data-testid="compare-note">
        {{ note }}
      </p>
    </div>
  </v-container>
</template>
