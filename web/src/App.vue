<script setup lang="ts">
import { mdiMagnify, mdiThemeLightDark } from '@mdi/js'
import { useTheme } from 'vuetify'
import DataStatusChip from '@/features/status/components/DataStatusChip.vue'
import { useDataStatus } from '@/features/status/useDataStatus'

const theme = useTheme()

// Start the live status stream once for the whole app.
useDataStatus()

const navItems = [{ title: 'Search', to: { name: 'search' }, icon: mdiMagnify }]
</script>

<template>
  <v-app>
    <v-app-bar flat border="b" density="comfortable">
      <v-app-bar-title>
        <router-link to="/" class="brand">Price Transparency Explorer</router-link>
      </v-app-bar-title>

      <nav aria-label="Main" class="d-none d-md-flex ga-1">
        <v-btn v-for="item in navItems" :key="item.title" :to="item.to" :prepend-icon="item.icon" variant="text">
          {{ item.title }}
        </v-btn>
      </nav>

      <template #append>
        <DataStatusChip class="me-2" />
        <v-btn
          :icon="mdiThemeLightDark"
          variant="text"
          :aria-label="`Switch to ${theme.current.value.dark ? 'light' : 'dark'} theme`"
          @click="theme.toggle()"
        />
      </template>
    </v-app-bar>

    <v-main>
      <router-view />
    </v-main>

    <v-footer class="text-caption text-medium-emphasis justify-center text-center px-4">
      Negotiated rates as published by the plan in its Transparency in Coverage files. A negotiated rate is not your
      out-of-pocket cost. CPT® is a registered trademark of the American Medical Association; descriptions are shown as
      published by the payer.
    </v-footer>
  </v-app>
</template>

<style scoped>
.brand {
  color: inherit;
  text-decoration: none;
  font-weight: 600;
}
</style>
