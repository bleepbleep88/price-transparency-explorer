<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef } from 'vue'
import { mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { plural } from '@/lib/format'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import DatasetSnapshot from '@/features/rates/components/DatasetSnapshot.vue'
import GroupCard from '@/features/rates/components/GroupCard.vue'
import LensPanel from '@/features/rates/components/LensPanel.vue'
import NpiDialog from '@/features/rates/components/NpiDialog.vue'
import ProviderTable from '@/features/rates/components/ProviderTable.vue'
import ResultSummary from '@/features/rates/components/ResultSummary.vue'
import SearchForm from '@/features/rates/components/SearchForm.vue'
import SourceDrawer from '@/features/rates/components/SourceDrawer.vue'
import StartHere from '@/features/rates/components/StartHere.vue'
import type { RateRow } from '@/features/rates/api/types'
import { useRateSearch, useReferenceLists, useSnapshots } from '@/features/rates/composables/useRateQueries'
import { useSearchRoute } from '@/features/rates/composables/useSearchRoute'
import { highlightHeading } from '@/features/rates/lib/lenses'
import type { SortKey } from '@/features/rates/lib/searchParams'

const { params, hasCode, urlErrors, canSearch, update, replace, startOver } = useSearchRoute()
const search = useRateSearch(params, canSearch)
const snapshots = useSnapshots()
const reference = useReferenceLists()

const currentSnapshot = computed(() => {
  const list = snapshots.data.value ?? []
  return list.find((s) => s.snapshotId === params.value.snapshot) ?? list.find((s) => s.isLatest) ?? null
})
const products = computed(() => currentSnapshot.value?.products ?? [])
const settings = computed(() => reference.data.value?.settings ?? [])

const apiError = computed(() => (search.error.value instanceof ApiError ? search.error.value : null))
const stillLoading = computed(() => apiError.value?.isLoading ?? false)
const fieldErrors = computed(() => ({
  ...urlErrors.value,
  ...(apiError.value?.status === 400 ? apiError.value.fieldErrors : {}),
}))
const result = computed(() => (search.isError.value ? undefined : search.data.value))

const announcement = computed(() =>
  result.value
    ? `${plural(result.value.groups.length, 'comparison group')} and ${plural(result.value.rows.total, 'provider row')} for ${result.value.query.code}.`
    : '',
)

const sourceOpen = ref(false)
const sourceRow = ref<RateRow | null>(null)
const npiOpen = ref(false)
const npiRow = ref<RateRow | null>(null)
const tableSection = useTemplateRef<HTMLElement>('tableSection')
const searchForm = useTemplateRef<InstanceType<typeof SearchForm>>('searchForm')

function openSource(row: RateRow) {
  sourceRow.value = row
  sourceOpen.value = true
}

function openNpis(row: RateRow) {
  npiRow.value = row
  npiOpen.value = true
}

async function showGroup(groupKey: string) {
  await update({ group: groupKey })
  await nextTick()
  tableSection.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

function onTableOptions(options: { page: number; pageSize: number; sort: SortKey }) {
  void update({ page: options.page, pageSize: options.pageSize, sort: options.sort })
}
</script>

<template>
  <v-container class="py-6" max-width="1280">
    <h1 class="text-headline-medium mb-1">Search negotiated rates</h1>
    <p class="text-body-medium text-medium-emphasis mb-4">
      What the plan has agreed to pay each provider for a service. Add an NPI or EIN to see where one provider sits.
    </p>

    <SearchForm
      ref="searchForm"
      :params="params"
      :products="products"
      :settings="settings"
      :external-errors="fieldErrors"
      :busy="search.isFetching.value"
      @submit="update"
      @clear="startOver()"
    />

    <div class="d-sr-only" aria-live="polite">{{ announcement }}</div>

    <div v-if="!hasCode" class="mt-6 d-flex flex-column ga-6">
      <StartHere @start="(lens, example) => startOver(example.code, { lens, npi: example.npi ?? null })" />
      <DatasetSnapshot v-if="currentSnapshot" :snapshot="currentSnapshot" />
      <v-skeleton-loader v-else-if="snapshots.isPending.value" type="card" />
      <LoadingPanel v-else />
    </div>

    <div v-else class="mt-4 d-flex flex-column ga-4">
      <v-alert v-if="!canSearch" type="warning" variant="tonal">Fix the highlighted fields to search.</v-alert>
      <LoadingPanel v-else-if="stillLoading" />
      <v-alert v-else-if="apiError?.status === 400" type="warning" variant="tonal">{{ apiError.message }}</v-alert>
      <v-alert v-else-if="apiError" type="error" variant="tonal" data-testid="search-error">
        <p>{{ apiError.message }}</p>
        <p v-if="apiError.traceId" class="text-body-small mt-1">Reference: {{ apiError.traceId }}</p>
        <template #append>
          <v-btn variant="text" :prepend-icon="mdiRefresh" @click="search.refetch()">Try again</v-btn>
        </template>
      </v-alert>

      <template v-else-if="search.isPending.value">
        <v-skeleton-loader type="heading, paragraph" />
        <v-row>
          <v-col v-for="index in 3" :key="index" cols="12" md="6" lg="4"><v-skeleton-loader type="card" /></v-col>
        </v-row>
        <v-skeleton-loader type="table" />
      </template>

      <template v-else-if="result">
        <v-progress-linear v-if="search.isFetching.value" indeterminate color="primary" />
        <div :style="{ opacity: search.isPlaceholderData.value ? 0.6 : 1 }" class="d-flex flex-column ga-4">
          <ResultSummary :result="result" :params="params" :products="products" :settings="settings" @update="update" />

          <!-- No groups, no reading: the hint or the empty state already says why and what to try. -->
          <LensPanel
            v-if="result.groups.length"
            :result="result"
            :params="params"
            @update="update"
            @cases="(cases) => replace({ cases })"
            @focus-npi="searchForm?.focusNpi()"
            @search="(code) => startOver(code)"
          />

          <v-row v-if="result.groups.length" aria-label="Comparison groups">
            <v-col v-for="group in result.groups" :key="group.key" cols="12" md="6" lg="4">
              <GroupCard
                :group="group"
                :selected="params.group === group.key"
                :highlight-heading="highlightHeading(result)"
                @show-providers="showGroup"
              />
            </v-col>
          </v-row>

          <section v-if="result.groups.length" ref="tableSection" aria-labelledby="providers-heading">
            <div class="d-flex flex-wrap align-center ga-2 mb-2">
              <h2 id="providers-heading" class="text-title-large">Providers</h2>
              <span class="text-body-medium text-medium-emphasis">
                {{ plural(result.rows.total, 'row') }}<template v-if="params.group"> in one comparison group</template>
              </span>
              <v-btn v-if="params.group" variant="text" size="small" @click="update({ group: null })">Show all groups</v-btn>
            </div>
            <ProviderTable
              :rows="result.rows"
              :groups="result.groups"
              :sort="params.sort"
              :loading="search.isFetching.value"
              :yours="Boolean(result.query.npi || result.query.ein || result.query.provider)"
              @update-options="onTableOptions"
              @open-source="openSource"
              @open-npis="openNpis"
            />
          </section>

          <v-empty-state
            v-else-if="!result.hints.length"
            headline="No rates match"
            text="Try removing a filter or searching another code."
          />
        </div>
      </template>
    </div>

    <SourceDrawer
      v-model="sourceOpen"
      :rate-id="sourceRow?.firstRateId ?? null"
      :provider-name="sourceRow?.businessName"
      :provider-key="sourceRow?.providerKey ?? null"
    />
    <NpiDialog v-model="npiOpen" :row="npiRow" />
  </v-container>
</template>
