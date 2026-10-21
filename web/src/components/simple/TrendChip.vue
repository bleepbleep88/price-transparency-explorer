<script setup lang="ts">
// How a price moved across the monthly files, in a few words: "Rates steady since Jul", "Up $12 since Aug".
defineProps<{ text: string | null }>()
</script>

<template>
  <span v-if="text" class="trend-chip" data-testid="trend-chip">{{ text }}</span>
</template>

<style scoped>
.trend-chip {
  align-self: flex-start;
  font-size: 0.8125rem;
  line-height: 1.5;
  padding: 1px 10px;
  border-radius: 999px;
  color: rgb(var(--v-theme-primary));
  background: rgba(var(--v-theme-primary), 0.1);
}
</style>
