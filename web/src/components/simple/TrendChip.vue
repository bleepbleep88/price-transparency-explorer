<script setup lang="ts">
import { computed } from 'vue'

// How a price moved across the monthly files, in a few words: "Rates steady since Jul", "Up $12 since Aug". Up is red
// and down green, as on the Changes page; steady and mixed stay plain.
const props = defineProps<{ text: string | null }>()
const tone = computed(() => (/^(rates )?up\b/i.test(props.text ?? '') ? 'up' : /^(rates )?down\b/i.test(props.text ?? '') ? 'down' : 'flat'))
</script>

<template>
  <span v-if="text" class="trend-chip" :class="tone" data-testid="trend-chip">{{ text }}</span>
</template>

<style scoped>
.trend-chip {
  align-self: flex-start;
  font-size: 0.875rem;
  line-height: 1.5;
  padding: 1px 10px;
  border-radius: 999px;
  color: rgba(var(--v-theme-on-surface), var(--v-medium-emphasis-opacity));
  background: rgba(var(--v-theme-on-surface), 0.06);
}

.trend-chip.up {
  color: rgb(var(--v-theme-high));
  background: rgba(var(--v-theme-high), 0.1);
}

.trend-chip.down {
  color: rgb(var(--v-theme-low));
  background: rgba(var(--v-theme-low), 0.1);
}
</style>
