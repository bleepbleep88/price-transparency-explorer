<script setup lang="ts">
// A page's title (1 to 4 words) and at most one short line under it.
defineProps<{ title: string; subtitle?: string | null }>()
</script>

<template>
  <header class="page-header">
    <h1 class="page-title">{{ title }}</h1>
    <p v-if="subtitle" class="page-subtitle">{{ subtitle }}</p>
    <slot />
  </header>
</template>

<style scoped>
.page-header {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.page-title {
  font-family: var(--v-font-heading);
  font-size: 2rem;
  font-weight: 600;
  line-height: 1.15;
  text-wrap: balance;
}

.page-subtitle {
  font-size: 1rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}
</style>
