import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import StartHere from '../StartHere.vue'

function mountCards() {
  return mount(StartHere, { global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } })] } })
}

describe('StartHere', () => {
  it('has one card per audience, each saying what the explorer answers for them', () => {
    const wrapper = mountCards()
    for (const key of ['providers', 'plans', 'employers', 'innovators', 'life-sciences']) {
      expect(wrapper.find(`[data-testid="start-${key}"]`).exists()).toBe(true)
    }
    expect(wrapper.get('[data-testid="start-employers"]').text()).toContain('What a service typically costs')
  })

  it('starts a search with the lens set, and the NPI for the providers example', async () => {
    const wrapper = mountCards()
    await wrapper.get('[data-testid="start-providers-99213"]').trigger('click')
    await wrapper.get('[data-testid="start-employers-45378"]').trigger('click')
    expect(wrapper.emitted('start')).toEqual([
      ['providers', expect.objectContaining({ code: '99213', npi: '1801474036' })],
      ['employers', expect.objectContaining({ code: '45378' })],
    ])
  })

  it('links innovators to the API reference', () => {
    const hrefs = mountCards()
      .get('[data-testid="start-innovators"]')
      .findAll('a')
      .map((link) => link.attributes('href'))
    expect(hrefs).toEqual(expect.arrayContaining(['/scalar/v1', '/openapi/v1.json']))
  })
})
