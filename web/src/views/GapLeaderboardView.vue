<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import { mdiMagnify, mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { money, plural, times, wholeNumber } from '@/lib/format'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import { useReferenceLists, useSnapshots } from '@/features/rates/composables/useRateQueries'
import { groupName } from '@/features/rates/lib/lenses'
import type { GapRow } from '@/features/insights/api/types'
import StatisticsNote from '@/features/insights/components/StatisticsNote.vue'
import { useGapLeaderboard } from '@/features/insights/composables/useInsightQueries'
import { searchLocation } from '@/features/insights/lib/insightLinks'
import { INSIGHT_PAGE_SIZES, mergeInsightQuery, parseGapQuery, type InsightPatch } from '@/features/insights/lib/insightParams'
import { serviceName } from '@/features/insights/lib/insightText'

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
  { title: 'Professional (clinician) fee', value: 'professional' },
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
  { title: 'Comparison group', key: 'group', sortable: false },
  { title: 'Providers', key: 'providerCount', sortable: false, align: 'end' as const },
  { title: 'Median', key: 'median', sortable: false, align: 'end' as const },
  { title: 'Gap to the lower quartile', key: 'gap', sortable: false, align: 'end' as const },
  { title: '90th ÷ 10th', key: 'spread', sortable: false, align: 'end' as const },
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
    <h1 class="text-headline-medium mb-1">Price gaps</h1>
    <p class="text-body-medium text-medium-emphasis mb-1">
      Comparison groups with 10 or more providers, ranked by the gap to the lower quartile: how far the median rate sits
      above the 25th percentile. Plans and employers read it as room to steer care; providers as negotiating context.
    </p>
    <StatisticsNote v-if="result" :basis="result.statistics" class="mb-3" />

    <v-row density="compact" class="mb-1">
      <v-col cols="6" md="3">
        <v-select
          :model-value="params.product ?? null"
          :items="productItems"
          label="Product"
          placeholder="All products"
          persistent-placeholder
          clearable
          @update:model-value="(value) => update({ product: value })"
        />
      </v-col>
      <v-col cols="6" md="3">
        <v-select
          :model-value="params.category ?? null"
          :items="categoryItems"
          label="Category"
          placeholder="All categories"
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
          label="Fee type"
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

    <div class="d-flex flex-wrap align-center ga-3 mb-3">
      <span class="text-body-medium" id="rank-by">Rank by</span>
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
        <v-btn value="gap" :aria-pressed="params.sort === 'gap'" data-testid="sort-gap">Gap to the lower quartile</v-btn>
        <v-btn value="spread" :aria-pressed="params.sort === 'spread'" data-testid="sort-spread">Spread (90th ÷ 10th)</v-btn>
      </v-btn-toggle>
      <v-btn v-if="filtered" variant="text" size="small" @click="clearFilters">Clear filters</v-btn>
    </div>

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
      <p v-for="note in result.notes" :key="note" class="text-body-small text-medium-emphasis mb-2" data-testid="gaps-note">{{ note }}</p>
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
              <div class="text-body-medium">{{ groupName(item) }}</div>
              <div class="text-body-small text-medium-emphasis">{{ item.unit }}</div>
            </div>
          </template>
          <template #[`item.providerCount`]="{ item }">{{ wholeNumber(item.providerCount) }}</template>
          <template #[`item.median`]="{ item }">
            <span class="text-no-wrap">{{ money(item.stats.median) }}{{ perUnit(item) }}</span>
          </template>
          <template #[`item.gap`]="{ item }">
            <div class="text-no-wrap py-1">
              <span class="font-weight-medium" data-testid="gap-value">{{ money(item.lowerQuartileGap) }}{{ perUnit(item) }}</span>
              <div class="text-body-small text-medium-emphasis">25th percentile {{ money(item.stats.p25) }}</div>
            </div>
          </template>
          <template #[`item.spread`]="{ item }">{{ times(item.stats.p90ToP10) }}</template>
          <template #[`item.actions`]="{ item }">
            <v-btn
              :to="searchLocation(item.search)"
              :icon="mdiMagnify"
              variant="text"
              size="small"
              :aria-label="`Open ${item.codeType} ${item.code}, ${groupName(item)}, in search`"
              data-testid="gap-search"
            />
          </template>
          <template #no-data>
            <div class="py-4 d-flex flex-column align-start ga-2">
              <p class="text-body-medium">
                {{ filtered ? 'No comparison group with 10 or more providers matches these filters.' : 'No comparison group has 10 or more providers yet.' }}
              </p>
              <v-btn v-if="filtered" variant="tonal" color="primary" size="small" @click="clearFilters">Clear filters</v-btn>
            </div>
          </template>
        </v-data-table-server>
      </v-card>
      <p class="text-body-small text-medium-emphasis mt-2">{{ plural(result.rows.total, 'comparison group') }} ranked.</p>
    </template>
  </v-container>
</template>
