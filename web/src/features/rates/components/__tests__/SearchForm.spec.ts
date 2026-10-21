import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { parseRouteQuery } from '../../lib/searchParams'
import SearchForm from '../SearchForm.vue'

function mountForm(query: Record<string, string>) {
  return mount(SearchForm, {
    props: { params: parseRouteQuery(query), products: [], settings: [] },
    global: {
      plugins: [createVuetify({ theme: { defaultTheme: 'light' } }), [VueQueryPlugin, { queryClient: new QueryClient() }]],
    },
  })
}

describe('SearchForm', () => {
  it('keeps unsearched edits when only the lens or the case count changes', async () => {
    const wrapper = mountForm({ code: '99213' })
    // The NPI sits under Filters.
    await wrapper.get('[data-testid="filters-toggle"]').trigger('click')
    const npi = wrapper.get('[data-testid="npi-input"] input')
    await npi.setValue('1801474036')

    await wrapper.setProps({ params: parseRouteQuery({ code: '99213', lens: 'providers' }) })
    expect((npi.element as HTMLInputElement).value).toBe('1801474036')
    await wrapper.setProps({ params: parseRouteQuery({ code: '99213', lens: 'employers', cases: '12', page: '2' }) })
    expect((npi.element as HTMLInputElement).value).toBe('1801474036')

    // A different search does reset the form.
    await wrapper.setProps({ params: parseRouteQuery({ code: '45378', lens: 'employers' }) })
    expect((npi.element as HTMLInputElement).value).toBe('')
  })

  it('leaves a collapsed filter panel collapsed when the lens changes', async () => {
    const wrapper = mountForm({ code: '45378', product: 'ambetter' })
    const panel = () => wrapper.get('.v-expansion-panel')
    expect(panel().classes()).toContain('v-expansion-panel--active')

    await wrapper.get('.v-expansion-panel-title').trigger('click')
    expect(panel().classes()).not.toContain('v-expansion-panel--active')

    await wrapper.setProps({ params: parseRouteQuery({ code: '45378', product: 'ambetter', lens: 'plans' }) })
    expect(panel().classes()).not.toContain('v-expansion-panel--active')
  })
})
