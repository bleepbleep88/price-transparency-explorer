<script setup lang="ts">
// A plain section with a short title (IBM Plex Sans 600, 18px), no card.
defineProps<{ title?: string | null }>()
</script>

<template>
  <section class="insight-section">
    <h2 v-if="title" class="section-title">{{ title }}</h2>
    <slot />
  </section>
</template>

<style scoped>
.insight-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.section-title {
  font-family: var(--v-font-body);
  font-size: 1.125rem;
  font-weight: 600;
  line-height: 1.35;
}
</style>
