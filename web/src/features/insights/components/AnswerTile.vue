<script setup lang="ts">
import type { Tone } from '../lib/insightText'

// The one answer an insights page leads with: what it's about, one big figure, one short line. Styled as the shared
// PriceTile, for figures that aren't a price ("75%", "18% above typical").
withDefaults(defineProps<{ label: string; figure: string; line?: string | null; tone?: Tone }>(), { line: null, tone: 'neutral' })
</script>

<template>
  <div class="answer-tile" data-testid="answer-tile">
    <span class="answer-label">{{ label }}</span>
    <span class="answer-figure" :class="tone" data-testid="answer-figure">{{ figure }}</span>
    <span v-if="line" class="answer-line" data-testid="answer-line">{{ line }}</span>
    <slot />
  </div>
</template>

<style scoped>
.answer-tile {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  padding: 16px;
  background: rgb(var(--v-theme-surface));
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 8px;
}

.answer-label {
  font-size: 0.875rem;
  font-weight: 600;
}

.answer-figure {
  font-family: var(--v-font-heading);
  font-size: 2.5rem;
  font-weight: 600;
  line-height: 1.1;
  color: rgb(var(--v-theme-primary));
  text-wrap: balance;
}

.answer-figure.low {
  color: rgb(var(--v-theme-low));
}

.answer-figure.high {
  color: rgb(var(--v-theme-high));
}

.answer-line {
  font-size: 1rem;
}
</style>
