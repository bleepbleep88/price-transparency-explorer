import { expect, test } from '@playwright/test'

// Runs against the real API and data, like search.spec.ts. See playwright.config.ts for E2E_BASE_URL.

test('plans and employers lenses open the leaderboard and the comparison for their code, and both lead back to search', async ({ page }) => {
  // Employers: the code's ranked groups, then one of them in search with the same gap.
  await page.goto('/?code=45378&lens=employers')
  await page.getByTestId('lens-panel').getByTestId('lens-gaps-link').click()
  await expect(page).toHaveURL(/\/insights\/gaps\?code=CPT(\+|%20)45378(&|$)/)
  await expect(page.getByRole('heading', { name: 'Price gaps' })).toBeVisible()
  await expect(page.getByTestId('gaps-note')).toContainText('no claim counts')

  const ranked = page.getByTestId('gap-row').first()
  await expect(ranked).toContainText('CPT 45378')
  const gap = await ranked.getByTestId('gap-value').innerText()
  await ranked.getByTestId('gap-search').click()
  await expect(page).toHaveURL(/[?&]group=/)
  const picked = page.getByTestId('group-card').filter({ hasText: 'Showing these providers' })
  await expect(picked.getByTestId('quartile-gap')).toContainText(gap)

  // Plans: the comparison of the two products for the same code.
  await page.goto('/?code=45378&lens=plans')
  await page.getByTestId('lens-panel').getByTestId('lens-compare-link').click()
  await expect(page).toHaveURL(/\/insights\/compare\?code=CPT(\+|%20)45378(&|$)/)
  await expect(page.getByTestId('compare-same')).toContainText('pairs')
  await expect(page.getByTestId('ratio-chart').locator('svg')).toBeVisible()
  await expect(page.getByTestId('ratio-band')).toHaveCount(10)

  // An amount opens the search that shows exactly that rate.
  const pair = page.getByTestId('compare-pair').first()
  const amount = (await pair.getByTestId('pair-product-value').innerText()).replace(/ per unit$/, '').trim()
  await pair.getByTestId('pair-product-value').click()
  await expect(page).toHaveURL(/[?&]ein=/)
  await expect(page.getByTestId('row-rate')).toHaveText(amount)
})

test("an EIN's providers lens opens its scorecard, and a scorecard row reads as the search it opens", async ({ page }) => {
  await page.goto('/?code=99213')
  const rows = page.getByTestId('provider-row')
  await expect(rows.first()).toBeVisible()
  // A provider with a real tax ID: masked ones can't be typed back in.
  const provider = rows.filter({ hasNotText: 'Unverified tax ID' }).first()
  const ein = (await provider.getByTestId('row-ein').innerText()).replace(/^EIN\s+/, '').trim()
  expect(ein).toMatch(/^\d{2}-\d{7}$/)

  await page.goto(`/?code=99213&ein=${ein}&lens=providers`)
  await page.getByTestId('scorecard-link').click()
  await expect(page).toHaveURL(new RegExp(`/providers/${ein.replace('-', '')}$`))
  await expect(page.getByTestId('scorecard-ein')).toHaveText(`EIN ${ein}`)
  await expect(page.getByTestId('price-index')).toBeVisible()

  // The scorecard's row for 99213 shows what the search for 99213 with this EIN shows.
  await page.getByRole('textbox', { name: 'Filter by code, name, product or setting' }).fill('99213')
  const row = page.getByTestId('scorecard-row').filter({ hasText: 'CPT 99213' }).first()
  const rate = await row.getByTestId('scorecard-rate').innerText()
  const vsMedian = await row.getByTestId('scorecard-vs-median').innerText()
  await row.getByTestId('scorecard-search').click()
  await expect(page).toHaveURL(/[?&]group=/)
  await expect(page.getByTestId('row-rate')).toHaveText(rate)
  await expect(page.getByTestId('provider-table')).toContainText(vsMedian)
})
