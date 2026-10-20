<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { useDisplay } from 'vuetify'
import { mdiArrowLeft, mdiInformationOutline, mdiMagnify, mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { money, plural, wholeNumber } from '@/lib/format'
import { dollars } from '@/lib/plainNumbers'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import type { ScorecardGroup } from '@/features/insights/api/types'
import FacilityChargesSwitch from '@/features/insights/components/FacilityChargesSwitch.vue'
import ScorecardExtremes from '@/features/insights/components/ScorecardExtremes.vue'
import StatisticsNote from '@/features/insights/components/StatisticsNote.vue'
import { insightKeys, useProviderScorecard } from '@/features/insights/composables/useInsightQueries'
import { searchLocation } from '@/features/insights/lib/insightLinks'
import { mergeInsightQuery, parseScorecardQuery, sortBy, sortParam, type InsightPatch } from '@/features/insights/lib/insightParams'
import { OVERALL_EXPLAINER, overallSummary, serviceName } from '@/features/insights/lib/insightText'
import { filterScorecardGroups } from '@/features/insights/lib/scorecardFilter'
import { FACILITY_CHARGE_MARK, groupLine, position, unitSuffix } from '@/features/insights/lib/plainWords'

const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()
const { smAndDown } = useDisplay()

const key = computed(() => String(route.params.key ?? ''))
const params = computed(() => parseScorecardQuery(route.query))
const scorecard = useProviderScorecard(key, () => params.value.snapshot, () => params.value.includeFacilityCharges ?? false)
const result = computed(() => (scorecard.isError.value ? undefined : scorecard.data.value))
const apiError = computed(() => (scorecard.error.value instanceof ApiError ? scorecard.error.value : null))
const provider = computed(() => result.value?.provider)
// Until the first statistics build there is nothing to compare; the note says so, and the page claims nothing else.
const saved = computed(() => result.value?.statistics.builtAt != null)
const overall = computed(() => (result.value ? overallSummary(result.value) : null))

// The router's afterEach puts the generic title back on every navigation (this page's own replace and a Back that
// remounts it from the cache included), so the provider's name goes back on whenever the answer or the address changes.
watch(
  [result, () => route.fullPath],
  ([value]) => {
    if (value && route.name === 'scorecard') document.title = `${value.provider.name} · Provider scorecard · Price Transparency Explorer`
  },
  { immediate: true },
)

// An EIN typed into the address, or a masked tax ID's full number, gives way to the provider's key, so the address
// never keeps a masked number. The answer is already here, so the key's page opens without asking again.
watch(result, (value) => {
  if (value && value.provider.providerKey !== key.value) {
    queryClient.setQueryData(
      insightKeys.scorecard(value.provider.providerKey, params.value.snapshot, params.value.includeFacilityCharges ?? false),
      value,
    )
    void router.replace({ name: 'scorecard', params: { key: value.provider.providerKey }, query: route.query })
  }
})

// The table's filter, order and page live in the address. Typing and paging replace the entry, so Back leaves the page.
function replace(patch: InsightPatch) {
  return router.replace({ name: 'scorecard', params: { key: key.value }, query: mergeInsightQuery(route.query, patch, '') })
}

const filter = ref('')
watch(() => params.value.q, (value) => (filter.value = value ?? ''), { immediate: true })
function onFilter(value: string | null) {
  filter.value = value ?? ''
  void replace({ q: filter.value.trim() || null })
}

const shownNames = 3
const headers = [
  { title: 'Service', key: 'code', sortable: true },
  { title: 'Setting and plan', key: 'groupKey', sortable: false },
  { title: 'Providers', key: 'providerCount', sortable: true, align: 'end' as const, nowrap: true },
  { title: 'Its price', key: 'value', sortable: true, align: 'end' as const, nowrap: true },
  { title: 'Typical price', key: 'median', sortable: true, align: 'end' as const, nowrap: true },
  // Sorted by its share of the typical price; shown in dollars and that share.
  { title: 'Compared with typical', key: 'vsMedian', sortable: true, align: 'end' as const },
  { title: 'Where it sits', key: 'band', sortable: false },
  { title: 'Search', key: 'actions', sortable: false, align: 'end' as const },
]
// The table's own search only reads its column values, so the rows are filtered here, on what each row shows.
const shownGroups = computed(() => filterScorecardGroups(result.value?.groups ?? [], params.value.q))
const bandColor: Record<string, string | undefined> = { low: 'low', typical: undefined, high: 'high' }

/** A facility charge with no procedure named is listed, but not compared unless facility charges are included. */
function compared(group: ScorecardGroup): boolean {
  return !group.facilityCharge || (result.value?.includeFacilityCharges ?? false)
}

function placed(group: ScorecardGroup) {
  return position(group.band, group.value, group.median, unitSuffix(group))
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
      <template #append>
        <v-btn :to="{ name: 'providers' }" variant="text">Find a provider</v-btn>
      </template>
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

    <div v-else-if="result && provider" class="d-flex flex-column ga-4" :style="{ opacity: scorecard.isPlaceholderData.value ? 0.6 : 1 }">
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
            <template v-if="index">, </template>{{ product.label }}: {{ plural(product.groups, 'service') }}
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

      <template v-if="saved">
        <FacilityChargesSwitch
          :model-value="result.includeFacilityCharges"
          :count="result.facilityCharges"
          @update:model-value="(value) => router.push({ name: 'scorecard', params: { key }, query: mergeInsightQuery(route.query, { includeFacilityCharges: value ? 'true' : null }, '') })"
        />

        <v-row>
          <v-col cols="12" md="4">
            <v-card class="h-100" data-testid="scorecard-overall">
              <v-card-item>
                <v-card-title tag="h2">Overall</v-card-title>
                <template #append>
                  <v-tooltip :text="OVERALL_EXPLAINER" max-width="340" location="top">
                    <template #activator="{ props: tip }">
                      <v-btn v-bind="tip" :icon="mdiInformationOutline" variant="text" size="x-small" density="comfortable" aria-label="How this is worked out" />
                    </template>
                  </v-tooltip>
                </template>
              </v-card-item>
              <v-card-text v-if="overall" class="d-flex flex-column ga-2">
                <p class="stat-figure text-headline-small" data-testid="scorecard-overall-text">{{ overall.headline }}</p>
                <p v-if="overall.detail" class="text-body-medium text-medium-emphasis" data-testid="scorecard-overall-detail">{{ overall.detail }}</p>
              </v-card-text>
            </v-card>
          </v-col>
          <v-col cols="12" md="4">
            <ScorecardExtremes title="Most above typical" :groups="result.highest" empty="None of its prices is more than a little above typical." />
          </v-col>
          <v-col cols="12" md="4">
            <ScorecardExtremes title="Most below typical" :groups="result.lowest" empty="None of its prices is more than a little below typical." />
          </v-col>
        </v-row>

        <v-card>
          <v-card-item>
            <v-card-title tag="h2">Every service ({{ wholeNumber(result.groups.length) }})</v-card-title>
            <v-card-subtitle class="text-wrap">
              Each row is the same service, in the same setting, under the same plan, and matches a search for that code with
              this provider's tax ID.
            </v-card-subtitle>
          </v-card-item>
          <v-card-text>
            <v-text-field
              :model-value="filter"
              label="Filter by code, name, plan or setting"
              clearable
              autocomplete="off"
              class="mb-2"
              style="max-width: 420px"
              data-testid="scorecard-filter"
              @update:model-value="onFilter"
            />
            <v-data-table
              :headers="headers"
              :items="shownGroups"
              item-value="groupKey"
              :sort-by="sortBy(params.sort)"
              :page="params.page"
              :items-per-page="smAndDown ? 10 : 25"
              :mobile="smAndDown"
              density="compact"
              hover
              :row-props="() => ({ 'data-testid': 'scorecard-row' })"
              @update:sort-by="(value) => replace({ sort: sortParam(value) })"
              @update:page="(page) => replace({ page })"
            >
              <template #[`item.code`]="{ item }">
                <div class="py-1">
                  <div class="font-weight-medium">{{ serviceName(item) }}</div>
                  <div class="text-body-small text-medium-emphasis">
                    {{ item.facilityCharge ? FACILITY_CHARGE_MARK : item.categoryLabel }}
                  </div>
                </div>
              </template>
              <template #[`item.groupKey`]="{ item }">
                <div class="py-1">
                  <div class="text-body-medium">{{ groupLine(item) }}</div>
                  <div class="text-body-small text-medium-emphasis">{{ item.unit }}</div>
                </div>
              </template>
              <template #[`item.providerCount`]="{ item }">{{ wholeNumber(item.providerCount) }}</template>
              <template #[`item.value`]="{ item }">
                <div class="text-no-wrap">
                  <span class="font-weight-medium" data-testid="scorecard-rate">{{ money(item.value) }}{{ unitSuffix(item) }}</span>
                  <div v-if="item.rateHigh !== item.rateLow" class="text-body-small text-medium-emphasis">
                    {{ money(item.rateLow) }} to {{ money(item.rateHigh) }}{{ unitSuffix(item) }}
                  </div>
                </div>
              </template>
              <template #[`item.median`]="{ item }">
                <span class="text-no-wrap">{{ item.median == null ? '—' : `${dollars(item.median)}${unitSuffix(item)}` }}</span>
              </template>
              <template #[`item.vsMedian`]="{ item }">
                <span data-testid="scorecard-vs-typical">
                  <template v-if="!compared(item)">Not compared</template>
                  <template v-else-if="item.median == null">Fewer than 5 providers: too few to compare</template>
                  <template v-else>{{ placed(item).difference }}</template>
                </span>
              </template>
              <template #[`item.band`]="{ item }">
                <v-chip
                  v-if="compared(item) && placed(item).label"
                  :color="bandColor[item.band ?? 'typical']"
                  size="small"
                  variant="outlined"
                  class="text-no-wrap"
                >
                  {{ placed(item).label }}
                </v-chip>
              </template>
              <template #[`item.actions`]="{ item }">
                <v-btn
                  :to="searchLocation(item.search)"
                  :icon="mdiMagnify"
                  variant="text"
                  size="small"
                  :aria-label="`Search ${item.codeType} ${item.code} with this provider, ${groupLine(item)}`"
                  data-testid="scorecard-search"
                />
              </template>
              <template #no-data>
                <p class="text-body-medium text-medium-emphasis py-4">
                  {{ params.q ? 'Nothing matches this filter.' : 'No saved figures for this provider.' }}
                </p>
              </template>
            </v-data-table>
          </v-card-text>
        </v-card>
      </template>
    </div>
  </v-container>
</template>
