import { expect, test, type Page } from '@playwright/test'

// Runs against the real API and data, like search.spec.ts. See playwright.config.ts for E2E_BASE_URL.

// Decision 89: plain numbers, never statistics words.
async function expectPlainWords(page: Page) {
  await expect(page.locator('main')).not.toContainText(/percentile|median|quartile|×|÷|price index/i)
  // "Times" only from twice, and nothing ranked without a difference.
  await expect(page.locator('main')).not.toContainText(/about 1(\.\d)? times|\$0\.00/)
}

// Decision 90: no code types, modifiers or tax IDs on the main view; they live in Details (closed here).
async function expectNothingTechnicalUpFront(page: Page) {
  const text = await page.locator('main').evaluate((main) => {
    const view = main.cloneNode(true) as HTMLElement
    view.querySelectorAll('[data-testid$="details"]').forEach((details) => details.remove())
    return view.textContent ?? ''
  })
  expect(text).not.toMatch(/\b(CPT|HCPCS|EIN|NPIs?)\b|\bRC \d|modifier|tax ID/i)
}

test('price gaps rank plain-named services, follow a code from a link, and open the search for a row', async ({ page }) => {
  // A search's link arrives with the code; it shows as a removable chip named in plain words.
  await page.goto('/insights/gaps?code=CPT%2045378')
  await expect(page.getByRole('heading', { name: 'Price gaps', level: 1 })).toBeVisible()
  await expect(page.getByTestId('gaps-code-chip')).toHaveText('Colonoscopy')
  const row = page.getByTestId('gap-row').first()
  await expect(row).toContainText('Colonoscopy')
  await expect(row.getByTestId('gap-value')).toHaveText(/^\$[\d,.]+$/)
  await expectPlainWords(page)
  await expectNothingTechnicalUpFront(page)

  // The codes are in Details.
  await page.getByTestId('gaps-details-toggle').click()
  await expect(page.getByTestId('gaps-details')).toContainText('CPT 45378')
  await expect(page.getByTestId('gaps-details')).toContainText("don't say how often a service happens")

  // One plan at a time, like search; "Both plans" names the plan on each row.
  await expect(page.getByTestId('gaps-plans')).toBeVisible()
  await expect(row).not.toContainText(/Essential Plan|Ambetter/)
  await page.getByTestId('gaps-plan-both').click()
  await expect(page).toHaveURL(/[?&]product=both(&|$)/)
  await expect(page.getByTestId('gap-row').first()).toContainText(/Essential Plan|Ambetter/)

  // The widest range uses its own columns, and the order lives in the address.
  await page.getByTestId('sort-range').click()
  await expect(page).toHaveURL(/[?&]sort=range(&|$)/)
  await expect(page.getByTestId('gaps-table').locator('th').last()).toHaveText('Spread')

  // A row's name opens the search for that exact service, place and plan.
  await page.getByTestId('gap-row').first().getByTestId('gap-search').click()
  await expect(page).toHaveURL(/[?&]group=/)
  await expect(page).toHaveURL(/[?&]code=45378(&|$)/)

  // A stale page number says it's past the end and offers the way back.
  await page.goto('/insights/gaps?page=99')
  await expect(page.getByTestId('gaps-no-data')).toContainText('Past the last page.')
  await page.getByTestId('gaps-first-page').click()
  await expect(page.getByTestId('gap-row').first()).toBeVisible()
  await expect(page.getByTestId('nav-gaps')).toHaveAttribute('aria-current', 'page')
})

test('compare plans leads with how often the price is the same, then the biggest differences', async ({ page }) => {
  await page.goto('/insights/compare?code=CPT%2045378')
  await expect(page.getByRole('heading', { name: 'Compare plans', level: 1 })).toBeVisible()
  await expect(page.getByTestId('compare-same')).toContainText(/Same price in both plans\s*\d+%\s*of [\d,]+ prices?/)
  await expect(page.getByTestId('compare-pair').first()).toBeVisible()
  await expectPlainWords(page)
  await expectNothingTechnicalUpFront(page)

  // The breakdown sits in Details.
  await page.getByTestId('compare-details-toggle').click()
  await expect(page.getByTestId('ratio-band')).toHaveCount(10)
  await expect(page.getByTestId('compare-details')).toContainText('EIN')

  // An amount opens the search that shows exactly that rate for that place.
  await page.getByTestId('compare-pair').first().getByTestId('pair-product-value').click()
  await expect(page).toHaveURL(/[?&]ein=/)
  await expect(page).toHaveURL(/[?&]group=/)

  // Under 5 places there's no share to give: each price is listed instead.
  await page.goto('/insights/compare?category=other')
  await expect(page.getByTestId('compare-few')).toContainText(/Only [1-4] places?/)
  await expect(page.getByTestId('compare-same')).toHaveCount(0)
  await expect(page.getByTestId('compare-pair').first()).toBeVisible()
})

test('a provider found by name opens its scorecard: one reading, above and below, and the full list on request', async ({ page }) => {
  await page.goto('/providers')
  await page.getByTestId('provider-lookup').locator('input').fill('United Health Services')
  await page.getByRole('option').first().click()
  await expect(page).toHaveURL(/\/providers\/[\w-]+$/)
  await expect(page.getByTestId('nav-providers')).toHaveAttribute('aria-current', 'page')

  await expect(page.getByTestId('scorecard-overall')).toContainText(/(Usually|Overall)\s*(\d+% (above|below) typical|About typical|About [\d.]+ times typical|Too few to say)/)
  await expect(page.getByTestId('place-row').first()).toBeVisible()
  await expectPlainWords(page)
  await expectNothingTechnicalUpFront(page)

  // The tax ID is in Details.
  await page.getByTestId('scorecard-details-toggle').click()
  await expect(page.getByTestId('scorecard-ein')).toHaveText(/^(EIN|Tax ID) /)

  // The full list opens on request; its filter lives in the address and finds plain names.
  await page.getByTestId('scorecard-see-all').click()
  await page.getByTestId('scorecard-filter').locator('input').fill('office visit')
  await expect(page).toHaveURL(/[?&]q=office(\+|%20)visit(&|$)/)
  const row = page.getByTestId('scorecard-row').first()
  await expect(row).toContainText('Office visit')
  await row.getByTestId('scorecard-search').click()
  await expect(page).toHaveURL(/[?&]group=/)
  await expect(page).toHaveURL(/[?&]ein=/)
})

test('a scorecard reached under another of its names says so, and an unknown provider gets a way back', async ({ page }) => {
  // NYU's faculty practice has two names of its own; a search row under the second links here with it.
  await page.goto(`/providers/135562308?as=${encodeURIComponent('NYU dba NYU Faculty Group Practice')}`)
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('NYU School of Medicine Faculty')
  await expect(page.getByTestId('scorecard-also')).toHaveText('Also listed as NYU Faculty Group Practice')
  // A practice listed under someone else's name shows its own on both the search row and the scorecard.
  await page.goto('/providers/113466729')
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Gastrointestinal Care of Long Island')
  // A name that isn't one of its own is never shown.
  await page.goto('/providers/135562308?as=Someone%20Else')
  await expect(page.getByTestId('scorecard-also')).not.toContainText('Someone Else')

  await page.goto('/providers/999999999')
  await expect(page.getByRole('heading', { name: 'No such provider', level: 1 })).toBeVisible()
  await page.getByTestId('scorecard-find').click()
  await expect(page).toHaveURL(/\/providers$/)
})
