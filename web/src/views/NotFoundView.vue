<script setup lang="ts">
import { mdiMapMarkerQuestionOutline } from '@mdi/js'
</script>

<template>
  <v-container class="py-16" max-width="720">
    <v-empty-state :icon="mdiMapMarkerQuestionOutline" text="That link doesn't match any page in the explorer.">
      <template #headline>
        <h1 class="text-h5">Page not found</h1>
      </template>
      <template #actions>
        <v-btn color="primary" :to="{ name: 'search' }">Go to search</v-btn>
      </template>
    </v-empty-state>
  </v-container>
</template>
