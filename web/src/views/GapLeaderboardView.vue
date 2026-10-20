<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import { mdiMagnify, mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { money, plural, wholeNumber } from '@/lib/format'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import { useReferenceLists, useSnapshots } from '@/features/rates/composables/useRateQueries'
import type { GapRow } from '@/features/insights/api/types'
import StatisticsNote from '@/features/insights/components/StatisticsNote.vue'
import { useGapLeaderboard } from '@/features/insights/composables/useInsightQueries'
import { searchLocation } from '@/features/insights/lib/insightLinks'
import { INSIGHT_PAGE_SIZES, mergeInsightQuery, parseGapQuery, type InsightPatch } from '@/features/insights/lib/insightParams'
import { RATES_NOT_COSTS, serviceName } from '@/features/insights/lib/insightText'
import { dollars, groupLine, spreadWords } from '@/features/insights/lib/plainWords'

const route = useRoute()
const router = useRouter()
const { smAndDown } = useDisplay()

const params = computed(() => parseGapQuery(route.query))
const leaderboard = useGapLeaderboard(params)
const snapshots = useSnapshots()
const reference = useReferenceLists()

const result = computed(() => (leaderboard.isError.value ? undefined : leaderboard.data.value))
const apiError = computed(() => (leaderboard.error.value instanceof ApiError ? leaderboard.error.value : null))
const filtered = computed(() => Boolean(params.value.product || params.value.category || params.value.setting || params.value.feeType || params.value.code))

const snapshot = computed(() => {
  const list = snapshots.data.value ?? []
  return list.find((s) => s.snapshotId === params.value.snapshot) ?? list.find((s) => s.isLatest) ?? null
})
const productItems = computed(() => (snapshot.value?.products ?? []).map((p) => ({ title: p.productLabel, value: p.productKey })))
const settingItems = computed(() => (reference.data.value?.settings ?? []).map((s) => ({ title: s.label, value: s.key })))
const categoryItems = computed(() => (result.value?.categories ?? []).map((c) => ({ title: `${c.label} (${wholeNumber(c.count)})`, value: c.key })))
const feeTypes = [
  { title: 'Facility fee', value: 'facility' },
  { title: "Doctor's fee", value: 'professional' },
]

// The code is typed, so it applies on Enter or when the field loses focus, not on every keystroke.
const code = ref('')
watch(() => params.value.code, (value) => (code.value = value ?? ''), { immediate: true })

function update(patch: InsightPatch) {
  return router.push({ name: 'gaps', query: mergeInsightQuery(route.query, patch, 'gap') })
}

function applyCode() {
  if (code.value.trim() !== (params.value.code ?? '')) void update({ code: code.value.trim() || null })
}

function clearFilters() {
  void update({ product: null, category: null, setting: null, feeType: null, code: null })
}

const headers = [
  { title: '#', key: 'rank', sortable: false, width: 48 },
  { title: 'Service', key: 'service', sortable: false },
  { title: 'Setting and plan', key: 'group', sortable: false },
  { title: 'Providers', key: 'providerCount', sortable: false, align: 'end' as const },
  { title: 'Typical price', key: 'typical', sortable: false, align: 'end' as const },
  { title: 'Lower-priced quarter get', key: 'lowerQuarter', sortable: false, align: 'end' as const },
  { title: 'Difference', key: 'difference', sortable: false, align: 'end' as const },
  { title: 'Most providers get', key: 'range', sortable: false },
  { title: 'Search', key: 'actions', sortable: false, align: 'end' as const },
]

function perUnit(row: GapRow): string {
  return row.perUnit ? ' per unit' : ''
}

function onOptions(options: { page: number; itemsPerPage: number }) {
  const rows = result.value?.rows
  if (!rows || (options.page === rows.page && options.itemsPerPage === rows.pageSize)) return
  void update({ page: options.itemsPerPage === rows.pageSize ? options.page : 1, pageSize: options.itemsPerPage })
}
</script>

<template>
  <v-container class="py-6" max-width="1280">
    <h1 class="text-headline-medium mb-1">Where prices differ most</h1>
    <p class="text-body-medium text-medium-emphasis mb-1">
      The same service, in the same setting, under the same plan, where 10 or more providers are paid quite different
      amounts. For plans and employers that's room to choose lower-priced providers; for providers it's context for
      negotiating.
    </p>
    <StatisticsNote v-if="result" :basis="result.statistics" class="mb-3" />

    <v-row density="compact" class="mb-1">
      <v-col cols="6" md="3">
        <v-select
          :model-value="params.product ?? null"
          :items="productItems"
          label="Plan"
          placeholder="All plans"
          persistent-placeholder
          clearable
          @update:model-value="(value) => update({ product: value })"
        />
      </v-col>
      <v-col cols="6" md="3">
        <v-select
          :model-value="params.category ?? null"
          :items="categoryItems"
          label="Kind of service"
          placeholder="All kinds"
          persistent-placeholder
          clearable
          data-testid="gaps-category"
          @update:model-value="(value) => update({ category: value })"
        />
      </v-col>
      <v-col cols="6" md="2">
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
      <v-col cols="6" md="2">
        <v-select
          :model-value="params.feeType ?? null"
          :items="feeTypes"
          label="Fee"
          placeholder="Both"
          persistent-placeholder
          clearable
          @update:model-value="(value) => update({ feeType: value })"
        />
      </v-col>
      <v-col cols="12" md="2">
        <v-text-field
          v-model="code"
          label="Billing code"
          placeholder="Any code"
          persistent-placeholder
          clearable
          autocomplete="off"
          :error-messages="apiError?.fieldErrors.code ?? []"
          data-testid="gaps-code"
          @keydown.enter="applyCode"
          @update:focused="(focused) => !focused && applyCode()"
          @click:clear="update({ code: null })"
        />
      </v-col>
    </v-row>

    <div class="d-flex flex-wrap align-center ga-3 mb-1">
      <span id="rank-by" class="text-body-medium">Rank by</span>
      <v-btn-toggle
        :model-value="params.sort"
        mandatory
        density="comfortable"
        variant="outlined"
        divided
        color="primary"
        role="group"
        aria-labelledby="rank-by"
        @update:model-value="(value) => update({ sort: value })"
      >
        <!-- Vuetify's toggle sets no ARIA state, so each button says whether it's the one in use. -->
        <v-btn value="gap" :aria-pressed="params.sort === 'gap'" data-testid="sort-gap">Biggest dollar difference</v-btn>
        <v-btn value="spread" :aria-pressed="params.sort === 'spread'" data-testid="sort-spread">Widest price range</v-btn>
      </v-btn-toggle>
      <v-btn v-if="filtered" variant="text" size="small" @click="clearFilters">Clear filters</v-btn>
    </div>
    <v-switch
      :model-value="params.includeFacilityCharges ?? false"
      color="primary"
      density="compact"
      hide-details
      inset
      :label="`Include facility charges with no procedure named${result?.facilityCharges ? ` (${wholeNumber(result.facilityCharges)})` : ''}`"
      data-testid="gaps-facility-charges"
      @update:model-value="(value) => update({ includeFacilityCharges: value ? 'true' : null })"
    />
    <p class="text-body-small text-medium-emphasis mb-3">
      Charges filed under a hospital revenue code alone, like "RC 0360 Operating room", cover whatever each hospital
      bundles under them, so they don't compare like for like and are left out unless you include them.
    </p>

    <LoadingPanel v-if="apiError?.isLoading" />
    <v-alert v-else-if="apiError?.status === 400" type="warning" variant="tonal">{{ apiError.message }}</v-alert>
    <v-alert v-else-if="apiError" type="error" variant="tonal" data-testid="gaps-error">
      <p>{{ apiError.message }}</p>
      <template #append>
        <v-btn variant="text" :prepend-icon="mdiRefresh" @click="leaderboard.refetch()">Try again</v-btn>
      </template>
    </v-alert>
    <v-skeleton-loader v-else-if="leaderboard.isPending.value" type="table" />

    <template v-else-if="result">
      <p class="text-body-small text-medium-emphasis mb-2" data-testid="gaps-note">{{ RATES_NOT_COSTS }}</p>
      <v-card :style="{ opacity: leaderboard.isPlaceholderData.value ? 0.6 : 1 }">
        <v-data-table-server
          :headers="headers"
          :items="result.rows.items"
          item-value="groupKey"
          :items-length="result.rows.total"
          :page="result.rows.page"
          :items-per-page="result.rows.pageSize"
          :items-per-page-options="[...INSIGHT_PAGE_SIZES]"
          :loading="leaderboard.isFetching.value"
          :mobile="smAndDown"
          density="compact"
          hover
          :row-props="() => ({ 'data-testid': 'gap-row' })"
          @update:options="onOptions"
        >
          <template #[`item.rank`]="{ item }">
            <span class="text-medium-emphasis">{{ item.rank }}</span>
          </template>
          <template #[`item.service`]="{ item }">
            <div class="py-1">
              <div class="font-weight-medium">{{ serviceName(item) }}</div>
              <div class="text-body-small text-medium-emphasis">{{ item.categoryLabel }}</div>
            </div>
          </template>
          <template #[`item.group`]="{ item }">
            <div class="py-1">
              <div class="text-body-medium">{{ groupLine(item) }}</div>
              <div class="text-body-small text-medium-emphasis">{{ item.unit }}</div>
            </div>
          </template>
          <template #[`item.providerCount`]="{ item }">{{ wholeNumber(item.providerCount) }}</template>
          <template #[`item.typical`]="{ item }">
            <span class="text-no-wrap" data-testid="gap-typical">{{ money(item.stats.median) }}{{ perUnit(item) }}</span>
          </template>
          <template #[`item.lowerQuarter`]="{ item }">
            <span class="text-no-wrap">{{ money(item.stats.p25) }} or less</span>
          </template>
          <template #[`item.difference`]="{ item }">
            <span class="text-no-wrap font-weight-medium" data-testid="gap-value">
              {{ dollars(item.lowerQuartileGap) }} less than typical{{ perUnit(item) }}
            </span>
          </template>
          <template #[`item.range`]="{ item }">
            <div class="py-1">
              <div class="text-no-wrap">{{ money(item.stats.p10) }} to {{ money(item.stats.p90) }}</div>
              <div class="text-body-small text-medium-emphasis">{{ spreadWords(item.stats.p10, item.stats.p90) }}</div>
            </div>
          </template>
          <template #[`item.actions`]="{ item }">
            <v-btn
              :to="searchLocation(item.search)"
              :icon="mdiMagnify"
              variant="text"
              size="small"
              :aria-label="`Open ${item.codeType} ${item.code}, ${groupLine(item)}, in search`"
              data-testid="gap-search"
            />
          </template>
          <template #no-data>
            <div class="py-4 d-flex flex-column align-start ga-2">
              <p class="text-body-medium">
                {{ filtered ? 'Nothing with 10 or more providers matches these filters.' : 'Nothing has 10 or more providers yet.' }}
              </p>
              <v-btn v-if="filtered" variant="tonal" color="primary" size="small" @click="clearFilters">Clear filters</v-btn>
            </div>
          </template>
        </v-data-table-server>
      </v-card>
      <p class="text-body-small text-medium-emphasis mt-2">
        {{ plural(result.rows.total, 'service') }} ranked. "Lower-priced quarter get" is what a quarter of providers are
        paid or less; "Difference" is how much less that is than the typical price. "Most providers get" leaves out the
        highest and lowest tenth.
      </p>
    </template>
  </v-container>
</template>
