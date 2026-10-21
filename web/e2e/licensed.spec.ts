import { expect, test } from '@playwright/test'

// No CPT or CDT text on screen or from the API: we have no license for the AMA's or the ADA's descriptions. A service
// shows our own plain name, else CMS's RBCS name with its code. Against the real API and data (see playwright.config.ts).

const cpt = /^\d{4}[0-9A-Z]$/

test("a CPT code shows our own name, else CMS's category name with its code", async ({ page }) => {
  await page.goto('/?code=45378')
  await expect(page.getByTestId('service-title')).toHaveText('Colonoscopy')
  await page.goto('/?code=83520')
  await expect(page.getByTestId('service-title')).toHaveText('Immunoassay (83520)')
})

test("the API never returns a CPT code's published text", async ({ request }) => {
  for (const code of ['45378', '00731', '99213', '83520']) {
    const result = await (await request.get(`/api/v1/rates?code=${code}`)).json()
    expect(result.code.descriptionSource, code).toBe('rbcs')
    expect(result.code.description, code).toBeNull()
    // Published CPT text in these files is upper case; RBCS names and our own aren't.
    expect(result.code.name, code).toMatch(/[a-z]/)
  }
  const suggestions: { code: string; name: string | null; description: string | null }[] = await (
    await request.get('/api/v1/codes?q=colon')
  ).json()
  expect(suggestions.length).toBeGreaterThan(0)
  for (const item of suggestions.filter((suggestion) => cpt.test(suggestion.code))) {
    expect(item.description, item.code).toBeNull()
    expect(item.name ?? 'none', item.code).toMatch(/[a-z]/)
  }
})
