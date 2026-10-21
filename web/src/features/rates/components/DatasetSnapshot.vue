<script setup lang="ts">
import { computed, ref } from 'vue'
import { plural, shortDate, wholeNumber } from '@/lib/format'
import type { SnapshotSummary } from '../api/types'
import { usePlans } from '../composables/useRateQueries'

// Which files are loaded, in one line: "Sep 28, 2026 files · 2 plans · 17,140 prices". The plans open a list.
const props = defineProps<{ snapshot: SnapshotSummary }>()

const plansOpen = ref(false)
const plans = usePlans(() => (plansOpen.value ? props.snapshot.snapshotId : null))

const prices = computed(() => props.snapshot.products.reduce((sum, product) => sum + (product.uniqueRates ?? 0), 0))
const unread = computed(() => props.snapshot.products.filter((product) => product.status === 'failed' || product.status === 'partial').length)
</script>

<template>
  <div class="dataset" data-testid="dataset-snapshot">
    <p>
      {{ shortDate(snapshot.indexDate) }} files ·
      <button type="button" class="plans-link" data-testid="dataset-plans" @click="plansOpen = true">
        {{ plural(snapshot.products.length, 'plan') }}
      </button>
      <template v-if="prices > 0"> · {{ wholeNumber(prices) }} prices</template>
    </p>
    <p v-if="unread" class="problem">{{ plural(unread, 'file') }} not fully loaded</p>

    <v-dialog v-model="plansOpen" max-width="720" scrollable>
      <v-card title="Plans in these files">
        <v-card-text>
          <v-skeleton-loader v-if="plans.isPending.value" type="list-item@6" />
          <div v-else-if="plans.data.value" class="d-flex flex-column ga-4">
            <section v-for="product in plans.data.value.products" :key="product.productKey">
              <h3 class="product-title">{{ product.productLabel }}</h3>
              <v-list density="compact" class="py-0">
                <v-list-item
                  v-for="plan in product.plans"
                  :key="plan.planId ?? plan.names[0]"
                  :title="plan.names.join(' · ')"
                  :subtitle="`${plan.planIdType?.toUpperCase() ?? 'ID'} ${plan.planId ?? 'not stated'}`"
                  lines="two"
                />
              </v-list>
            </section>
          </div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="plansOpen = false">Close</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </div>
</template>

<style scoped>
.dataset {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.plans-link {
  padding: 0;
  font: inherit;
  color: rgb(var(--v-theme-primary));
  text-decoration: underline;
  background: none;
  border: 0;
  cursor: pointer;
}

.plans-link:focus-visible {
  outline: 2px solid rgb(var(--v-theme-primary));
  outline-offset: 2px;
}

.problem {
  color: rgb(var(--v-theme-error));
}

.product-title {
  font-size: 1.125rem;
  font-weight: 600;
}
</style>
