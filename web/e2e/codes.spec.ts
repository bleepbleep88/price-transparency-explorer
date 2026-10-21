import { expect, test } from '@playwright/test'

// Categories, visit levels and drug doses, against the real API and data. See playwright.config.ts for E2E_BASE_URL.

test("a code's Details show CMS's category and how it's priced", async ({ page }) => {
  await page.goto('/?code=45378')

  await page.getByTestId('search-details-toggle').click()
  await expect(page.getByTestId('category-chip')).toHaveText('Procedures · Digestive/Gastrointestinal · Lower GI Endoscopy - Other')
  await expect(page.getByTestId('pricing-note-facility-rates')).toContainText('filed under a revenue code')
})

test('an office visit shows every level side by side, numbers in Details', async ({ page }) => {
  await page.goto('/?code=99213')

  const ladder = page.getByTestId('level-ladder')
  await expect(ladder.getByRole('heading', { name: 'Price by level' })).toBeVisible()
  await expect(ladder.getByTestId('level-ladder-chart')).toBeVisible()
  await expect(ladder.getByTestId('level-ladder-chart')).toHaveAttribute('aria-label', /^Typical price by level: Level 1 \$/)
  // Essential Plan in the office: level 5 ($101, 70 places) sits below level 4 ($163, 40 places), and the chart says why.
  await expect(ladder.getByTestId('level-dip-note')).toHaveText("Different places are priced at each level, so the steps don't always rise.")
  await expect(ladder.getByTestId('level-table')).toBeHidden()

  await expect(ladder.getByTestId('level-details-toggle')).toHaveText('Details: levels')
  await ladder.getByTestId('level-details-toggle').click()
  await expect(ladder.getByTestId('level-step')).toHaveCount(5)
  await expect(ladder.getByTestId('level-step').nth(2)).toContainText(/Level 3\s*99213/)
  await expect(ladder.getByTestId('level-caption')).toHaveText("How often each level is billed isn't in the files.")

  // Another plan and bill: the title follows.
  await ladder.getByTestId('level-line').click()
  const options = page.getByRole('option')
  const second = (await options.nth(1).innerText()).trim()
  await options.nth(1).click()
  await expect(ladder.getByTestId('level-line-label')).toContainText(second.split(' · ')[0]!)
})

test('a drug dose becomes whole units and a cost in one line, kept in the URL', async ({ page }) => {
  await page.goto('/?code=J3301')

  const calculator = page.getByTestId('dose-calculator')
  await expect(calculator.getByRole('heading', { name: 'Price per dose' })).toBeVisible()
  await calculator.getByTestId('dose-input').locator('input').fill('45')

  await expect(calculator.getByTestId('dose-answer')).toHaveText(/^45 mg = 5 units → \$[\d.,]+$/)
  await expect(calculator.getByTestId('dose-unit')).toHaveText('1 unit = 10 mg')
  await expect(page).toHaveURL(/[?&]dose=45(&|$)/)

  await calculator.getByTestId('dose-details-toggle').click()
  await expect(calculator.getByTestId('dose-details')).toContainText('modifier JW')
  await expect(calculator.getByTestId('dose-card-label')).toContainText('Price from: ')
})

test("the ladder follows the search's plan, setting and fee type", async ({ page }) => {
  await page.goto('/?code=99214&feeType=facility')
  await expect(page.getByTestId('level-ladder').getByTestId('level-line-label')).not.toHaveText(/· Doctor(?!')/)

  await page.goto('/?code=99214&product=ambetter-from-fidelis-care')
  await expect(page.getByTestId('level-ladder').getByTestId('level-line-label')).toContainText('Ambetter from Fidelis Care')
})

test('a drug priced only for discarded amounts gets no dose cost, and says why', async ({ page }) => {
  await page.goto('/?code=J0585&dose=100')

  const calculator = page.getByTestId('dose-calculator')
  await expect(calculator.getByTestId('dose-reason')).toHaveText('Only priced for thrown-away drug. No dose price.')
  await expect(calculator.getByTestId('dose-answer')).toHaveCount(0)
})

test('the source drawer and the NPI list give focus back to what opened them', async ({ page }) => {
  await page.goto('/?code=99213')

  const row = page.getByTestId('place-row').first()
  await row.focus()
  await page.keyboard.press('Enter')
  const drawer = page.getByTestId('source-drawer')
  await expect(drawer.getByTestId('source-json-path')).toBeVisible()
  await drawer.getByRole('button', { name: 'Close' }).click()
  await expect(row).toBeFocused()

  await page.getByTestId('see-all').click()
  const menu = page.getByTestId('row-menu').first()
  await menu.focus()
  await page.keyboard.press('Enter')
  await page.getByText(/^Who works here/).first().click()
  const dialog = page.getByTestId('npi-dialog')
  await expect(dialog.getByTestId('npi-count')).toBeVisible()
  await dialog.getByRole('button', { name: 'Close' }).click()
  await expect(menu).toBeFocused()
})
