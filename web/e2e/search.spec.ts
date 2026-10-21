import { expect, test, type Page } from '@playwright/test'

// These flows run against the real API and data. See playwright.config.ts for E2E_BASE_URL.

async function searchFor(page: Page, text: string) {
  await page.getByRole('combobox', { name: 'Service or code' }).fill(text)
  await page.getByRole('button', { name: 'Search', exact: true }).click()
}

test('knee arthroscopy (29881) shows the surgery center bill, with how the plan filed it under Details', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'What does it cost?' })).toBeVisible()

  await searchFor(page, '29881')
  await expect(page).toHaveURL(/[?&]code=29881(&|$)/)
  await expect(page.getByTestId('service-title')).toHaveText('Knee scope')
  await expect(page.getByTestId('price-tile').filter({ hasText: 'Surgery center' }).first()).toBeVisible()
  await expect(page.getByTestId('place-row').first()).toBeVisible()

  // Revenue codes stay off the main view; Details says how the plan listed it.
  await expect(page.getByTestId('hint-code_facility_only')).toBeHidden()
  await page.getByTestId('search-details-toggle').click()
  await expect(page.getByTestId('hint-code_facility_only')).toContainText('revenue code 0490')
})

test('a name typed without picking a suggestion searches the best match', async ({ page }) => {
  await page.goto('/')
  const box = page.getByRole('combobox', { name: 'Service or code' })
  await box.fill('colonoscopy')
  await expect(page.getByRole('option').first()).toBeVisible()
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  // The plain name wins: Colonoscopy (45378), not the busier 45380 with biopsy.
  await expect(page).toHaveURL(/[?&]code=45378(&|$)/)
  await expect(page.getByTestId('price-tile').first()).toBeVisible()
})

test('words people use find the service: botox, then mri', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('combobox', { name: 'Service or code' }).fill('botox')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page).toHaveURL(/[?&]code=J0585(&|$)/)
  await expect(page.getByTestId('service-title')).toHaveText('Botox')
  // The tile is never the thrown-away-drug price unless that's all there is, and then it says so.
  for (const tile of await page.getByTestId('price-tile').allInnerTexts()) {
    if (/JW|discarded/i.test(tile)) expect(tile).toContain('for drug thrown away')
  }

  await page.getByRole('combobox', { name: 'Service or code' }).fill('mri')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page).not.toHaveURL(/[?&]code=MRI(&|$)/i)
  await expect(page.getByTestId('price-tile').first()).toBeVisible()
})

test('a tax ID under Filters shows where that provider stands on its bill', async ({ page, request }) => {
  // The first provider with a real tax ID: masked ones can't be typed back in.
  const response = await request.get('/api/v1/rates?code=99213&pageSize=50')
  const body = (await response.json()) as { rows: { items: { ein: string | null; einMasked: boolean }[] } }
  const ein = body.rows.items.find((row) => row.ein && !row.einMasked)!.ein!
  const display = `${ein.slice(0, 2)}-${ein.slice(2)}`

  await page.goto('/?code=99213')
  await expect(page.getByTestId('price-tile').first()).toBeVisible()
  await page.getByTestId('filters-toggle').click()
  await page.getByRole('textbox', { name: 'Tax ID (EIN)' }).fill(display)
  await page.getByRole('button', { name: 'Search', exact: true }).click()

  await expect(page).toHaveURL(new RegExp(`[?&]ein=${display}(&|$)`))
  await expect(page.getByTestId('active-filter').filter({ hasText: `Tax ID ${display}` })).toBeVisible()
  await expect(page.getByTestId('tile-yours').first()).toContainText('Yours: $')
})

test('a Manhattan location filter on 45378 keeps only places there', async ({ page }) => {
  await page.goto('/?code=45378')
  await expect(page.getByTestId('place-row').first()).toBeVisible()

  await page.getByTestId('filters-toggle').click()
  await page.getByRole('combobox', { name: 'Location' }).click()
  await page.getByRole('option', { name: /^Manhattan/ }).click()
  await page.getByRole('button', { name: 'Search', exact: true }).click()

  await expect(page).toHaveURL(/[?&]county=Manhattan(&|$)/)
  await expect(page.getByTestId('active-filter').filter({ hasText: 'Manhattan' })).toBeVisible()
  // The server narrows the rows; once they arrive, every place listed is in Manhattan.
  await expect
    .poll(async () => {
      const places = await page.getByTestId('place-where').allInnerTexts()
      return places.length > 0 && places.every((place) => place === 'Manhattan')
    })
    .toBe(true)
  // Copper marks only a provider searched by NPI, EIN or name, never a location's matches.
  await expect(page.locator('.copper-mark')).toHaveCount(0)
  await page.getByTestId('search-details-toggle').click()
  await expect(page.getByTestId('search-details')).toContainText('national provider registry (NPPES, October 2026)')
})

// Both ways to finish: click Search (the field loses focus and keeps the typed place), or press Enter in the field.
const finishBy = {
  'then Search': (page: Page) => page.getByRole('button', { name: 'Search', exact: true }).click(),
  'then Enter': (page: Page) => page.getByRole('combobox', { name: 'Location' }).press('Enter'),
}

for (const { typed, finish } of [
  { typed: 'Brooklyn', finish: 'then Search' },
  { typed: 'Brookl', finish: 'then Enter' },
  { typed: 'Kings', finish: 'then Enter' },
] as const) {
  test(`a location typed as "${typed}" (${finish}) searches Brooklyn`, async ({ page }) => {
    await page.goto('/?code=45378')
    await expect(page.getByTestId('place-row').first()).toBeVisible()

    await page.getByTestId('filters-toggle').click()
    const location = page.getByRole('combobox', { name: 'Location' })
    await location.click()
    await location.pressSequentially(typed)
    // "Kings" is Brooklyn's county: the option shows that name too.
    await expect(page.getByRole('option', { name: /^Brooklyn/ })).toBeVisible()
    await finishBy[finish](page)

    await expect(page).toHaveURL(/[?&]county=Brooklyn(&|$)/)
    await expect(page.getByTestId('active-filter').filter({ hasText: 'Brooklyn' })).toBeVisible()
  })
}

test('picking the other plan and another bill keeps both in the address', async ({ page }) => {
  await page.goto('/?code=45378')
  await expect(page.getByTestId('price-tile').first()).toBeVisible()
  await page.getByTestId('plan-tabs').getByRole('button', { name: 'Ambetter' }).click()
  await expect(page).toHaveURL(/[?&]plan=ambetter-from-fidelis-care(&|$)/)
  await expect(page.getByTestId('price-tile').filter({ hasText: 'Surgery center' })).toContainText('$569')

  await page.getByTestId('order-priciest').click()
  await expect(page).toHaveURL(/[?&]sort=-rate(&|$)/)
  await expect(page.getByTestId('place-row').first()).toContainText('$1,154')
})
