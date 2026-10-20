<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { mdiRefresh } from '@mdi/js'
import { ApiError } from '@/lib/apiClient'
import { shortDate, wholeNumber } from '@/lib/format'
import LoadingPanel from '@/features/status/components/LoadingPanel.vue'
import ChangesTable from '@/features/history/components/ChangesTable.vue'
import { useChanges } from '@/features/history/composables/useHistoryQueries'
import { useSnapshotChoice } from '@/features/history/composables/useSnapshotChoice'
import {
  DEFAULT_CHANGE_SORT,
  MAX_CHANGE_PCT,
  mergeChangesQuery,
  parseChangesQuery,
  type ChangeSort,
  type ChangesPatch,
} from '@/features/history/lib/changesParams'
import { countsSummary, listedElsewhereSummary, waitingText } from '@/features/history/lib/changesText'
import FacilityChargesSwitch from '@/features/insights/components/FacilityChargesSwitch.vue'
import { olderSnapshots } from '@/features/history/lib/snapshots'

const route = useRoute()
const router = useRouter()
const params = computed(() => parseChangesQuery(route.query))

// The newer file is the snapshot picked in the app bar; the older one defaults to the file before it.
const { snapshots, current, choose, isPending: snapshotsPending } = useSnapshotChoice()
const older = computed(() => olderSnapshots(snapshots.value, current.value))
const canCompare = computed(() => older.value.length > 0)

const changes = useChanges(params, canCompare)
const apiError = computed(() => (changes.error.value instanceof ApiError ? changes.error.value : null))
const result = computed(() => (changes.isError.value ? undefined : changes.data.value))
// Until both files' numbers are saved and current the API sends no counts or rows, only why (refreshed when loading ends).
const counts = computed(() => (result.value?.current ? result.value.counts : null))

function update(patch: ChangesPatch) {
  const next = 'page' in patch ? patch : { ...patch, page: null }
  return router.push({ name: 'changes', query: mergeChangesQuery(route.query, next) })
}

// Typed filters apply on Enter or Apply, not on every keystroke.
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

function applyTyped() {
  const minimum = Number(minChangePct.value)
  void update({ code: code.value.trim() || null, minChangePct: minimum > 0 && minimum <= MAX_CHANGE_PCT ? minimum : null })
}

const fromItems = computed(() => older.value.map((snapshot) => ({ title: shortDate(snapshot.indexDate), value: snapshot.snapshotId })))
const toItems = computed(() =>
  snapshots.value.map((snapshot) => ({
    title: shortDate(snapshot.indexDate),
    value: snapshot.snapshotId,
    props: { disabled: olderSnapshots(snapshots.value, snapshot).length === 0, subtitle: snapshot.isLatest ? 'Newest' : undefined },
  })),
)
const fromId = computed(() => result.value?.query.from ?? params.value.from ?? older.value[0]?.snapshotId ?? null)
const productItems = computed(() => (current.value?.products ?? []).map((product) => ({ title: product.productLabel, value: product.productKey })))

function chooseTo(id: unknown) {
  const snapshot = snapshots.value.find((candidate) => candidate.snapshotId === id)
  if (snapshot) void choose(snapshot)
}

const kinds = computed(() => [
  { value: 'increased', label: 'Went up', count: counts.value?.increased },
  { value: 'decreased', label: 'Went down', count: counts.value?.decreased },
  { value: 'new', label: 'New', count: counts.value?.new },
  { value: 'dropped', label: 'Gone', count: counts.value?.dropped },
])

const summary = computed(() =>
  result.value && counts.value ? countsSummary(counts.value, shortDate(result.value.from.indexDate), shortDate(result.value.to.indexDate)) : '',
)
const listedElsewhere = computed(() => (counts.value ? listedElsewhereSummary(counts.value) : null))
const notCompared = computed(() => result.value?.products.filter((product) => !product.compared) ?? [])

function onTableOptions(options: { page: number; pageSize: number; sort: ChangeSort }) {
  void update({ page: options.page, pageSize: options.pageSize, sort: options.sort })
}
</script>

