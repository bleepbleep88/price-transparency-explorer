<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '@/lib/apiClient'
import { monthDay, plural, shortDate } from '@/lib/format'
import PageHeader from '@/components/simple/PageHeader.vue'
import DetailsPanel from '@/components/simple/DetailsPanel.vue'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import ChangesTable from '@/features/history/components/ChangesTable.vue'
import CountTiles from '@/features/history/components/CountTiles.vue'
import { useChanges } from '@/features/history/composables/useHistoryQueries'
import { useSnapshotChoice } from '@/features/history/composables/useSnapshotChoice'
import {
  CHANGE_SORT_LABELS,
  MAX_CHANGE_PCT,
  mergeChangesQuery,
  parseChangesQuery,
  type ChangesPatch,
} from '@/features/history/lib/changesParams'
import { listedElsewhereSummary, WAITING_SHORT, waitingText } from '@/features/history/lib/changesText'
import FacilityChargesSwitch from '@/features/insights/components/FacilityChargesSwitch.vue'
import { planName } from '@/features/rates/lib/answer'
import { olderSnapshots } from '@/features/history/lib/snapshots'
import type { ChangeFilter } from '@/features/history/api/types'

// Decision 90: the four counts big, then the biggest changes as "Name · service · $old → $new". The newer file is the
// month picked in the app bar; the older one defaults to the month before. Everything else is in Details.
const route = useRoute()
const router = useRouter()
const params = computed(() => parseChangesQuery(route.query))

const { snapshots, current, isPending: snapshotsPending } = useSnapshotChoice()
const older = computed(() => olderSnapshots(snapshots.value, current.value))
const canCompare = computed(() => older.value.length > 0)

const changes = useChanges(params, canCompare)
const apiError = computed(() => (changes.error.value instanceof ApiError ? changes.error.value : null))
const result = computed(() => (changes.isError.value ? undefined : changes.data.value))
// Until both files' numbers are saved and current the API sends no counts or rows (refreshed when loading ends).
const counts = computed(() => (result.value?.current ? result.value.counts : null))

function update(patch: ChangesPatch) {
  const next = 'page' in patch ? patch : { ...patch, page: null }
  return router.push({ name: 'changes', query: mergeChangesQuery(route.query, next) })
}

// The service box applies on Enter, not on every keystroke.
const code = ref(params.value.code ?? '')
const minChangePct = ref<number | null>(params.value.minChangePct ?? null)
watch(
  () => params.value.code,
  (value) => (code.value = value ?? ''),
)
watch(
  () => params.value.minChangePct,
  (value) => (minChangePct.value = value ?? null),
)

function applyCode() {
  void update({ code: code.value.trim() || null })
}

function applyMinimum() {
  const minimum = Number(minChangePct.value)
  void update({ minChangePct: minimum > 0 && minimum <= MAX_CHANGE_PCT ? minimum : null })
}

const fromItems = computed(() => older.value.map((snapshot) => ({ title: monthDay(snapshot.indexDate), value: snapshot.snapshotId })))
const fromId = computed(() => result.value?.query.from ?? params.value.from ?? older.value[0]?.snapshotId ?? null)
const productItems = computed(() =>
  (current.value?.products ?? []).map((product) => ({ title: planName(product.productLabel), value: product.productKey })),
)
const sortItems = Object.entries(CHANGE_SORT_LABELS).map(([value, title]) => ({ value, title }))

const subtitle = computed(() => {
  const from = result.value?.from.indexDate ?? older.value.find((snapshot) => snapshot.snapshotId === fromId.value)?.indexDate
  return from && current.value ? `${monthDay(from)} to ${shortDate(current.value.indexDate)}` : null
})

const LIST_TITLES: Record<ChangeFilter, string> = {
  changed: 'Biggest changes',
  increased: 'Biggest rises',
  decreased: 'Biggest drops',
  new: 'New rates',
  dropped: 'Gone rates',
}
const listTitle = computed(() => LIST_TITLES[params.value.kind ?? 'changed'])

const listedElsewhere = computed(() => (counts.value ? listedElsewhereSummary(counts.value) : null))
const compared = computed(() => {
  if (!result.value) return ''
  const files = `${shortDate(result.value.from.indexDate)} and ${shortDate(result.value.to.indexDate)} files, compared by provider (tax ID), service, setting and plan.`
  return counts.value ? `${files} ${plural(counts.value.unchanged, 'rate')} stayed the same.` : files
})
const notCompared = computed(() => result.value?.products.filter((product) => !product.compared) ?? [])
</script>

