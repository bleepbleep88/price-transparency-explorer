<script setup lang="ts">
import { computed } from 'vue'
import { ALWAYS_TRUE } from '@/lib/plainNumbers'
import { mdiMenu, mdiThemeLightDark } from '@mdi/js'
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

// Each item is current on its own pages: a scorecard (/providers/:key) belongs to Providers. Every page shows the month
// picked in the app bar, so links between pages keep it.
const { carried } = useSnapshotChoice()
const navItems = computed(() => [
  { title: 'Search', to: { name: 'search', query: carried.value }, names: ['search'] },
  { title: 'Price gaps', to: { name: 'gaps', query: carried.value }, names: ['gaps'] },
  { title: 'Compare plans', to: { name: 'compare', query: carried.value }, names: ['compare'] },
  { title: 'Providers', to: { name: 'providers', query: carried.value }, names: ['providers', 'scorecard'] },
  { title: 'Changes', to: { name: 'changes', query: carried.value }, names: ['changes'] },
])
const isCurrent = (item: { names: string[] }) => item.names.includes(String(route.name))
</script>

<template>
  <v-app>
    <v-app-bar flat border="b" density="comfortable">
      <v-app-bar-title>
        <router-link :to="{ name: 'search', query: carried }" class="brand">Price Explorer</router-link>
      </v-app-bar-title>

      <nav aria-label="Main" class="d-none d-md-flex ga-1">
        <v-btn
          v-for="item in navItems"
          :key="item.title"
          :to="item.to"
          :active="isCurrent(item)"
          :aria-current="isCurrent(item) ? 'page' : undefined"
          variant="text"
          class="px-3"
          :data-testid="`nav-${item.to.name}`"
        >
          {{ item.title }}
        </v-btn>
      </nav>

      <template #append>
        <!-- Sources lists every file, so there's no month to pick there. -->
        <SnapshotPicker v-if="route.name !== 'sources'" />
        <DataStatusChip class="me-2" />
        <!-- On a phone the pages sit in a menu. -->
        <v-menu location="bottom end">
          <template #activator="{ props: menu }">
            <v-btn v-bind="menu" :icon="mdiMenu" variant="text" class="d-md-none" aria-label="Pages" data-testid="nav-menu" />
          </template>
          <v-list density="compact" aria-label="Pages" nav>
            <v-list-item
              v-for="item in navItems"
              :key="item.title"
              :to="item.to"
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

    <v-footer color="background" border="t" class="app-footer">
      <span data-testid="always-true">{{ ALWAYS_TRUE }}</span>
      <router-link :to="{ name: 'sources', query: carried }" data-testid="nav-sources">Where the data comes from</router-link>
      <span class="cpt">CPT® is a trademark of the American Medical Association.</span>
    </v-footer>
  </v-app>
</template>

<style scoped>
.brand {
  color: inherit;
  text-decoration: none;
  font-weight: 600;
}

.app-footer {
  display: flex;
  flex: 0 0 auto;
  flex-wrap: wrap;
  align-content: center;
  align-items: center;
  justify-content: center;
  gap: 8px 24px;
  padding: 16px max(16px, env(safe-area-inset-left, 0px));
  font-size: 0.875rem;
  color: rgba(var(--v-theme-on-background), var(--v-medium-emphasis-opacity));
}

.app-footer a {
  color: rgb(var(--v-theme-primary));
}

.cpt {
  font-size: 0.75rem;
}
</style>
