import { expect, test, type Locator } from '@playwright/test'

// Runs against the real API and data, like search.spec.ts. See playwright.config.ts for E2E_BASE_URL.

// Decision 89 and 90: no statistics words anywhere, and no codes, modifiers, tax IDs, NPIs, hashes or file names on
// the main view (Details may have them).
const STATISTICS = /percentile|median|quartile|×|p90|p10|\bindex\b|typical|payment/i
const NOT_ON_MAIN_VIEW = /\bRC\b|CPT|HCPCS|modifier|tax ID|\bEINs?\b|\bNPIs?\b|SHA-256|ETag|snapshot|\.json/i

/** What a reader sees with every Details panel closed. */
async function mainViewText(scope: Locator): Promise<string> {
  return scope.evaluate((element) => {
    const copy = element.cloneNode(true) as HTMLElement
    copy.querySelectorAll('.details-body').forEach((body) => body.remove())
    return copy.innerText || copy.textContent || ''
  })
}

test('the sources page leads with one total, then each month with its two plans and their files', async ({ page }) => {
  await page.goto('/')
  await page.getByTestId('nav-sources').click()
  await expect(page).toHaveURL(/\/sources/)
  await expect(page.getByRole('heading', { name: 'Where the data comes from', level: 1 })).toBeVisible()

  // The newest month's rate files: every published price, and what's left once exact copies count once.
  await expect(page.getByTestId('sources-total')).toContainText(/^[\d,]+ prices → [\d,]+ after removing copies/)
  await expect(page.getByTestId('sources-total')).toContainText(/[A-Z][a-z]{2} \d{1,2}, \d{4} files/)

  const months = page.getByTestId('source-snapshot')
  await expect(months).toHaveCount(3)
  await expect(months.filter({ hasText: 'Latest' })).toHaveCount(1)
  const newest = months.first()
  await expect(newest).toContainText('Latest')
  const plans = newest.getByTestId('source-plan')
  await expect(plans).toHaveCount(2)
  await expect(plans.first().getByTestId('source-link-prices')).toHaveAttribute('href', /in-network\.json$/)
  await expect(plans.first().getByTestId('source-link-oon')).toHaveAttribute('href', /allowed-amounts\.json$/)
  // Each link downloads the whole file (the rate files run 85 to 175 MB), so it says how big it is.
  await expect(plans.first().getByTestId('source-link-prices')).toHaveText(/^Prices · \d+ MB\s*$/)
  await expect(plans.first().getByTestId('source-link-oon')).toHaveText(/^Out of network · [\d,]+ (KB|MB)\s*$/)
  await expect(page.getByTestId('source-plan-problem')).toHaveCount(0)
  // The month picker has nothing to pick here, and the status chip is gone once the data is ready.
  await expect(page.getByTestId('data-status')).toHaveCount(0)

  const main = await mainViewText(page.locator('main'))
  expect(main).not.toMatch(STATISTICS)
  expect(main).not.toMatch(NOT_ON_MAIN_VIEW)

  // Details: each of the month's four files, what it held and how to trace it.
  await newest.getByTestId('details-toggle').click()
  const files = newest.getByTestId('source-file')
  await expect(files).toHaveCount(4)
  await expect(files.filter({ hasText: 'In-network rates' })).toHaveCount(2)
  const outOfNetwork = files.filter({ hasText: 'Out-of-network allowed amounts' })
  await expect(outOfNetwork).toHaveCount(2)
  await expect(outOfNetwork.first()).toContainText(/[\d,]+ amounts in the file became [\d,]+ different amounts after removing exact copies\./)
  await expect(outOfNetwork.first().getByRole('link', { name: 'Open the file' })).toHaveAttribute('href', /allowed-amounts\.json$/)
  await expect(files.first().getByRole('link', { name: 'Open the file' })).toHaveAttribute('href', /in-network\.json$/)
  await expect(files.first()).toContainText(/SHA-256 [0-9a-f]{64}/)
  expect(await page.locator('main').innerText()).not.toMatch(STATISTICS)

  await page.getByTestId('cleaning-rules-toggle').click()
  await expect(page.getByTestId('cleaning-rules')).toContainText('Exact copies are counted once.')
})

test('a code with out-of-network amounts gets one line, with every bill in Details; a code without gets no panel', async ({ page }) => {
  // Revenue code 0450 (emergency room): both products' allowed-amount files list ER facility amounts.
  await page.goto('/?code=0450')
  const panel = page.getByTestId('oon-panel')
  await expect(panel.getByRole('heading', { name: 'Out of network' })).toBeVisible()
  await expect(panel.getByTestId('oon-line')).toHaveText(
    /^(Providers usually billed (about [\d.]+ times|about \d+% (more|less) than|about the same as) what the plan allowed\.|The plan allowed \$[\d,.]+(\s–\s\$[\d,.]+)?\.|Only \d+ places?: the plan allowed .+\.)$/,
  )
  const main = await mainViewText(panel)
  expect(main).not.toMatch(STATISTICS)
  expect(main).not.toMatch(NOT_ON_MAIN_VIEW)
  expect(main).not.toContain('0450')

  await panel.getByTestId('oon-details-toggle').click()
  const rows = panel.getByTestId('oon-row')
  await expect(rows.first()).toBeVisible()
  await expect(rows.first()).toContainText('Facility fee')
  await expect(panel.getByTestId('oon-notes')).toContainText("The files don't say whether an amount covers one visit or several.")
  await expect(panel.getByTestId('oon-notes')).toContainText('20 or more claims')
  expect(await panel.innerText()).not.toMatch(STATISTICS)

  // A code nobody was paid for out of network has no panel.
  const answered = page.waitForResponse((response) => response.url().includes('/api/v1/out-of-network') && response.url().includes('99213'))
  await page.goto('/?code=99213')
  expect(((await (await answered).json()) as { groups: unknown[] }).groups).toEqual([])
  await expect(page.getByTestId('oon-panel')).toHaveCount(0)
})

test('the home page says in one line which files are loaded, and an unknown link says so', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('dataset-snapshot')).toContainText(/^[A-Z][a-z]{2} \d{1,2}, \d{4} files · 2 plans · [\d,]+ prices/)
  await page.getByTestId('dataset-plans').click()
  await expect(page.getByRole('dialog')).toContainText('Plans in these files')
  await page.getByRole('dialog').getByRole('button', { name: 'Close' }).click()

  await page.goto('/no-such-page')
  await expect(page.getByRole('heading', { name: 'Page not found', level: 1 })).toBeVisible()
  await page.getByRole('link', { name: 'Go to search' }).click()
  await expect(page).toHaveURL(/\/$/)
})