<template>
  <v-container class="py-6 d-flex flex-column ga-6" max-width="960">
    <PageHeader title="Rate changes" :subtitle="subtitle" />

    <v-skeleton-loader v-if="snapshotsPending" type="card" />
    <LoadingPanel v-else-if="!snapshots.length" />
    <p v-else-if="snapshots.length < 2" class="text-medium-emphasis" data-testid="changes-single">Only one month is loaded.</p>
    <p v-else-if="!canCompare" class="text-medium-emphasis" data-testid="changes-oldest">
      Nothing older than {{ shortDate(current?.indexDate) }}. Pick a newer month.
    </p>

    <template v-else>
      <div class="filters" role="search" aria-label="Filter changes">
        <v-select
          label="Compared with"
          :items="fromItems"
          :model-value="fromId"
          density="compact"
          hide-details
          data-testid="changes-from"
          @update:model-value="(id) => update({ from: id })"
        />
        <v-select
          label="Plan"
          :items="productItems"
          :model-value="params.product ?? null"
          placeholder="All plans"
          persistent-placeholder
          clearable
          density="compact"
          hide-details
          data-testid="changes-product"
          @update:model-value="(product) => update({ product: product ?? null })"
        />
        <v-text-field
          v-model="code"
          label="Service code"
          placeholder="All, or e.g. 45378"
          persistent-placeholder
          clearable
          density="compact"
          hide-details
          data-testid="changes-code"
          @keydown.enter="applyCode"
          @click:clear="update({ code: null })"
        />
      </div>

      <DetailsPanel label="More filters" testid="changes-more">
        <div class="more-filters">
          <v-select
            label="Order"
            :items="sortItems"
            :model-value="params.sort"
            density="compact"
            hide-details
            data-testid="changes-sort"
            @update:model-value="(sort) => update({ sort })"
          />
          <v-number-input
            v-model="minChangePct"
            label="Changed by at least (%)"
            :min="0"
            :max="MAX_CHANGE_PCT"
            control-variant="hidden"
            density="compact"
            hide-details
            data-testid="changes-min"
            @keydown.enter="applyMinimum"
            @blur="applyMinimum"
          />
        </div>
        <v-switch
          :model-value="params.hideListedElsewhere"
          label="Hide rates only listed differently"
          color="primary"
          density="compact"
          hide-details
          inset
          data-testid="changes-hide-listed"
          @update:model-value="(hide) => update({ hideListedElsewhere: Boolean(hide) })"
        />
        <FacilityChargesSwitch
          :model-value="params.includeFacilityCharges"
          :count="counts?.facilityCharges ?? 0"
          @update:model-value="(include) => update({ includeFacilityCharges: include })"
        />
      </DetailsPanel>

      <LoadingPanel v-if="apiError?.isLoading" />
      <p v-else-if="apiError" class="text-high" data-testid="changes-error">
        {{ apiError.status === 400 ? apiError.message : 'Changes could not load.' }}
        <v-btn v-if="apiError.status !== 400" variant="text" color="primary" @click="changes.refetch()">Try again</v-btn>
      </p>
      <v-skeleton-loader v-else-if="changes.isPending.value" type="paragraph, list-item-two-line@4" />
      <p v-else-if="result && !counts" class="text-body-large" data-testid="changes-waiting">{{ WAITING_SHORT }}</p>

      <template v-else-if="result && counts">
        <CountTiles :counts="counts" :kind="params.kind" @kind="(kind) => update({ kind })" />

        <section class="d-flex flex-column ga-2" aria-labelledby="changes-list-title">
          <h2 id="changes-list-title" class="section-title" data-testid="changes-list-title">{{ listTitle }}</h2>
          <ChangesTable
            :rows="result.rows"
            :from="result.from"
            :to="result.to"
            :loading="changes.isPlaceholderData.value"
            @page="(page) => update({ page })"
          />
        </section>
      </template>

      <DetailsPanel v-if="result" testid="changes-details">
        <p v-if="!counts">{{ waitingText(result) }}</p>
        <p data-testid="changes-summary">{{ compared }}</p>
        <p>A change means the file lists a different rate, not that anyone paid it.</p>
        <p v-if="listedElsewhere" data-testid="changes-listed-elsewhere">{{ listedElsewhere }}</p>
        <p v-if="params.includeFacilityCharges" data-testid="changes-order">
          Facility charges with no procedure named come after the rest in the default order.
        </p>
        <p v-for="product in notCompared" :key="product.productKey" data-testid="changes-not-compared">
          {{ product.productLabel }}: {{ product.notComparedBecause }}
        </p>
      </DetailsPanel>
    </template>
  </v-container>
</template>

<style scoped>
.filters {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.more-filters {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

@media (max-width: 599px) {
  .filters,
  .more-filters {
    grid-template-columns: minmax(0, 1fr);
    gap: 8px;
  }
}

.section-title {
  font-size: 1.125rem;
  font-weight: 600;
}
</style>
