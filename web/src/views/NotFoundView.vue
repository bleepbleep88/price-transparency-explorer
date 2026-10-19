<script setup lang="ts">
import { mdiMapMarkerQuestionOutline } from '@mdi/js'
</script>

<template>
  <v-container class="py-16" max-width="720">
    <v-empty-state
      :icon="mdiMapMarkerQuestionOutline"
      headline="Page not found"
      text="That link doesn't match any page in the explorer."
    >
      <template #actions>
        <v-btn color="primary" :to="{ name: 'search' }">Go to search</v-btn>
      </template>
    </v-empty-state>
  </v-container>
</template>
