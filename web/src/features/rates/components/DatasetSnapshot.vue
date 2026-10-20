<script setup lang="ts">
import { computed, ref } from 'vue'
import { mdiOpenInNew } from '@mdi/js'
import { megabytes, plural, shortDate, wholeNumber } from '@/lib/format'
import type { SnapshotSummary } from '../api/types'
import { usePlans } from '../composables/useRateQueries'

const props = defineProps<{ snapshot: SnapshotSummary }>()

const plansOpen = ref(false)
const plans = usePlans(() => (plansOpen.value ? props.snapshot.snapshotId : null))

const statusColor: Record<string, string> = { loaded: 'success', partial: 'warning', failed: 'error', pending: 'default' }
const totals = computed(() =>
  props.snapshot.products.reduce(
    (sum, product) => ({ published: sum.published + (product.pricesPublished ?? 0), unique: sum.unique + (product.uniqueRates ?? 0) }),
    { published: 0, unique: 0 },
  ),
)
</script>

<template>
  <v-card data-testid="dataset-snapshot">
    <v-card-item>
      <v-card-title class="text-wrap">What's loaded</v-card-title>
      <v-card-subtitle class="text-wrap">
        Snapshot {{ shortDate(snapshot.indexDate) }} from {{ snapshot.reportingEntityName ?? 'the plan' }}
        <template v-if="snapshot.reportingEntityType">({{ snapshot.reportingEntityType }})</template>
      </v-card-subtitle>
    </v-card-item>

    <v-card-text class="d-flex flex-column ga-3">
      <p class="text-body-medium">
        {{ wholeNumber(totals.published) }} published prices become
        <strong>{{ wholeNumber(totals.unique) }} unique rates</strong> once exact duplicates are removed.
      </p>

      <v-sheet v-for="product in snapshot.products" :key="product.fileId" rounded="sm" border class="pa-3 d-flex flex-column ga-1">
        <div class="d-flex align-center ga-2 flex-wrap">
          <span class="text-title-small">{{ product.productLabel }}</span>
          <v-chip :color="statusColor[product.status] ?? 'default'" size="x-small" variant="tonal">{{ product.status }}</v-chip>
        </div>
        <p class="text-body-medium text-medium-emphasis">
          {{ plural(product.distinctPlanIds, 'plan ID') }} ({{ plural(product.planEntries, 'listed plan') }}) ·
          {{ wholeNumber(product.distinctEins) }} tax IDs · {{ wholeNumber(product.distinctNpis) }} NPIs ·
          {{ megabytes(product.contentLength) }}
        </p>
        <p class="text-body-medium text-medium-emphasis">
          {{ wholeNumber(product.pricesPublished) }} prices → {{ wholeNumber(product.uniqueRates) }} unique rates
        </p>
        <p class="text-body-small text-medium-emphasis">
          Dates: index {{ shortDate(snapshot.indexDate) }} · file says {{ shortDate(product.fileLastUpdatedOn) }} · server
          {{ shortDate(product.httpLastModified) }}
        </p>
        <p v-if="product.warning || product.error" class="text-body-small text-warning">{{ product.warning ?? product.error }}</p>
        <v-btn :href="product.url" target="_blank" rel="noopener" variant="text" size="small" :append-icon="mdiOpenInNew" class="px-0 align-self-start">
          Rate file
        </v-btn>
      </v-sheet>

      <div class="d-flex flex-wrap ga-2">
        <v-btn variant="tonal" size="small" @click="plansOpen = true">See the plans</v-btn>
        <v-btn :href="snapshot.indexUrl" target="_blank" rel="noopener" variant="text" size="small" :append-icon="mdiOpenInNew">
          Index file
        </v-btn>
      </div>
    </v-card-text>

    <v-dialog v-model="plansOpen" max-width="720" scrollable>
      <v-card title="Plans in this snapshot" subtitle="Every plan in a product shares that product's rates.">
        <v-card-text>
          <v-skeleton-loader v-if="plans.isPending.value" type="list-item@6" />
          <template v-else-if="plans.data.value">
            <section v-for="product in plans.data.value.products" :key="product.productKey" class="mb-4">
              <h3 class="text-title-medium">{{ product.productLabel }}</h3>
              <p class="text-body-small text-medium-emphasis mb-1">
                {{ plural(product.planEntries, 'listed plan') }} under {{ plural(product.plans.length, 'plan ID') }}
                (cost-sharing variants share an ID)
              </p>
              <v-list density="compact" class="py-0">
                <v-list-item
                  v-for="plan in product.plans"
                  :key="plan.planId ?? plan.names[0]"
                  :title="`${plan.planIdType?.toUpperCase() ?? 'ID'} ${plan.planId ?? 'not stated'}`"
                  :subtitle="plan.names.join(' · ')"
                  lines="three"
                />
              </v-list>
            </section>
          </template>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="plansOpen = false">Close</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-card>
</template>
