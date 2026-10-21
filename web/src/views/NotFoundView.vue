<script setup lang="ts">
import PageHeader from '@/components/simple/PageHeader.vue'
</script>

<template>
  <v-container class="not-found" max-width="960">
    <PageHeader title="Page not found" subtitle="That link doesn't match a page." />
    <v-btn color="primary" :to="{ name: 'search' }" class="align-self-start">Go to search</v-btn>
  </v-container>
</template>

<style scoped>
.not-found {
  display: flex;
  flex-direction: column;
  gap: 24px;
  padding-top: 40px;
  padding-bottom: 40px;
}
</style>
