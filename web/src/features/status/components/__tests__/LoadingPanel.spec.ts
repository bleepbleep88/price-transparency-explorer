import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import type { IngestionPhase, IngestionStatus } from '@/features/rates/api/types'
import LoadingPanel from '../LoadingPanel.vue'

const live = vi.hoisted(() => ({ status: { value: null as IngestionStatus | null }, progress: { value: null as number | null } }))
vi.mock('../../useDataStatus', () => ({ useDataStatus: () => live }))

function statusOf(phase: IngestionPhase): IngestionStatus {
  return {
    phase,
    message: 'Reading Essential Plan rates',
    dataAvailable: false,
    currentFile: null,
    currentFileProgress: null,
    filesDone: 1,
    filesTotal: 6,
    issues: [],
    updatedAt: '2026-10-20T07:00:00+00:00',
    version: 3,
  }
}

let wrapper: VueWrapper | undefined

function mountPanel(status: IngestionStatus | null, progress: number | null) {
  live.status.value = status
  live.progress.value = progress
  wrapper = mount(LoadingPanel, { global: { plugins: [createVuetify()] } })
  return wrapper
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

describe('LoadingPanel', () => {
  it('is one line and a bar', () => {
    const panel = mountPanel(statusOf('parsing'), 0.42)
    expect(panel.get('.loading-line').text()).toBe('Loading prices 42%')
    expect(panel.find('[role="progressbar"]').exists()).toBe(true)
    expect(panel.text()).not.toContain('Essential Plan')

    expect(mountPanel(null, null).get('.loading-line').text()).toBe('Loading prices')
    expect(mountPanel(statusOf('buildingStatistics'), null).get('.loading-line').text()).toBe('Preparing comparisons')
  })
})
