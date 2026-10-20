import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import type { IngestionPhase, IngestionStatus } from '@/features/rates/api/types'
import DataStatusChip from '../DataStatusChip.vue'

// The chip reads the shared live status; each test sets what the server last pushed before mounting.
const live = vi.hoisted(() => ({ status: { value: null as IngestionStatus | null }, progress: { value: null as number | null } }))
vi.mock('../../useDataStatus', () => ({ useDataStatus: () => live }))

function statusOf(phase: IngestionPhase, overrides: Partial<IngestionStatus> = {}): IngestionStatus {
  return {
    phase,
    message: 'Data is ready.',
    dataAvailable: true,
    currentFile: null,
    currentFileProgress: null,
    filesDone: 2,
    filesTotal: 2,
    issues: [],
    updatedAt: '2026-10-20T07:00:00+00:00',
    version: 12,
    ...overrides,
  }
}

let wrapper: VueWrapper | undefined

function mountChip(status: IngestionStatus, progress: number | null) {
  live.status.value = status
  live.progress.value = progress
  wrapper = mount(DataStatusChip, { global: { plugins: [createVuetify()] } })
  return wrapper
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

describe('DataStatusChip', () => {
  it('says statistics are being built once every rate file is saved', () => {
    const chip = mountChip(
      statusOf('buildingStatistics', { message: 'Building statistics for snapshot 2026-08-31: 600 of 1,631 codes…' }),
      1,
    )

    expect(chip.get('[data-testid="data-status"]').text()).toBe('Building statistics')
    expect(chip.get('[role="status"]').text()).toBe('Building statistics')
  })

  it('shows how far loading has got while rate files are read', () => {
    const chip = mountChip(statusOf('parsing', { dataAvailable: false, filesDone: 1, currentFileProgress: 0 }), 0.5)

    expect(chip.get('[data-testid="data-status"]').text()).toBe('Loading data 50%')
    expect(chip.get('[role="status"]').text()).toBe('Loading data')
  })
})
