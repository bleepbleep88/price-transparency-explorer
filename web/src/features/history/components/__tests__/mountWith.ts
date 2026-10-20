import { flushPromises, mount } from '@vue/test-utils'
import type { Component } from 'vue'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter, type LocationQueryRaw } from 'vue-router'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'

// Mounts a history component the way the app runs it: a router (search and changes pages), a fresh query client and
// Vuetify. Overlays (menus, tooltips) attach to the document body.
export async function mountWith(component: Component, props: Record<string, unknown> = {}, query: LocationQueryRaw = {}, path = '/') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'search', component: { template: '<div />' } },
      { path: '/changes', name: 'changes', component: { template: '<div />' } },
    ],
  })
  await router.push({ path, query })
  await router.isReady()
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrapper = mount(component, {
    props,
    attachTo: document.body,
    global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), router, [VueQueryPlugin, { queryClient }]] },
  })
  await flushPromises()
  return { wrapper, router }
}
