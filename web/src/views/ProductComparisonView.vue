<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import { mdiRefresh, mdiSwapHorizontal } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { money, plural, times, wholeNumber } from '@/lib/format'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import { useReferenceLists } from '@/features/rates/composables/useRateQueries'
import { modifierText } from '@/features/rates/lib/lenses'
import type { ComparisonPair, CompareSort } from '@/features/insights/api/types'
import StatisticsNote from '@/features/insights/components/StatisticsNote.vue'
import { useProductComparison } from '@/features/insights/composables/useInsightQueries'
import { searchLocation } from '@/features/insights/lib/insightLinks'
import {
  INSIGHT_PAGE_SIZES,
  mergeInsightQuery,
  parseCompareQuery,
  pickProducts,
  type InsightPatch,
} from '@/features/insights/lib/insightParams'
import { percentDifference, serviceName, share } from '@/features/insights/lib/insightText'

// ECharts is large: load it only when a comparison has something to draw.
const RatioBandsChart = defineAsyncComponent(() => import('@/features/insights/components/RatioBandsChart.vue'))

const route = useRoute()
const router = useRouter()
const { smAndDown } = useDisplay()

const params = computed(() => parseCompareQuery(route.query))
const comparison = useProductComparison(params)
const reference = useReferenceLists()

const result = computed(() => (comparison.isError.value ? undefined : comparison.data.value))
const apiError = computed(() => (comparison.error.value instanceof ApiError ? comparison.error.value : null))
const filtered = computed(() => Boolean(params.value.code || params.value.category || params.value.setting))
const product = computed(() => result.value?.product.label ?? 'The first product')
const versus = computed(() => result.value?.versus.label ?? 'the second')

const productItems = computed(() => (result.value?.products ?? []).map((p) => ({ title: p.label, value: p.key })))
const settingItems = computed(() => (reference.data.value?.settings ?? []).map((s) => ({ title: s.label, value: s.key })))
const categoryItems = computed(() => (result.value?.categories ?? []).map((c) => ({ title: `${c.label} (${wholeNumber(c.count)})`, value: c.key })))
const sortItems = computed(() => [
  { title: 'Biggest difference either way', value: 'difference' },
  { title: `${product.value} lowest against ${versus.value}`, value: 'ratio' },
  { title: `${product.value} highest against ${versus.value}`, value: '-ratio' },
])

const code = ref('')
watch(() => params.value.code, (value) => (code.value = value ?? ''), { immediate: true })

function update(patch: InsightPatch) {
  return router.push({ name: 'compare', query: mergeInsightQuery(route.query, patch, 'difference') })
}

function applyCode() {
  if (code.value.trim() !== (params.value.code ?? '')) void update({ code: code.value.trim() || null })
}

function clearFilters() {
  void update({ code: null, category: null, setting: null })
}

function choose(side: 'product' | 'versus', key: string | null) {
  const current = result.value
  if (current && key) void update(pickProducts({ product: current.product.key, versus: current.versus.key }, side, key))
}

function swap() {
  const current = result.value
  if (current) void update({ product: current.versus.key, versus: current.product.key })
}

const summaryCards = computed(() => {
  const summary = result.value?.summary
  if (!summary) return []
  return [
    { key: 'same', title: 'Same rate', figure: share(summary.identical, summary.pairs), text: `${wholeNumber(summary.identical)} of ${plural(summary.pairs, 'pair')}` },
    { key: 'lower', title: `${product.value} pays less`, figure: share(summary.lower, summary.pairs), text: plural(summary.lower, 'pair') },
    { key: 'higher', title: `${product.value} pays more`, figure: share(summary.higher, summary.pairs), text: plural(summary.higher, 'pair') },
    {
      key: 'middle',
      title: 'Middle 80% of ratios',
      figure: summary.p10 == null ? '—' : `${times(summary.p10)} to ${times(summary.p90)}`,
      text: summary.median == null ? 'No pairs' : `median ${times(summary.median)}`,
    },
  ]
})

const pairHeaders = computed(() => [
  { title: 'Provider', key: 'provider', sortable: false },
  { title: 'Service', key: 'service', sortable: false },
  { title: 'Fee type and setting', key: 'group', sortable: false },
  { title: product.value, key: 'productValue', sortable: false, align: 'end' as const },
  { title: versus.value, key: 'versusValue', sortable: false, align: 'end' as const },
  { title: 'Ratio', key: 'ratio', sortable: false, align: 'end' as const },
])

const categoryHeaders = [
  { title: 'Category', key: 'label', sortable: true },
  { title: 'Pairs', key: 'pairs', sortable: true, align: 'end' as const },
  { title: 'Same rate', key: 'identical', sortable: true, align: 'end' as const },
  { title: 'Lower', key: 'lower', sortable: true, align: 'end' as const },
  { title: 'Higher', key: 'higher', sortable: true, align: 'end' as const },
  { title: 'Median ratio', key: 'median', sortable: true, align: 'end' as const },
]

function groupText(pair: ComparisonPair): string {
  const modifiers = pair.modifiers.length ? ` · ${modifierText(pair.modifiers)}` : ''
  return `${pair.feeTypeLabel} · ${pair.settingLabel}${modifiers}`
}

function perUnit(pair: ComparisonPair): string {
  return pair.perUnit ? ' per unit' : ''
}

function onOptions(options: { page: number; itemsPerPage: number }) {
  const pairs = result.value?.pairs
  if (!pairs || (options.page === pairs.page && options.itemsPerPage === pairs.pageSize)) return
  void update({ page: options.itemsPerPage === pairs.pageSize ? options.page : 1, pageSize: options.itemsPerPage })
}
</script>

