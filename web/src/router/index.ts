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
      path: '/sources',
      name: 'sources',
      component: () => import('@/views/SourcesView.vue'),
      meta: { title: 'Sources and data quality' },
    },
    {
      path: '/insights/gaps',
      name: 'gaps',
      component: () => import('@/views/GapLeaderboardView.vue'),
      meta: { title: 'Where prices differ most' },
    },
    {
      path: '/insights/compare',
      name: 'compare',
      component: () => import('@/views/ProductComparisonView.vue'),
      meta: { title: 'Plan comparison' },
    },
    {
      path: '/providers',
      name: 'providers',
      component: () => import('@/views/ProviderLookupView.vue'),
      meta: { title: 'Provider scorecards' },
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
