import { computed } from 'vue'
import { useRoute, useRouter, type LocationQuery } from 'vue-router'
import { formValuesFrom, mergeQuery, parseRouteQuery, validateSearchForm, type QueryPatch } from '../lib/searchParams'

/** The search lives in the URL. Reading parses route.query; every change is a router push (so Back works). */
export function useSearchRoute() {
  const route = useRoute()
  const router = useRouter()

  const params = computed(() => parseRouteQuery(route.query))
  const hasCode = computed(() => params.value.code !== '')

  // A pasted or hand-edited URL is validated like the form, so a bad link shows errors instead of firing a request.
  const urlErrors = computed(() => (hasCode.value ? validateSearchForm(formValuesFrom(params.value)) : {}))
  const canSearch = computed(() => hasCode.value && Object.keys(urlErrors.value).length === 0)

  /** Changing what's searched starts again at page 1 unless the patch sets a page itself. */
  function update(patch: QueryPatch) {
    const next = 'page' in patch ? patch : { ...patch, page: null }
    return router.push({ name: 'search', query: mergeQuery(route.query, next) })
  }

  /** Same as update, without a new history entry (for typing, e.g. the cases-a-year input). */
  function replace(patch: QueryPatch) {
    return router.replace({ name: 'search', query: mergeQuery(route.query, patch) })
  }

  /** A fresh search: everything goes except the lens, unless the patch sets one (Start-here cards do). */
  function startOver(code?: string, patch: QueryPatch = {}) {
    const keep: LocationQuery = params.value.lens ? { lens: params.value.lens } : {}
    return router.push({ name: 'search', query: mergeQuery(keep, { code, ...patch }) })
  }

  return { params, hasCode, urlErrors, canSearch, update, replace, startOver }
}
