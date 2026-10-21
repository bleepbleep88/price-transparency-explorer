<script setup lang="ts">
import { computed, ref, useTemplateRef, watch } from 'vue'
import { mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { plural } from '@/lib/format'
import PageHeader from '@/components/simple/PageHeader.vue'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import BillTiles from '@/features/rates/components/BillTiles.vue'
import DatasetSnapshot from '@/features/rates/components/DatasetSnapshot.vue'
import DoseCalculator from '@/features/rates/components/DoseCalculator.vue'
import LevelLadder from '@/features/rates/components/LevelLadder.vue'
import NpiDialog from '@/features/rates/components/NpiDialog.vue'
import OtherPrices from '@/features/rates/components/OtherPrices.vue'
import PlacesSection from '@/features/rates/components/PlacesSection.vue'
import RoleStrip from '@/features/rates/components/RoleStrip.vue'
import SearchDetails from '@/features/rates/components/SearchDetails.vue'
import SearchForm from '@/features/rates/components/SearchForm.vue'
import ServiceHeader from '@/features/rates/components/ServiceHeader.vue'
import SourceDrawer from '@/features/rates/components/SourceDrawer.vue'
import StartHere from '@/features/rates/components/StartHere.vue'
import OutOfNetworkPanel from '@/features/out-of-network/components/OutOfNetworkPanel.vue'
import { useOutOfNetwork } from '@/features/out-of-network/composables/useOutOfNetwork'
import { visibleGroups } from '@/features/out-of-network/lib/outOfNetworkText'
import type { RateRow } from '@/features/rates/api/types'
import { useRateSearch, useReferenceLists, useSnapshots } from '@/features/rates/composables/useRateQueries'
import { useSearchRoute } from '@/features/rates/composables/useSearchRoute'
import { currentPlan, leadGroup, tileGroups } from '@/features/rates/lib/answer'
import { marksSearchedProvider } from '@/features/rates/lib/lenses'
import type { SortKey } from '@/features/rates/lib/searchParams'
import { useRateTrend } from '@/features/history/composables/useHistoryQueries'
import { usableSnapshots } from '@/features/history/lib/snapshots'

const { params, hasCode, urlErrors, canSearch, update, replace, startOver } = useSearchRoute()
const search = useRateSearch(params, canSearch)
const outOfNetwork = useOutOfNetwork(params, canSearch)
const snapshots = useSnapshots()
const reference = useReferenceLists()

const currentSnapshot = computed(() => {
  const list = snapshots.data.value ?? []
  return list.find((s) => s.snapshotId === params.value.snapshot) ?? list.find((s) => s.isLatest) ?? null
})

// With more than one month loaded, each tile says how its price moved.
const trend = useRateTrend(params, computed(() => canSearch.value && usableSnapshots(snapshots.data.value ?? []).length > 1))
const trendResult = computed(() => (trend.isError.value ? null : (trend.data.value ?? null)))
const products = computed(() => currentSnapshot.value?.products ?? [])
const settings = computed(() => reference.data.value?.settings ?? [])
const locations = computed(() => reference.data.value?.locations ?? [])

const apiError = computed(() => (search.error.value instanceof ApiError ? search.error.value : null))
const stillLoading = computed(() => apiError.value?.isLoading ?? false)
const fieldErrors = computed(() => ({
  ...urlErrors.value,
  ...(apiError.value?.status === 400 ? apiError.value.fieldErrors : {}),
}))
const result = computed(() => (search.isError.value ? undefined : search.data.value))
const outOfNetworkResult = computed(() => outOfNetwork.data.value ?? null)
const showOutOfNetwork = computed(() => visibleGroups(outOfNetworkResult.value, params.value).length > 0)

// The plan tab, and the bill whose places are listed: the one picked, else the plan's first tile.
const plan = computed(() => (result.value ? currentPlan(result.value, params.value.plan) : null))
const lead = computed(() => (result.value ? leadGroup(result.value, plan.value, params.value.group) : null))
const shownTiles = computed(() => (result.value ? tileGroups(result.value, plan.value, params.value.group).map((group) => group.key) : []))

// The places list follows one bill. A new search starts on the plan's first tile; the URL keeps the pick.
watch(
  () => [result.value, plan.value] as const,
  ([current]) => {
    if (!current || !lead.value || search.isPlaceholderData.value) return
    if (params.value.group !== lead.value.key) void replace({ group: lead.value.key, page: 1 })
  },
)

const announcement = computed(() => (result.value ? `${plural(result.value.groups.length, 'price')} for ${result.value.query.code}.` : ''))

const sourceOpen = ref(false)
const sourceRow = ref<RateRow | null>(null)
const npiOpen = ref(false)
const npiRow = ref<RateRow | null>(null)
const searchForm = useTemplateRef<InstanceType<typeof SearchForm>>('searchForm')

function openSource(row: RateRow) {
  sourceRow.value = row
  sourceOpen.value = true
}

function openNpis(row: RateRow) {
  npiRow.value = row
  npiOpen.value = true
}

function pickPlan(next: string) {
  if (!result.value) return
  const first = tileGroups(result.value, next, null)[0]
  void replace({ plan: next, group: first?.key ?? null, page: 1 })
}

function pickGroup(groupKey: string, productKey?: string) {
  void replace({ group: groupKey, plan: productKey ?? plan.value, page: 1 })
}

function onTableOptions(options: { page: number; pageSize: number; sort: SortKey }) {
  void update({ page: options.page, pageSize: options.pageSize, sort: options.sort })
}
</script>

<template>
  <v-container class="py-8 search-page" max-width="960">
    <div class="stack">
      <PageHeader v-if="!hasCode" title="What does it cost?" subtitle="What the plan pays each place, for one service." />

      <SearchForm
        ref="searchForm"
        :params="params"
        :products="products"
        :settings="settings"
        :locations="locations"
        :external-errors="fieldErrors"
        :busy="search.isFetching.value"
        @submit="update"
        @clear="startOver()"
      />

      <div class="d-sr-only" aria-live="polite">{{ announcement }}</div>

      <template v-if="!hasCode">
        <StartHere />
        <DatasetSnapshot v-if="currentSnapshot" :snapshot="currentSnapshot" />
        <v-skeleton-loader v-else-if="snapshots.isPending.value" type="text" />
        <LoadingPanel v-else />
      </template>

      <template v-else>
        <v-alert v-if="!canSearch" type="warning" variant="tonal">Fix the highlighted fields to search.</v-alert>
        <LoadingPanel v-else-if="stillLoading" />
        <v-alert v-else-if="apiError?.status === 400" type="warning" variant="tonal">{{ apiError.message }}</v-alert>
        <v-alert v-else-if="apiError" type="error" variant="tonal" data-testid="search-error">
          <p>{{ apiError.message }}</p>
          <template #append>
            <v-btn variant="text" :prepend-icon="mdiRefresh" @click="search.refetch()">Try again</v-btn>
          </template>
        </v-alert>

        <template v-else-if="search.isPending.value">
          <v-skeleton-loader type="heading" />
          <div class="tiles-loading">
            <v-skeleton-loader v-for="index in 2" :key="index" type="card" />
          </div>
        </template>

        <template v-else-if="result">
          <v-progress-linear v-if="search.isFetching.value" indeterminate color="primary" />
          <div :style="{ opacity: search.isPlaceholderData.value ? 0.6 : 1 }" class="stack">
            <ServiceHeader :result="result" :params="params" :products="products" :settings="settings" :locations="locations" @update="update" />

            <template v-if="result.groups.length">
              <BillTiles :result="result" :plan="plan" :selected="lead?.key ?? null" :trend="trendResult" @plan="pickPlan" @select="pickGroup" />

              <PlacesSection
                :rows="result.rows"
                :groups="result.groups"
                :group="lead"
                :sort="params.sort"
                :loading="search.isFetching.value"
                :yours="marksSearchedProvider(result)"
                :directory="reference.data.value?.directory"
                @update-options="onTableOptions"
                @open-source="openSource"
                @open-npis="openNpis"
              />

              <!-- Visit levels side by side, and a drug's cost for a dose: each shows only when the search has them. -->
              <LevelLadder v-if="result.code?.levelFamily" :result="result" :params="params" />
              <DoseCalculator v-if="result.code?.doseUnit" :result="result" :dose="params.dose" @dose="(dose) => replace({ dose })" />

              <OutOfNetworkPanel v-if="showOutOfNetwork && outOfNetworkResult" :result="outOfNetworkResult" :params="params" />

              <RoleStrip
                :result="result"
                :params="params"
                :group="lead"
                @update="update"
                @cases="(cases) => replace({ cases })"
                @focus-npi="searchForm?.focusNpi()"
                @search="(code) => startOver(code)"
              />

              <div class="after">
                <OtherPrices :result="result" :shown="shownTiles" @pick="pickGroup" />
                <SearchDetails :result="result" :group="lead" :trend="trendResult" :directory="reference.data.value?.directory" />
              </div>
            </template>

            <v-empty-state v-else-if="!result.hints.length" headline="No prices found" text="Try another service." />
          </div>
        </template>
      </template>
    </div>

    <SourceDrawer
      v-model="sourceOpen"
      :rate-id="sourceRow?.firstRateId ?? null"
      :provider-name="sourceRow?.businessName"
      :provider-key="sourceRow?.providerKey ?? null"
      :whole-tax-id-rate-id="sourceRow?.wholeTaxIdRateId ?? null"
    />
    <NpiDialog v-model="npiOpen" :row="npiRow" />
  </v-container>
</template>

<style scoped>
.stack {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.tiles-loading {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 16px;
}

.after {
  display: flex;
  flex-direction: column;
}
</style>