<template>
  <v-container class="py-6" max-width="1280">
    <h1 class="text-headline-medium mb-1">Plan comparison</h1>
    <p class="text-body-medium text-medium-emphasis mb-1">
      The same provider, service, setting and modifiers in two products: what one product pays against the other.
    </p>
    <StatisticsNote v-if="result" :basis="result.statistics" class="mb-3" />

    <v-row density="compact">
      <v-col cols="12" sm="5" md="4">
        <v-select
          :model-value="result?.product.key ?? null"
          :items="productItems"
          label="Compare"
          data-testid="compare-product"
          @update:model-value="(value) => choose('product', value)"
        />
      </v-col>
      <v-col cols="12" sm="2" md="1" class="d-flex justify-center align-start">
        <v-btn :icon="mdiSwapHorizontal" variant="text" aria-label="Swap the two products" :disabled="!result" data-testid="compare-swap" @click="swap" />
      </v-col>
      <v-col cols="12" sm="5" md="4">
        <v-select
          :model-value="result?.versus.key ?? null"
          :items="productItems"
          label="With"
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
          @click:clear="update({ code: null })"
        />
      </v-col>
      <v-col cols="12" sm="4" md="3">
        <v-select
          :model-value="params.category ?? null"
          :items="categoryItems"
          label="Category"
          placeholder="All categories"
          persistent-placeholder
          clearable
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
          @update:model-value="(value) => update({ setting: value })"
        />
      </v-col>
    </v-row>
    <v-btn v-if="filtered" variant="text" size="small" class="mb-2" @click="clearFilters">Clear filters</v-btn>

    <LoadingPanel v-if="apiError?.isLoading" />
    <v-alert v-else-if="apiError?.status === 400" type="warning" variant="tonal" data-testid="compare-problem">{{ apiError.message }}</v-alert>
    <v-alert v-else-if="apiError" type="error" variant="tonal">
      <p>{{ apiError.message }}</p>
      <template #append>
        <v-btn variant="text" :prepend-icon="mdiRefresh" @click="comparison.refetch()">Try again</v-btn>
      </template>
    </v-alert>
    <v-skeleton-loader v-else-if="comparison.isPending.value" type="card, table" />

    <div v-else-if="result" class="d-flex flex-column ga-4" :style="{ opacity: comparison.isPlaceholderData.value ? 0.6 : 1 }">
      <v-alert v-if="!result.summary.pairs" type="info" variant="tonal" data-testid="compare-empty">
        No provider has this service in both products{{ filtered ? ' under these filters' : '' }}.
        <template v-if="filtered" #append>
          <v-btn variant="text" @click="clearFilters">Clear filters</v-btn>
        </template>
      </v-alert>

      <template v-else>
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
        <p class="text-body-small text-medium-emphasis">
          {{ plural(result.summary.pairs, 'pair') }}: {{ plural(result.summary.providers, 'provider') }}, {{ plural(result.summary.codes, 'code') }}.
          A ratio is {{ product }}'s rate ÷ {{ versus }}'s for the same provider and service.
        </p>

        <v-row>
          <v-col cols="12" md="7">
            <v-card class="h-100">
              <v-card-item>
                <v-card-title>How the ratios spread</v-card-title>
                <v-card-subtitle class="text-wrap">{{ product }} against {{ versus }}, pair by pair</v-card-subtitle>
              </v-card-item>
              <v-card-text>
                <RatioBandsChart :bands="result.distribution" />
              </v-card-text>
            </v-card>
          </v-col>
          <v-col cols="12" md="5">
            <v-card class="h-100">
              <v-card-item>
                <v-card-title>In numbers</v-card-title>
              </v-card-item>
              <v-table density="compact" aria-label="Pairs by how far apart the two rates are">
                <thead>
                  <tr>
                    <th scope="col">{{ product }}'s rate</th>
                    <th scope="col" class="text-end">Pairs</th>
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
            <v-card-title>By category</v-card-title>
          </v-card-item>
          <v-data-table
            :headers="categoryHeaders"
            :items="result.byCategory"
            item-value="key"
            :items-per-page="-1"
            hide-default-footer
            :mobile="smAndDown"
            density="compact"
            aria-label="Pairs by category of service"
          >
            <template #[`item.pairs`]="{ item }">{{ wholeNumber(item.pairs) }}</template>
            <template #[`item.identical`]="{ item }">{{ share(item.identical, item.pairs) }}</template>
            <template #[`item.lower`]="{ item }">{{ share(item.lower, item.pairs) }}</template>
            <template #[`item.higher`]="{ item }">{{ share(item.higher, item.pairs) }}</template>
            <template #[`item.median`]="{ item }">{{ times(item.median) }}</template>
          </v-data-table>
        </v-card>

        <v-card>
          <v-card-item>
            <v-card-title>Pair by pair</v-card-title>
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
            aria-label="Pairs of rates"
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
              <span class="text-body-medium">{{ groupText(item) }}</span>
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
                {{ money(item.productValue) }}{{ perUnit(item) }}
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
                {{ money(item.versusValue) }}{{ perUnit(item) }}
              </v-btn>
            </template>
            <template #[`item.ratio`]="{ item }">
              <div class="text-no-wrap py-1">
                <span class="font-weight-medium" data-testid="pair-ratio">{{ times(item.ratio) }}</span>
                <div class="text-body-small text-medium-emphasis">{{ percentDifference(item.ratio) }}</div>
              </div>
            </template>
          </v-data-table-server>
        </v-card>
      </template>

      <p v-for="note in result.notes" :key="note" class="text-body-small text-medium-emphasis" data-testid="compare-note">{{ note }}</p>
    </div>
  </v-container>
</template>