<template>
  <v-container class="py-6" max-width="1280">
    <h1 class="text-headline-medium mb-1">Rate changes between files</h1>
    <p class="text-body-medium text-medium-emphasis mb-4">
      Negotiated rates that differ between two of the plan's monthly files, for the same provider, service, setting and plan. A
      change means the file lists a different rate, not that anyone paid it. The files don't say how often a service happens.
    </p>

    <v-skeleton-loader v-if="snapshotsPending" type="card" />
    <LoadingPanel v-else-if="!snapshots.length" />
    <v-empty-state
      v-else-if="snapshots.length < 2"
      headline="Only one file is loaded"
      text="Changes need two of the plan's monthly files. Add another index to the configuration."
      data-testid="changes-single"
    />
    <v-empty-state
      v-else-if="!canCompare"
      headline="Nothing older to compare with"
      :text="`The ${shortDate(current?.indexDate)} file is the oldest one loaded. Pick a newer file in the bar above.`"
      data-testid="changes-oldest"
    />

    <div v-else class="d-flex flex-column ga-4">
      <v-card>
        <v-card-text>
          <v-form @submit.prevent="applyTyped">
            <v-row density="compact" align="center">
              <v-col cols="12" sm="6" md="3">
                <v-select
                  label="Older file"
                  :items="fromItems"
                  :model-value="fromId"
                  data-testid="changes-from"
                  @update:model-value="(id) => update({ from: id })"
                />
              </v-col>
              <v-col cols="12" sm="6" md="3">
                <v-select
                  label="Newer file"
                  :items="toItems"
                  :model-value="current?.snapshotId"
                  data-testid="changes-to"
                  @update:model-value="chooseTo"
                />
              </v-col>
              <v-col cols="12" sm="6" md="3">
                <v-select
                  label="Product"
                  :items="productItems"
                  :model-value="params.product ?? null"
                  placeholder="All products"
                  persistent-placeholder
                  clearable
                  data-testid="changes-product"
                  @update:model-value="(product) => update({ product: product ?? null })"
                />
              </v-col>
              <v-col cols="12" sm="6" md="3">
                <v-text-field
                  v-model="code"
                  label="Billing code"
                  placeholder="All codes"
                  persistent-placeholder
                  clearable
                  data-testid="changes-code"
                  @click:clear="update({ code: null })"
                />
              </v-col>
              <v-col cols="12" sm="6" md="3">
                <v-number-input
                  v-model="minChangePct"
                  label="Changed by at least (%)"
                  :min="0"
                  :max="MAX_CHANGE_PCT"
                  control-variant="hidden"
                  hide-details
                  data-testid="changes-min"
                />
              </v-col>
              <v-col cols="12" sm="6" md="6">
                <v-switch
                  :model-value="params.hideListedElsewhere"
                  label="Hide new and gone rates that are only listed differently"
                  color="primary"
                  density="compact"
                  hide-details
                  data-testid="changes-hide-listed"
                  @update:model-value="(hide) => update({ hideListedElsewhere: Boolean(hide) })"
                />
              </v-col>
              <v-col cols="12" sm="6" md="6">
                <!-- Decision 89, the insights pages' switch: left out of the order and counts unless asked for. -->
                <FacilityChargesSwitch
                  :model-value="params.includeFacilityCharges"
                  :count="counts?.facilityCharges ?? 0"
                  @update:model-value="(include) => update({ includeFacilityCharges: include })"
                />
              </v-col>
              <v-col cols="12" md="6" class="d-flex justify-md-end align-center">
                <v-btn type="submit" color="primary" variant="tonal" data-testid="changes-apply">Apply</v-btn>
              </v-col>
            </v-row>
          </v-form>
        </v-card-text>
      </v-card>

      <LoadingPanel v-if="apiError?.isLoading" />
      <v-alert v-else-if="apiError?.status === 400" type="warning" variant="tonal">{{ apiError.message }}</v-alert>
      <v-alert v-else-if="apiError" type="error" variant="tonal" data-testid="changes-error">
        <p>{{ apiError.message }}</p>
        <template #append>
          <v-btn variant="text" :prepend-icon="mdiRefresh" @click="changes.refetch()">Try again</v-btn>
        </template>
      </v-alert>
      <v-skeleton-loader v-else-if="changes.isPending.value" type="paragraph, table" />

      <v-alert v-else-if="result && !result.current" type="info" variant="tonal" data-testid="changes-waiting">
        {{ waitingText(result) }}
      </v-alert>

      <template v-else-if="result && counts">
        <v-alert
          v-for="product in notCompared"
          :key="product.productKey"
          type="info"
          variant="tonal"
          density="comfortable"
          data-testid="changes-not-compared"
        >
          {{ product.productLabel }}. {{ product.notComparedBecause }}
        </v-alert>

        <section class="d-flex flex-column ga-2" aria-labelledby="changes-summary">
          <p id="changes-summary" class="text-body-large" data-testid="changes-summary">{{ summary }}</p>
          <p v-if="listedElsewhere" class="text-body-medium text-medium-emphasis" data-testid="changes-listed-elsewhere">
            {{ listedElsewhere }}
          </p>
          <v-chip-group
            column
            :model-value="params.kind ?? null"
            selected-class="text-primary"
            aria-label="Kind of change"
            @update:model-value="(kind) => update({ kind: (kind as string | undefined) ?? null })"
          >
            <v-chip
              v-for="kind in kinds"
              :key="kind.value"
              :value="kind.value"
              variant="outlined"
              filter
              role="button"
              :aria-pressed="params.kind === kind.value"
              :data-testid="`changes-kind-${kind.value}`"
            >
              {{ kind.label }}<span class="ms-1 text-medium-emphasis">{{ wholeNumber(kind.count) }}</span>
            </v-chip>
          </v-chip-group>
        </section>

        <p
          v-if="params.sort === DEFAULT_CHANGE_SORT && params.includeFacilityCharges"
          class="text-body-small text-medium-emphasis"
          data-testid="changes-order"
        >
          Biggest moves first. Facility charges with no procedure named come after the rest.
        </p>
        <div :style="{ opacity: changes.isPlaceholderData.value ? 0.6 : 1 }">
          <ChangesTable
            :rows="result.rows"
            :sort="params.sort"
            :from="result.from"
            :to="result.to"
            :loading="changes.isFetching.value"
            @update-options="onTableOptions"
          />
        </div>
      </template>
    </div>
  </v-container>
</template>
