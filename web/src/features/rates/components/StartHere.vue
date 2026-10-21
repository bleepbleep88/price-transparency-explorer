<script setup lang="ts">
import { useRoute } from 'vue-router'

// A few common services to try, by plain name. Examples stay on the month picked in the app bar.
const route = useRoute()

export interface Example {
  code: string
  label: string
}

const EXAMPLES: readonly Example[] = [
  { code: '45378', label: 'Colonoscopy' },
  { code: '99213', label: 'Office visit' },
  { code: '99284', label: 'ER visit' },
  { code: '66984', label: 'Cataract surgery' },
  { code: 'J0585', label: 'Botox' },
]
</script>

<template>
  <section class="examples" aria-label="Try a service">
    <span class="examples-label">Try</span>
    <v-chip
      v-for="example in EXAMPLES"
      :key="example.code"
      :to="{ name: 'search', query: { code: example.code, snapshot: route.query.snapshot } }"
      variant="outlined"
      rounded="pill"
      :data-testid="`example-${example.code}`"
    >
      {{ example.label }}
    </v-chip>
  </section>
</template>

<style scoped>
.examples {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.examples-label {
  font-weight: 600;
  margin-right: 4px;
}
</style>
