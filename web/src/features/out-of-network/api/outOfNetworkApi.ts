import { requestJson } from '@/lib/apiClient'
import type { OutOfNetworkResult } from './types'

export const outOfNetworkApi = {
  /** A code's out-of-network payments per product and fee type; the code is read the way search reads it. */
  forCode: (code: string, codeType: string | undefined, snapshot: number | undefined, signal?: AbortSignal) =>
    requestJson<OutOfNetworkResult>('/api/v1/out-of-network', { query: { code, codeType, snapshot }, signal }),
}
