import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
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

  function startOver(code?: string) {
    return router.push({ name: 'search', query: code ? { code } : {} })
  }

  return { params, hasCode, urlErrors, canSearch, update, startOver }
}
