<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { useDisplay } from 'vuetify'
import { mdiChevronDown, mdiChevronLeft, mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { money, plural, wholeNumber } from '@/lib/format'
import { providerName } from '@/lib/plainNames'
import { dollars } from '@/lib/plainNumbers'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import PageHeader from '@/components/simple/PageHeader.vue'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import type { ScorecardGroup } from '@/features/insights/api/types'
import AnswerTile from '@/features/insights/components/AnswerTile.vue'
import FacilityChargesSwitch from '@/features/insights/components/FacilityChargesSwitch.vue'
import InsightSection from '@/features/insights/components/InsightSection.vue'
import ScorecardExtremes from '@/features/insights/components/ScorecardExtremes.vue'
import StatisticsNote from '@/features/insights/components/StatisticsNote.vue'
import { insightKeys, useProviderScorecard } from '@/features/insights/composables/useInsightQueries'
import { searchLocation } from '@/features/insights/lib/insightLinks'
import { mergeInsightQuery, parseScorecardQuery, sortBy, sortParam, type InsightPatch } from '@/features/insights/lib/insightParams'
import { alsoListedAs, facilityLeftOut, OVERALL_METHOD, overallReading, serviceName, statisticsLine, toneOf } from '@/features/insights/lib/insightText'
import { filterScorecardGroups } from '@/features/insights/lib/scorecardFilter'
import { FACILITY_CHARGE_MARK, FACILITY_CHARGE_REASON, groupLine, rowLine, serviceLabel, tellApart, unitSuffix, versusTypical } from '@/features/insights/lib/plainWords'

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
const name = computed(() => (provider.value ? providerName(provider.value.name) : ''))
// Until the first statistics build there is nothing to compare; the note says so, and the page claims nothing else.
const saved = computed(() => result.value?.statistics.builtAt != null)
const overall = computed(() => (result.value ? overallReading(result.value) : null))
// Reached from a search row with another of its names, the page says it's the same provider.
const alsoListed = computed(() => (provider.value ? alsoListedAs(provider.value, params.value.as) : null))

// The router's afterEach puts the generic title back on every navigation (this page's own replace and a Back that
// remounts it from the cache included), so the provider's name goes back on whenever the answer or the address changes.
watch(
  [result, () => route.fullPath],
  ([value]) => {
    if (value && route.name === 'scorecard') document.title = `${providerName(value.provider.name)} · Price Explorer`
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

// The full list's filter, order and page live in the address. Typing and paging replace the entry, so Back leaves the
// page. With any of them set, the list opens by itself.
function replace(patch: InsightPatch) {
  return router.replace({ name: 'scorecard', params: { key: key.value }, query: mergeInsightQuery(route.query, patch, '') })
}

const showAll = ref(false)
const allOpen = computed(() => showAll.value || Boolean(params.value.q || params.value.sort || params.value.page > 1))
function toggleAll() {
  if (allOpen.value) {
    showAll.value = false
    void replace({ q: null, sort: null, page: null })
  } else {
    showAll.value = true
  }
}

const filter = ref('')
watch(() => params.value.q, (value) => (filter.value = value ?? ''), { immediate: true })
function onFilter(value: string | null) {
  filter.value = value ?? ''
  void replace({ q: filter.value.trim() || null })
}

// At most four columns; on a phone the typical price drops out (the "Compared" column still says how far off it is).
const headers = computed(() => [
  { title: 'Service', key: 'code', sortable: true },
  { title: 'Its price', key: 'value', sortable: true, align: 'end' as const, nowrap: true },
  ...(smAndDown.value ? [] : [{ title: 'Typical', key: 'median', sortable: true, align: 'end' as const, nowrap: true }]),
  { title: 'Compared', key: 'vsMedian', sortable: true, align: 'end' as const },
])
const shownGroups = computed(() => filterScorecardGroups(result.value?.groups ?? [], params.value.q))
// A modifier shows only when it tells a row apart from another with the same service, bill and plan.
const apart = computed(() => tellApart(result.value?.groups ?? [], (group) => [group.code, group.feeType, group.settingKey, group.productKey].join('|')))
// One price per service, place and plan: "45 prices in 2 plans".
const subtitle = computed(() => (result.value && provider.value ? `${plural(result.value.groups.length, 'price')} in ${plural(provider.value.products.length, 'plan')}` : null))

/** A facility-only charge is listed, but not compared unless facility charges are included. */
function compared(group: ScorecardGroup): boolean {
  return !group.facilityCharge || (result.value?.includeFacilityCharges ?? false)
}

function comparedText(group: ScorecardGroup): string {
  if (!compared(group)) return 'Not compared'
  return versusTypical(group.value, group.median, group.band) ?? 'Too few places'
}

function comparedTone(group: ScorecardGroup): string {
  if (!compared(group) || group.median == null) return ''
  return group.band === 'low' ? 'low' : group.band === 'high' ? 'high' : toneOf(group.value / group.median)
}

function setFacilityCharges(value: boolean) {
  void router.push({ name: 'scorecard', params: { key: key.value }, query: mergeInsightQuery(route.query, { includeFacilityCharges: value ? 'true' : null }, '') })
}
</script>

<template>
  <v-container class="py-10 d-flex flex-column ga-6" max-width="960">
    <v-btn :to="{ name: 'providers' }" variant="text" :prepend-icon="mdiChevronLeft" class="align-self-start px-1">Providers</v-btn>

    <LoadingPanel v-if="apiError?.isLoading" />
    <template v-else-if="apiError?.status === 404 || apiError?.status === 400">
      <PageHeader title="No such provider" subtitle="No prices for this provider in these files." data-testid="scorecard-not-found" />
      <v-btn :to="{ name: 'providers' }" variant="tonal" color="primary" class="align-self-start" data-testid="scorecard-find">
        Find a provider
      </v-btn>
    </template>
    <v-alert v-else-if="apiError" type="error" variant="tonal">
      {{ apiError.message }}
      <template #append>
        <v-btn variant="text" :prepend-icon="mdiRefresh" @click="scorecard.refetch()">Try again</v-btn>
      </template>
    </v-alert>
    <template v-else-if="scorecard.isPending.value">
      <v-skeleton-loader type="heading, paragraph" />
      <v-skeleton-loader type="table" />
    </template>

    <template v-else-if="result && provider">
      <PageHeader :title="name" :subtitle="subtitle" data-testid="scorecard-name">
        <p v-if="alsoListed" class="also-listed" data-testid="scorecard-also">{{ alsoListed }}</p>
      </PageHeader>
      <StatisticsNote :basis="result.statistics" />

      <div v-if="saved" class="d-flex flex-column ga-10" :style="{ opacity: scorecard.isPlaceholderData.value ? 0.6 : 1 }">
        <div class="d-flex flex-column ga-4">
          <AnswerTile
            v-if="overall"
            :label="overall.figure === 'Too few to say' ? 'Overall' : 'Usually'"
            :figure="overall.figure"
            :line="overall.line"
            :tone="overall.tone"
            data-testid="scorecard-overall"
          />
          <FacilityChargesSwitch :model-value="result.includeFacilityCharges" :count="result.facilityCharges" @update:model-value="setFacilityCharges" />
        </div>

        <div class="extremes">
          <ScorecardExtremes title="Most above typical" :groups="result.highest" tone="high" empty="None well above typical." />
          <ScorecardExtremes title="Most below typical" :groups="result.lowest" tone="low" empty="None well below typical." />
        </div>

        <InsightSection>
          <v-btn
            variant="text"
            color="primary"
            class="align-self-start px-1"
            :append-icon="mdiChevronDown"
            :aria-expanded="allOpen"
            data-testid="scorecard-see-all"
            @click="toggleAll"
          >
            {{ allOpen ? 'Hide the full list' : `See all ${wholeNumber(result.groups.length)}` }}
          </v-btn>
          <template v-if="allOpen">
            <v-text-field
              :model-value="filter"
              label="Filter"
              placeholder="Name, plan or place"
              persistent-placeholder
              clearable
              autocomplete="off"
              hide-details
              density="compact"
              style="max-width: 360px"
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
              :mobile="false"
              density="compact"
              class="all-table"
              :row-props="() => ({ 'data-testid': 'scorecard-row' })"
              @update:sort-by="(value) => replace({ sort: sortParam(value) })"
              @update:page="(page) => replace({ page })"
            >
              <template #[`item.code`]="{ item }">
                <div class="py-2">
                  <router-link :to="searchLocation(item.search)" class="service-link" data-testid="scorecard-search">
                    {{ serviceLabel(item) }}
                  </router-link>
                  <span class="code"> {{ item.code }}</span>
                  <div class="text-body-medium text-medium-emphasis">
                    {{ item.facilityCharge ? `${FACILITY_CHARGE_MARK} · ` : '' }}{{ rowLine(item, apart.get(item)) }}
                  </div>
                </div>
              </template>
              <template #[`item.value`]="{ item }">
                <span class="text-no-wrap" data-testid="scorecard-rate">{{ dollars(item.value) }}</span>
              </template>
              <template #[`item.median`]="{ item }">
                <span class="text-no-wrap">{{ item.median == null ? '—' : dollars(item.median) }}</span>
              </template>
              <template #[`item.vsMedian`]="{ item }">
                <span class="compared" :class="comparedTone(item)" data-testid="scorecard-vs-typical">{{ comparedText(item) }}</span>
              </template>
              <template #no-data>
                <p class="text-body-large text-medium-emphasis py-4">{{ params.q ? 'Nothing matches.' : 'No prices saved yet.' }}</p>
              </template>
            </v-data-table>
          </template>
        </InsightSection>
      </div>

      <DetailsPanel testid="scorecard-details">
        <p data-testid="scorecard-ein">{{ provider.ein || provider.einMasked ? 'EIN' : 'Tax ID' }} {{ provider.einDisplay }}</p>
        <p v-if="provider.einMasked">
          The IRS never issues this prefix, so the number may be a Social Security number published by mistake. It's masked here.
        </p>
        <p>
          {{ provider.name }} · {{ plural(provider.npiCount, 'NPI') }}<template v-if="provider.wholeTaxIdContract">
            · has a contract for its whole tax ID</template>
        </p>
        <p v-if="provider.otherNames.length">Also listed as: {{ provider.otherNames.join('; ') }}</p>
        <p>
          <template v-for="(product, index) in provider.products" :key="product.key">
            <template v-if="index">, </template>{{ product.label }}: {{ plural(product.groups, 'service') }}
          </template>
        </p>
        <p>{{ statisticsLine(result.statistics) }}</p>
        <p>{{ OVERALL_METHOD }} It needs 5 services with 5 or more places each.</p>
        <p v-if="facilityLeftOut(result)">{{ facilityLeftOut(result) }} {{ FACILITY_CHARGE_REASON }}</p>
        <p v-else>{{ FACILITY_CHARGE_REASON }}</p>
        <div v-if="result.highest.length || result.lowest.length">
          <p class="font-weight-medium">The services above, by billing code</p>
          <ul class="detail-list">
            <li v-for="group in [...result.highest, ...result.lowest]" :key="group.groupKey">
              {{ serviceName(group) }} · {{ groupLine(group) }} · {{ group.unit }} · {{ money(group.value) }}{{ unitSuffix(group) }}
            </li>
          </ul>
        </div>
      </DetailsPanel>
    </template>
  </v-container>
</template>

<style scoped>
.extremes {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 24px 40px;
}

@media (max-width: 599px) {
  .extremes {
    grid-template-columns: minmax(0, 1fr);
  }
}

.all-table {
  background: transparent;
}

.all-table :deep(td),
.all-table :deep(th) {
  font-size: 1rem;
  vertical-align: top;
}

.all-table :deep(th) {
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

.compared {
  font-weight: 600;
}

.also-listed {
  font-size: 1rem;
}

.compared.low {
  color: rgb(var(--v-theme-low));
}

.compared.high {
  color: rgb(var(--v-theme-high));
}

.detail-list {
  padding-left: 16px;
}
</style>
