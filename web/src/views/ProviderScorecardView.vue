<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { useDisplay } from 'vuetify'
import { mdiArrowLeft, mdiInformationOutline, mdiMagnify, mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { money, ordinal, plural, times, wholeNumber } from '@/lib/format'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import { groupName } from '@/features/rates/lib/lenses'
import ScorecardExtremes from '@/features/insights/components/ScorecardExtremes.vue'
import StatisticsNote from '@/features/insights/components/StatisticsNote.vue'
import { insightKeys, useProviderScorecard } from '@/features/insights/composables/useInsightQueries'
import { searchLocation } from '@/features/insights/lib/insightLinks'
import { indexExplainer, indexSentence, medianCounts, serviceName } from '@/features/insights/lib/insightText'

const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()
const { smAndDown } = useDisplay()

const key = computed(() => String(route.params.key ?? ''))
const snapshot = computed(() => {
  const value = Number(Array.isArray(route.query.snapshot) ? route.query.snapshot[0] : route.query.snapshot)
  return Number.isInteger(value) && value > 0 ? value : undefined
})
const scorecard = useProviderScorecard(key, snapshot)
const result = computed(() => (scorecard.isError.value ? undefined : scorecard.data.value))
const apiError = computed(() => (scorecard.error.value instanceof ApiError ? scorecard.error.value : null))
const provider = computed(() => result.value?.provider)

// An EIN typed into the address, or a masked tax ID's full number, gives way to the provider's key, so the address
// never keeps a masked number. The answer is already here, so the key's page opens without asking again.
watch(result, (value) => {
  if (!value) return
  document.title = `${value.provider.name} · Provider scorecard · Price Transparency Explorer`
  if (value.provider.providerKey !== key.value) {
    queryClient.setQueryData(insightKeys.scorecard(value.provider.providerKey, snapshot.value), value)
    void router.replace({ name: 'scorecard', params: { key: value.provider.providerKey }, query: route.query })
  }
})

const shownNames = 3
const filter = ref('')
const headers = [
  { title: 'Service', key: 'code', sortable: true },
  { title: 'Comparison group', key: 'groupKey', sortable: false },
  { title: 'Providers', key: 'providerCount', sortable: true, align: 'end' as const, nowrap: true },
  { title: 'Rate', key: 'value', sortable: true, align: 'end' as const, nowrap: true },
  { title: 'Percentile', key: 'percentile', sortable: true, align: 'end' as const, nowrap: true },
  { title: '× median', key: 'vsMedian', sortable: true, align: 'end' as const, nowrap: true },
  { title: 'Position', key: 'band', sortable: false },
  { title: 'Search', key: 'actions', sortable: false, align: 'end' as const },
]
const filterKeys = ['code', 'codeName', 'categoryLabel', 'productLabel', 'settingLabel', 'feeTypeLabel']
const bandView: Record<string, { label: string; color?: string }> = {
  low: { label: 'Low', color: 'low' },
  typical: { label: 'Typical' },
  high: { label: 'High', color: 'high' },
}
</script>

<template>
  <v-container class="py-6" max-width="1280">
    <v-btn :to="{ name: 'providers' }" variant="text" size="small" :prepend-icon="mdiArrowLeft" class="px-1 mb-2">
      Provider scorecards
    </v-btn>

    <LoadingPanel v-if="apiError?.isLoading" />
    <v-alert v-else-if="apiError?.status === 404" type="info" variant="tonal" data-testid="scorecard-not-found">
      {{ apiError.message }}
      <template #append>
        <v-btn :to="{ name: 'providers' }" variant="text">Find a provider</v-btn>
      </template>
    </v-alert>
    <v-alert v-else-if="apiError?.status === 400" type="warning" variant="tonal">
      {{ apiError.fieldErrors.provider?.[0] ?? apiError.message }}
    </v-alert>
    <v-alert v-else-if="apiError" type="error" variant="tonal">
      <p>{{ apiError.message }}</p>
      <template #append>
        <v-btn variant="text" :prepend-icon="mdiRefresh" @click="scorecard.refetch()">Try again</v-btn>
      </template>
    </v-alert>
    <template v-else-if="scorecard.isPending.value">
      <v-skeleton-loader type="heading, paragraph" />
      <v-skeleton-loader type="table" class="mt-4" />
    </template>

    <div v-else-if="result && provider" class="d-flex flex-column ga-4">
      <header class="d-flex flex-column ga-1">
        <h1 class="text-headline-medium" data-testid="scorecard-name">{{ provider.name }}</h1>
        <div class="d-flex flex-wrap align-center ga-2 text-body-medium text-medium-emphasis">
          <span data-testid="scorecard-ein">{{ provider.ein || provider.einMasked ? 'EIN' : 'Tax ID' }} {{ provider.einDisplay }}</span>
          <v-tooltip
            v-if="provider.einMasked"
            text="The IRS never issues this prefix, so the number may be a Social Security number published by mistake. It's masked here."
            max-width="340"
            location="top"
          >
            <template #activator="{ props: tip }">
              <v-chip v-bind="tip" size="x-small" color="warning" variant="tonal" tabindex="0">Unverified tax ID</v-chip>
            </template>
          </v-tooltip>
          <span>· {{ plural(provider.npiCount, 'NPI') }}</span>
          <v-chip v-if="provider.wholeTaxIdContract" size="x-small" variant="tonal">Has a whole-tax-ID contract</v-chip>
        </div>
        <p class="text-body-medium">
          <template v-for="(product, index) in provider.products" :key="product.key">
            <template v-if="index">, </template>{{ product.label }}: {{ plural(product.groups, 'comparison group') }}
          </template>
        </p>
        <p v-if="provider.otherNames.length" class="text-body-small text-medium-emphasis">
          Also listed as: {{ provider.otherNames.slice(0, shownNames).join('; ') }}
          <v-tooltip v-if="provider.otherNames.length > shownNames" location="top" max-width="420">
            <template #activator="{ props: tip }">
              <span v-bind="tip" class="has-tooltip" tabindex="0">and {{ provider.otherNames.length - shownNames }} more</span>
            </template>
            {{ provider.otherNames.slice(shownNames).join('; ') }}
          </v-tooltip>
        </p>
        <StatisticsNote :basis="result.statistics" class="mt-1" />
      </header>

      <v-row>
        <v-col cols="12" md="4">
          <v-card class="h-100" data-testid="price-index">
            <v-card-item>
              <v-card-title class="d-flex align-center ga-1">
                Price index
                <v-tooltip :text="indexExplainer(result.index.value)" max-width="340" location="top">
                  <template #activator="{ props: tip }">
                    <v-icon v-bind="tip" :icon="mdiInformationOutline" size="small" tabindex="0" aria-label="How the price index works" />
                  </template>
                </v-tooltip>
              </v-card-title>
            </v-card-item>
            <v-card-text class="d-flex flex-column ga-2">
              <p class="stat-figure text-display-small" data-testid="price-index-value">
                {{ result.index.value == null ? '—' : times(result.index.value) }}
              </p>
              <p class="text-body-medium">{{ indexSentence(result.index) }}</p>
              <p v-if="result.index.groupsCounted" class="text-body-medium text-medium-emphasis">{{ medianCounts(result.index) }}</p>
              <p v-if="result.index.groupsTotal > result.index.groupsCounted" class="text-body-small text-medium-emphasis">
                {{ plural(result.index.groupsTotal - result.index.groupsCounted, 'group') }} with fewer than
                {{ result.index.minimumProviders }} providers {{ result.index.groupsTotal - result.index.groupsCounted === 1 ? 'has' : 'have' }}
                no median, so no percentile either.
              </p>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" md="4">
          <ScorecardExtremes title="Furthest above the median" :groups="result.highest" empty="None of its rates is above the median." />
        </v-col>
        <v-col cols="12" md="4">
          <ScorecardExtremes title="Furthest below the median" :groups="result.lowest" empty="None of its rates is below the median." />
        </v-col>
      </v-row>

      <v-card>
        <v-card-item>
          <v-card-title>Every comparison group ({{ wholeNumber(result.groups.length) }})</v-card-title>
          <v-card-subtitle class="text-wrap">
            Each row is what a search for the code with this provider's tax ID shows: its rate, placed against every other
            provider in the same group.
          </v-card-subtitle>
        </v-card-item>
        <v-card-text>
          <v-text-field
            v-model="filter"
            label="Filter by code, name, product or setting"
            clearable
            autocomplete="off"
            class="mb-2"
            style="max-width: 420px"
          />
          <v-data-table
            :headers="headers"
            :items="result.groups"
            item-value="groupKey"
            :search="filter ?? ''"
            :filter-keys="filterKeys"
            :items-per-page="smAndDown ? 10 : 25"
            :mobile="smAndDown"
            density="compact"
            hover
            :row-props="() => ({ 'data-testid': 'scorecard-row' })"
            aria-label="Every comparison group for this provider"
          >
            <template #[`item.code`]="{ item }">
              <div class="py-1">
                <div class="font-weight-medium">{{ serviceName(item) }}</div>
                <div class="text-body-small text-medium-emphasis">{{ item.categoryLabel }}</div>
              </div>
            </template>
            <template #[`item.groupKey`]="{ item }">
              <div class="py-1">
                <div class="text-body-medium">{{ groupName(item) }}</div>
                <div class="text-body-small text-medium-emphasis">{{ item.unit }}</div>
              </div>
            </template>
            <template #[`item.providerCount`]="{ item }">{{ wholeNumber(item.providerCount) }}</template>
            <template #[`item.value`]="{ item }">
              <div class="text-no-wrap">
                <span class="font-weight-medium" data-testid="scorecard-rate">{{ money(item.value) }}</span>
                <div v-if="item.rateHigh !== item.rateLow" class="text-body-small text-medium-emphasis">
                  {{ money(item.rateLow) }} to {{ money(item.rateHigh) }}
                </div>
              </div>
            </template>
            <template #[`item.percentile`]="{ item }">
              <span data-testid="scorecard-percentile">{{ item.percentile == null ? '—' : ordinal(item.percentile) }}</span>
            </template>
            <template #[`item.vsMedian`]="{ item }">
              <span data-testid="scorecard-vs-median">{{ times(item.vsMedian) }}</span>
            </template>
            <template #[`item.band`]="{ item }">
              <v-chip v-if="item.band" :color="bandView[item.band]?.color" size="small" variant="outlined">{{ bandView[item.band]?.label }}</v-chip>
              <span v-else class="text-body-small text-medium-emphasis">Under {{ result.index.minimumProviders }} providers</span>
            </template>
            <template #[`item.actions`]="{ item }">
              <v-btn
                :to="searchLocation(item.search)"
                :icon="mdiMagnify"
                variant="text"
                size="small"
                :aria-label="`Search ${item.codeType} ${item.code} with this provider, ${groupName(item)}`"
                data-testid="scorecard-search"
              />
            </template>
            <template #no-data>
              <p class="text-body-medium text-medium-emphasis py-4">
                {{ filter ? 'No group matches this filter.' : 'No saved statistics for this provider yet.' }}
              </p>
            </template>
          </v-data-table>
        </v-card-text>
      </v-card>
    </div>
  </v-container>
</template>
