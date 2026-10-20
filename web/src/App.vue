<script setup lang="ts">
import { ALWAYS_TRUE } from '@/lib/plainNumbers'
import { mdiAccountSearchOutline, mdiChartBar, mdiCompareHorizontal, mdiDatabaseOutline, mdiMagnify, mdiMenu, mdiThemeLightDark } from '@mdi/js'
import { useTheme } from 'vuetify'
import DataStatusChip from '@/features/status/components/DataStatusChip.vue'
import { useDataStatus } from '@/features/status/useDataStatus'

const theme = useTheme()

// Start the live status stream once for the whole app.
useDataStatus()

const navItems = [
  { title: 'Search', to: { name: 'search' }, icon: mdiMagnify },
  { title: 'Price gaps', to: { name: 'gaps' }, icon: mdiChartBar },
  { title: 'Plan comparison', to: { name: 'compare' }, icon: mdiCompareHorizontal },
  { title: 'Provider scorecards', to: { name: 'providers' }, icon: mdiAccountSearchOutline },
  { title: 'Sources', to: { name: 'sources' }, icon: mdiDatabaseOutline },
]
</script>

<template>
  <v-app>
    <v-app-bar flat border="b" density="comfortable">
      <v-app-bar-title>
        <router-link to="/" class="brand">Price Transparency Explorer</router-link>
      </v-app-bar-title>

      <nav aria-label="Main" class="d-none d-lg-flex ga-1">
        <v-btn v-for="item in navItems" :key="item.title" :to="item.to" :prepend-icon="item.icon" variant="text" exact>
          {{ item.title }}
        </v-btn>
      </nav>

      <template #append>
        <DataStatusChip class="me-2" />
        <!-- Below the large breakpoint the pages sit in a menu, so the app bar fits a phone. -->
        <v-menu location="bottom end">
          <template #activator="{ props: menu }">
            <v-btn v-bind="menu" :icon="mdiMenu" variant="text" class="d-lg-none" aria-label="Pages" data-testid="nav-menu" />
          </template>
          <v-list density="compact" aria-label="Pages" nav>
            <v-list-item v-for="item in navItems" :key="item.title" :to="item.to" :prepend-icon="item.icon" :title="item.title" exact />
          </v-list>
        </v-menu>
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

    <v-footer color="background" border="t" class="text-body-small text-medium-emphasis justify-center text-center px-4">
      {{ ALWAYS_TRUE }} CPT® is a registered trademark of the American Medical Association; descriptions are shown as
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
