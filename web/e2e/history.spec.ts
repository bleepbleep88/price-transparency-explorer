import { expect, test, type Page } from '@playwright/test'

// Runs against the real API with the three monthly files loaded (2026-07-27, 2026-08-31, 2026-09-28). See
// playwright.config.ts for E2E_BASE_URL.

const STATISTICS_WORDS = /percentile|median|middle half|quartile|×|p90|p10|spread multiple|\bindex\b|comparison group|snapshot/i
// Decision 90: no code types, modifiers, tax IDs or NPIs on the main view (Details may have them).
const CODES_AND_IDS = /\bCPT\b|HCPCS|\bRC \d|\bEIN\b|tax ID|modifier|\bNPI/i

/** The words on the first screen: text whose box starts above the fold, Details closed. */
async function firstScreenWords(page: Page): Promise<number> {
  return page.evaluate(() => {
    const walker = document.createTreeWalker(document.querySelector('main') ?? document.body, NodeFilter.SHOW_TEXT)
    let words = 0
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      const element = node.parentElement
      if (!element || !node.textContent?.trim()) continue
      const box = element.getBoundingClientRect()
      if (box.height === 0 || box.top >= window.innerHeight || getComputedStyle(element).visibility === 'hidden') continue
      words += node.textContent.trim().split(/\s+/).length
    }
    return words
  })
}

test("the month picker shows just the date, moves search to another month and back, and keeps it across a new search", async ({ page }) => {
  await page.goto('/?code=45378')
  const picker = page.getByTestId('snapshot-picker')
  await expect(picker).toHaveText('Sep 28, 2026')

  await picker.click()
  await page.getByTestId('snapshot-option-2026-08-31').click()
  await expect(page).toHaveURL(/[?&]snapshot=\d+(&|$)/)
  await expect(page).toHaveURL(/[?&]code=45378(&|$)/)
  await expect(picker).toHaveText('Aug 31, 2026')

  // A new search stays on the picked month.
  await page.getByRole('combobox', { name: 'Billing code' }).fill('29881')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page).toHaveURL(/[?&]code=29881(&|$)/)
  await expect(page).toHaveURL(/[?&]snapshot=\d+(&|$)/)

  // The newest is the default, so picking it leaves the URL without a month.
  await picker.click()
  await page.getByTestId('snapshot-option-2026-09-28').click()
  await expect(page).not.toHaveURL(/[?&]snapshot=/)
  await expect(picker).toHaveText('Sep 28, 2026')
})

test('a price whose typical moved only because different providers are listed says who stayed, never a price change', async ({ page }) => {
  // Ambetter 99213 office with modifier 25: all 37 providers in both the Aug 31 and Sep 28 files kept their rate (#11, H1).
  await page.goto('/?code=99213&product=ambetter-from-fidelis-care&setting=office')
  const card = page.locator('[data-group-key="ambetter-from-fidelis-care|CPT|99213|professional|office|25|negotiated"]')
  await expect(card.getByTestId('trend-who')).toContainText('Aug–Sep: 37 kept their rate, 87 left, 39 joined')
  await expect(card.getByTestId('trend-typical')).toContainText(/^Typical: Jul \$[\d,.]+ · Aug \$[\d,.]+ · Sep \$[\d,.]+$/)
  await expect(card.getByTestId('trend-line')).not.toContainText(/went up|times as much/)
})

test('the changes page leads with four big counts and the biggest changes as name, service and old to new price', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 })
  await page.goto('/changes')
  await expect(page.getByRole('heading', { name: 'Rate changes', level: 1 })).toBeVisible()
  await expect(page.getByText('Aug 31 to Sep 28, 2026')).toBeVisible()
  for (const kind of ['increased', 'decreased', 'new', 'dropped']) {
    await expect(page.getByTestId(`changes-kind-${kind}`)).toContainText(/^(Went up|Went down|New|Gone)[\d,]+$/)
  }
  await expect(page.getByTestId('changes-list-title')).toHaveText('Biggest changes')
  await expect(page.getByTestId('change-row')).toHaveCount(10)
  await expect(page.getByTestId('change-text').first()).toHaveText(/^\$[\d,.]+ → \$[\d,.]+$|^(New|Gone) · \$[\d,.]+$/)

  // Decision 90: few words, no statistics words, no codes or tax IDs up front.
  const main = await page.locator('main').innerText()
  expect(main).not.toMatch(STATISTICS_WORDS)
  expect(main).not.toMatch(CODES_AND_IDS)
  expect(await firstScreenWords(page)).toBeLessThan(150)
})

test('a count is the filter: rises, then one code, then a row opens and leads to its rates in search', async ({ page }) => {
  await page.goto('/changes')
  await page.getByTestId('changes-kind-increased').click()
  await expect(page).toHaveURL(/[?&]kind=increased(&|$)/)
  await expect(page.getByTestId('changes-list-title')).toHaveText('Biggest rises')
  await expect(page.getByTestId('changes-kind-increased')).toHaveAttribute('aria-pressed', 'true')

  await page.getByTestId('changes-code').locator('input').fill('99213')
  await page.getByTestId('changes-code').locator('input').press('Enter')
  await expect(page).toHaveURL(/[?&]code=99213(&|$)/)
  // Previous results stay on screen while the new ones load, so wait for the list to settle.
  await expect
    .poll(async () => [...new Set((await page.getByTestId('change-service').allInnerTexts()).map((text) => text.split(' · ')[0]))])
    .toEqual(['Office visit, level 3'])
  await expect(page.getByTestId('change-text').first()).toHaveClass(/text-high/)

  await page.getByTestId('change-row').first().locator('button').first().click()
  const details = page.getByTestId('change-details')
  await expect(details).toContainText('CPT 99213')
  await expect(details).toContainText(/tax ID/)
  await details.getByTestId('change-search').click()
  await expect(page).toHaveURL(/[?&]ein=[^&]+/)
  await expect(page).toHaveURL(/[?&]group=[^&]+/)
})

test('more filters hide rates only listed differently and add facility charges with no procedure named', async ({ page }) => {
  await page.goto('/changes')
  const before = await page.getByTestId('changes-kind-new').innerText()
  await page.getByTestId('changes-more-toggle').click()

  await page.getByTestId('changes-hide-listed').locator('input').check()
  await expect(page).toHaveURL(/[?&]listedElsewhere=false(&|$)/)
  await expect(page.getByTestId('changes-kind-new')).not.toHaveText(before)

  const facility = page.getByTestId('facility-charges')
  await expect(facility).toContainText(/Include facility-only charges \([\d,]+\)/)
  await facility.locator('input').check()
  await expect(page).toHaveURL(/[?&]includeFacilityCharges=true(&|$)/)
  await page.getByTestId('changes-details-toggle').click()
  await expect(page.getByTestId('changes-order')).toContainText('Facility charges with no procedure named come after the rest')
})

test('the changes page fits a phone in one column', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/changes')
  await expect(page.getByTestId('change-row').first()).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
})

test('the lens history lines are a few words with no statistics words', async ({ page }) => {
  for (const route of ['/?code=99213&ein=061036715&lens=providers', '/?code=45378&lens=employers', '/?code=45378&lens=plans']) {
    await page.goto(route)
    const line = page.getByTestId('lens-history')
    await expect(line).toBeVisible()
    const text = await line.innerText()
    expect(text).not.toMatch(STATISTICS_WORDS)
    expect(text.split(/\s+/).filter((word) => /\w/.test(word)).length).toBeLessThanOrEqual(14)
  }
})
