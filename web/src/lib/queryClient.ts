import { QueryClient } from '@tanstack/vue-query'

// Rate data only changes when the server finishes loading a snapshot, and every query key carries the
// snapshot id, so cached results never go stale on their own. No automatic retries: errors show a
// "Try again" action, and "still loading" (503) waits for the server's ready event instead of a timer.
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: Number.POSITIVE_INFINITY,
      retry: false,
      refetchOnWindowFocus: false,
    },
  },
})
