// Typed fetch wrapper. Errors always come back as ApiError, built from the API's RFC 9457 problem responses.

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  code?: string
  traceId?: string
  errors?: Record<string, string[]>
}

const STATUS_TEXT: Record<number, string> = {
  0: "Can't reach the server. Check that the API is running.",
  400: 'Some inputs need fixing.',
  404: 'Not found.',
  500: 'Something went wrong on the server.',
  503: 'Data is still loading. Results will appear as soon as it finishes.',
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined
  readonly fieldErrors: Record<string, string[]>
  readonly traceId: string | undefined
  readonly retryAfterSeconds: number | undefined

  constructor(
    status: number,
    message: string,
    options: {
      code?: string
      fieldErrors?: Record<string, string[]>
      traceId?: string
      retryAfterSeconds?: number
    } = {},
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = options.code
    this.fieldErrors = options.fieldErrors ?? {}
    this.traceId = options.traceId
    this.retryAfterSeconds = options.retryAfterSeconds
  }

  get isLoading(): boolean {
    return this.status === 503 && this.code === 'index_loading'
  }
}

export type QueryValue = string | number | boolean | null | undefined | readonly (string | number)[]

/** Empty base = same origin (Vite proxy in dev, nginx in Docker). */
const baseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export function buildUrl(path: string, query?: Record<string, QueryValue>): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value === undefined || value === null || value === '') continue
    if (Array.isArray(value)) {
      for (const item of value) params.append(key, String(item))
    } else {
      params.append(key, String(value))
    }
  }
  const qs = params.toString()
  return `${baseUrl}${path}${qs ? `?${qs}` : ''}`
}

export async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {}
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // Non-JSON error body (e.g. a proxy page); fall back to the status text below.
  }
  const retryAfter = Number(response.headers.get('Retry-After'))
  return new ApiError(
    response.status,
    problem.detail ?? problem.title ?? STATUS_TEXT[response.status] ?? `Request failed (${response.status}).`,
    {
      code: problem.code,
      fieldErrors: problem.errors,
      traceId: problem.traceId,
      retryAfterSeconds: Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : undefined,
    },
  )
}

export async function requestJson<T>(
  path: string,
  options: { query?: Record<string, QueryValue>; signal?: AbortSignal } = {},
): Promise<T> {
  let response: Response
  try {
    response = await fetch(buildUrl(path, options.query), {
      headers: { Accept: 'application/json' },
      signal: options.signal,
    })
  } catch (error) {
    if (options.signal?.aborted) throw error
    throw new ApiError(0, STATUS_TEXT[0]!)
  }

  if (!response.ok) throw await toApiError(response)
  return (await response.json()) as T
}
