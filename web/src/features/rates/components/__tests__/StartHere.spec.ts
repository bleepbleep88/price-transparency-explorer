import { describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import StartHere from '../StartHere.vue'

async function mountExamples(query: Record<string, string> = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', name: 'search', component: { template: '<div />' } }],
  })
  await router.push({ path: '/', query })
  await router.isReady()
  const wrapper = mount(StartHere, { global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), router] } })
  await flushPromises()
  return wrapper
}

describe('StartHere', () => {
  it('offers a few common services by plain name, each a search link', async () => {
    const wrapper = await mountExamples()
    const colonoscopy = wrapper.get('[data-testid="example-45378"]')
    expect(colonoscopy.text()).toBe('Colonoscopy')
    expect(colonoscopy.attributes('href')).toBe('/?code=45378')
    expect(wrapper.findAll('[data-testid^="example-"]').map((chip) => chip.text())).toEqual([
      'Colonoscopy',
      'Office visit',
      'ER visit',
      'Cataract surgery',
      'Knee scope',
      'Botox',
    ])
    // Plain names only: no codes on the chips.
    expect(wrapper.text()).not.toMatch(/\d{4,}/)
  })

  it('keeps the month picked in the app bar', async () => {
    const wrapper = await mountExamples({ snapshot: '2' })
    expect(wrapper.get('[data-testid="example-J0585"]').attributes('href')).toBe('/?code=J0585&snapshot=2')
  })
})
