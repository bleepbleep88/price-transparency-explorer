<script setup lang="ts">
import { mdiOpenInNew } from '@mdi/js'
import { useRoute } from 'vue-router'
import { START_HERE } from '../lib/lenses'

// Examples stay on the snapshot picked in the app bar.
const route = useRoute()
</script>

<template>
  <section aria-labelledby="start-heading" class="d-flex flex-column ga-3">
    <h2 id="start-heading" class="text-title-large">Start here</h2>
    <v-row density="compact">
      <v-col v-for="card in START_HERE" :key="card.key" cols="12" sm="6" lg="4">
        <v-card class="h-100 d-flex flex-column" :data-testid="`start-${card.key}`">
          <v-card-item>
            <v-card-title>{{ card.label }}</v-card-title>
            <p class="text-body-medium text-medium-emphasis mt-1">{{ card.answers }}</p>
          </v-card-item>
          <v-list density="compact" class="py-0 bg-transparent flex-grow-1" :aria-label="`Example searches for ${card.label}`">
            <!-- Each example is a link (it is just a URL); the wrapper keeps the list's items valid for screen readers. -->
            <div v-for="example in card.examples" :key="`${example.code}-${example.npi ?? ''}`" role="listitem">
              <v-list-item
                :to="{ name: 'search', query: { code: example.code, lens: card.key, npi: example.npi, snapshot: route.query.snapshot } }"
                exact
                :data-testid="`start-${card.key}-${example.code}`"
              >
                <template #prepend>
                  <span class="example-code text-primary">{{ example.code }}</span>
                </template>
                <v-list-item-title class="text-wrap text-body-medium">{{ example.label }}</v-list-item-title>
              </v-list-item>
            </div>
          </v-list>
          <v-card-actions v-if="card.links" class="flex-wrap">
            <v-btn
              v-for="link in card.links"
              :key="link.href"
              :href="link.href"
              target="_blank"
              rel="noopener"
              size="small"
              :append-icon="mdiOpenInNew"
            >
              {{ link.label }}
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>
  </section>
</template>

<style scoped>
/* Codes sit in their own column, like an account number in a ledger. */
.example-code {
  min-width: 3.75rem;
  font-weight: 600;
}
</style>
