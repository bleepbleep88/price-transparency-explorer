<script setup lang="ts">
import { computed } from 'vue'
import { ALWAYS_TRUE } from '@/lib/plainNumbers'
import { mdiAccountSearchOutline, mdiChartBar, mdiCompareHorizontal, mdiDatabaseOutline, mdiMagnify, mdiMenu, mdiThemeLightDark } from '@mdi/js'
import { useRoute } from 'vue-router'
import { useTheme } from 'vuetify'
import DataStatusChip from '@/features/status/components/DataStatusChip.vue'
import { useDataStatus } from '@/features/status/useDataStatus'
import SnapshotPicker from '@/features/history/components/SnapshotPicker.vue'
import { useSnapshotChoice } from '@/features/history/composables/useSnapshotChoice'

const theme = useTheme()
const route = useRoute()

// Start the live status stream once for the whole app.
useDataStatus()

// Each item is current on its own pages: a scorecard (/providers/:key) belongs to Provider scorecards. Every page shows
// the snapshot picked in the app bar, so links between pages keep it.
const { carried } = useSnapshotChoice()
const navItems = computed(() => [
  { title: 'Search', to: { name: 'search', query: carried.value }, icon: mdiMagnify, names: ['search'] },
  { title: 'Price differences', to: { name: 'gaps', query: carried.value }, icon: mdiChartBar, names: ['gaps'] },
  { title: 'Plan comparison', to: { name: 'compare', query: carried.value }, icon: mdiCompareHorizontal, names: ['compare'] },
  { title: 'Provider scorecards', to: { name: 'providers', query: carried.value }, icon: mdiAccountSearchOutline, names: ['providers', 'scorecard'] },
  { title: 'Sources', to: { name: 'sources', query: carried.value }, icon: mdiDatabaseOutline, names: ['sources'] },
])
const isCurrent = (item: { names: string[] }) => item.names.includes(String(route.name))
</script>

<template>
  <v-app>
    <v-app-bar flat border="b" density="comfortable">
      <v-app-bar-title>
        <router-link :to="{ name: 'search', query: carried }" class="brand">Price Transparency Explorer</router-link>
      </v-app-bar-title>

      <nav aria-label="Main" class="d-none d-lg-flex ga-1">
        <v-btn
          v-for="item in navItems"
          :key="item.title"
          :to="item.to"
          :prepend-icon="item.icon"
          :active="isCurrent(item)"
          :aria-current="isCurrent(item) ? 'page' : undefined"
          variant="text"
          :data-testid="`nav-${item.to.name}`"
        >
          {{ item.title }}
        </v-btn>
      </nav>

      <template #append>
        <SnapshotPicker />
        <DataStatusChip class="me-2" />
        <!-- Below the large breakpoint the pages sit in a menu, so the app bar fits a phone. -->
        <v-menu location="bottom end">
          <template #activator="{ props: menu }">
            <v-btn v-bind="menu" :icon="mdiMenu" variant="text" class="d-lg-none" aria-label="Pages" data-testid="nav-menu" />
          </template>
          <v-list density="compact" aria-label="Pages" nav>
            <v-list-item
              v-for="item in navItems"
              :key="item.title"
              :to="item.to"
              :prepend-icon="item.icon"
              :title="item.title"
              :active="isCurrent(item)"
              :aria-current="isCurrent(item) ? 'page' : undefined"
            />
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
