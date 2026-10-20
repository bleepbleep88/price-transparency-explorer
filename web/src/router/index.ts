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
