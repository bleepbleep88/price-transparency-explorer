import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, buildUrl, requestJson } from '../apiClient'

function jsonResponse(body: unknown, init: ResponseInit & { headers?: Record<string, string> } = {}) {
  return new Response(JSON.stringify(body), {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init.headers },
  })
}

describe('buildUrl', () => {
  it('skips empty values and repeats array params', () => {
    expect(buildUrl('/api/v1/rates', { code: '29881', npi: '', ein: undefined, setting: ['24', '22'] })).toBe(
      '/api/v1/rates?code=29881&setting=24&setting=22',
    )
  })
})

describe('requestJson', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('returns parsed JSON on success', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ ok: true })))
    await expect(requestJson<{ ok: boolean }>('/api/v1/status')).resolves.toEqual({ ok: true })
  })

  it('maps validation problems to field errors', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse(
          { title: 'Some search inputs need fixing', status: 400, code: 'validation_failed', errors: { npi: ['Bad'] }, traceId: 't-1' },
          { status: 400 },
        ),
      ),
    )
    const error = await requestJson('/api/v1/rates').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 400, code: 'validation_failed', fieldErrors: { npi: ['Bad'] }, traceId: 't-1' })
  })

  it('flags 503 index_loading as a loading state and reads Retry-After', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({ title: 'Data is still loading', status: 503, code: 'index_loading' }, { status: 503, headers: { 'Retry-After': '5' } }),
      ),
    )
    const error = (await requestJson('/api/v1/rates').catch((e: unknown) => e)) as ApiError
    expect(error.isLoading).toBe(true)
    expect(error.retryAfterSeconds).toBe(5)
  })

  it('turns network failures into status 0 errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    const error = (await requestJson('/api/v1/rates').catch((e: unknown) => e)) as ApiError
    expect(error.status).toBe(0)
  })
})
