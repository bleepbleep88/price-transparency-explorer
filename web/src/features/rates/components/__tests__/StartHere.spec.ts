import { describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { createMemoryHistory, createRouter } from 'vue-router'
import StartHere from '../StartHere.vue'

async function mountCards() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', name: 'search', component: { template: '<div />' } }],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(StartHere, { global: { plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), router] } })
  await flushPromises()
  return wrapper
}

describe('StartHere', () => {
  it('has one card per audience, each saying what the explorer answers for them', async () => {
    const wrapper = await mountCards()
    for (const key of ['providers', 'plans', 'employers', 'innovators', 'life-sciences']) {
      expect(wrapper.find(`[data-testid="start-${key}"]`).exists()).toBe(true)
    }
    expect(wrapper.get('[data-testid="start-employers"]').text()).toContain('What a service typically costs')
  })

  it('makes every example a link that sets the lens (and the NPI for the providers example)', async () => {
    const wrapper = await mountCards()
    const providers = wrapper.get('[data-testid="start-providers-99213"]')
    expect(providers.element.tagName).toBe('A')
    expect(providers.attributes('href')).toBe('/?code=99213&lens=providers&npi=1801474036')
    expect(providers.attributes('role')).toBe('link')
    expect(providers.element.parentElement?.getAttribute('role')).toBe('listitem')
    expect(wrapper.get('[data-testid="start-employers-45378"]').attributes('href')).toBe('/?code=45378&lens=employers')
  })

  it('links innovators to the API reference', async () => {
    const hrefs = (await mountCards())
      .get('[data-testid="start-innovators"]')
      .findAll('a')
      .map((link) => link.attributes('href'))
    expect(hrefs).toEqual(expect.arrayContaining(['/scalar/v1', '/openapi/v1.json']))
  })
})
