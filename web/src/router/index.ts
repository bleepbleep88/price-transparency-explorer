import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'search',
      component: () => import('@/views/SearchView.vue'),
      meta: { title: 'Search rates' },
    },
    {
      path: '/changes',
      name: 'changes',
      component: () => import('@/views/ChangesView.vue'),
      meta: { title: 'Rate changes' },
    },
    {
      path: '/sources',
      name: 'sources',
      component: () => import('@/views/SourcesView.vue'),
      meta: { title: 'Where the data comes from' },
    },
    {
      path: '/insights/gaps',
      name: 'gaps',
      component: () => import('@/views/GapLeaderboardView.vue'),
      meta: { title: 'Price gaps' },
    },
    {
      path: '/insights/compare',
      name: 'compare',
      component: () => import('@/views/ProductComparisonView.vue'),
      meta: { title: 'Compare plans' },
    },
    {
      path: '/providers',
      name: 'providers',
      component: () => import('@/views/ProviderLookupView.vue'),
      meta: { title: 'Providers' },
    },
    {
      path: '/providers/:key',
      name: 'scorecard',
      component: () => import('@/views/ProviderScorecardView.vue'),
      meta: { title: 'Provider scorecard' },
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/views/NotFoundView.vue'),
      meta: { title: 'Page not found' },
    },
  ],
})

router.afterEach((to) => {
  const title = to.meta.title as string | undefined
  document.title = title ? `${title} · Price Transparency Explorer` : 'Price Transparency Explorer'
})

export default router
