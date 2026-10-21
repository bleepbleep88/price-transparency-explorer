import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import type { IngestionPhase, IngestionStatus } from '@/features/rates/api/types'
import DataStatusChip from '../DataStatusChip.vue'

// The chip reads the shared live status; each test sets what the server last pushed before mounting.
const live = vi.hoisted(() => ({
  status: { value: null as IngestionStatus | null },
  progress: { value: null as number | null },
  unreachable: { value: false },
}))
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

function mountChip(status: IngestionStatus | null, progress: number | null = null, unreachable = false) {
  live.status.value = status
  live.progress.value = progress
  live.unreachable.value = unreachable
  wrapper = mount(DataStatusChip, { attrs: { class: 'me-2' }, global: { plugins: [createVuetify()] } })
  return wrapper
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

describe('DataStatusChip', () => {
  it('is hidden once the data is ready, notes or not, and still tells screen readers', () => {
    const ready = mountChip(statusOf('ready'))
    expect(ready.find('[data-testid="data-status"]').exists()).toBe(false)
    expect(ready.get('[role="status"]').text()).toBe('Data ready')

    const notes = mountChip(statusOf('ready', { issues: [{ severity: 'warning', message: 'Essential Plan was only partly read.', source: null }] }))
    expect(notes.find('[data-testid="data-status"]').exists()).toBe(false)
  })

  it('shows how far loading has got while files are read', () => {
    const chip = mountChip(statusOf('parsing', { dataAvailable: false, filesDone: 1, currentFileProgress: 0 }), 0.5)

    expect(chip.get('[data-testid="data-status"]').text()).toBe('Loading data 50%')
    expect(chip.get('[data-testid="data-status"]').classes()).toContain('me-2')
    expect(chip.get('[role="status"]').text()).toBe('Loading data')
  })

  it('says comparisons are being prepared once every file is saved', () => {
    const chip = mountChip(statusOf('buildingStatistics', { message: 'Building statistics for snapshot 2026-08-31: 600 of 1,631 codes…' }), 1)

    expect(chip.get('[data-testid="data-status"]').text()).toBe('Preparing comparisons')
    expect(chip.get('[role="status"]').text()).toBe('Preparing comparisons')
  })

  it('shows when loading failed, when a file failed, and when the server cannot be reached', () => {
    expect(mountChip(statusOf('failed')).get('[data-testid="data-status"]').text()).toBe('Data failed to load')

    const failedFile = mountChip(statusOf('ready', { issues: [{ severity: 'error', message: "Ambetter's rate file couldn't be loaded.", source: null }] }))
    expect(failedFile.get('[data-testid="data-status"]').text()).toBe('Some data failed')
    expect(failedFile.get('[role="status"]').text()).toBe('Some data failed')

    expect(mountChip(null, null, true).get('[data-testid="data-status"]').text()).toBe("Can't reach the server")
    // Before the first answer, nothing flashes up.
    const waiting = mountChip(null)
    expect(waiting.find('[data-testid="data-status"]').exists()).toBe(false)
    expect(waiting.get('[role="status"]').text()).toBe('')
  })
})
