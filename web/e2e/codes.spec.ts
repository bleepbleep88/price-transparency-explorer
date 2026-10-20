import { expect, test } from '@playwright/test'

// Categories, visit levels and drug doses, against the real API and data. See playwright.config.ts for E2E_BASE_URL.

test("a code shows CMS's category and how it's priced", async ({ page }) => {
  await page.goto('/?code=45378')

  await expect(page.getByTestId('category-chip')).toHaveText('Procedures · Digestive/Gastrointestinal')
  await page.getByTestId('pricing-notes').getByRole('button', { name: "How it's priced" }).click()
  await expect(page.getByTestId('pricing-note-facility-rates')).toContainText('filed under a revenue code')
})

test('an office visit shows every level side by side, like for like', async ({ page }) => {
  await page.goto('/?code=99213')

  const ladder = page.getByTestId('level-ladder')
  await expect(ladder).toContainText('Prices by visit level')
  await expect(ladder.getByTestId('level-ladder-chart')).toBeVisible()
  await expect(ladder.getByTestId('level-step')).toHaveCount(5)
  await expect(ladder.getByTestId('level-step').nth(2)).toContainText('Level 3 (99213)')
  await expect(ladder.getByTestId('level-caption')).toContainText("The files don't say how often each level is billed.")

  // Another plan, setting and fee: the steps follow.
  await ladder.getByTestId('level-line').click()
  const options = page.getByRole('option')
  const second = (await options.nth(1).innerText()).trim()
  await options.nth(1).click()
  await expect(ladder.getByTestId('level-line')).toContainText(second.split(' · ')[0]!)
})

test('a drug dose becomes whole units and a cost, kept in the URL', async ({ page }) => {
  await page.goto('/?code=J3301')

  const calculator = page.getByTestId('dose-calculator')
  await expect(calculator).toContainText('One unit of J3301 is 10 mg')
  await calculator.getByTestId('dose-input').locator('input').fill('45')

  await expect(calculator.getByTestId('dose-math')).toContainText("45 mg divided by 10 mg is 4.5. Units are billed whole, so that's 5 units.")
  await expect(calculator.getByTestId('dose-math')).toContainText(/At the typical price of \$[\d.]+ a unit, 5 units come to \$[\d.,]+\./)
  await expect(page).toHaveURL(/[?&]dose=45(&|$)/)
  await expect(calculator.getByTestId('dose-notes')).toContainText('modifier JW')
})

test("the ladder follows the search's plan, setting and fee type", async ({ page }) => {
  await page.goto('/?code=99214&feeType=facility')
  await expect(page.getByTestId('level-ladder').getByTestId('level-line')).toContainText('Facility fee')

  await page.goto('/?code=99214&product=ambetter-from-fidelis-care')
  await expect(page.getByTestId('level-ladder').getByTestId('level-line')).toContainText('Ambetter from Fidelis Care')
})

test('a drug priced only for discarded amounts gets no dose cost, and says why', async ({ page }) => {
  await page.goto('/?code=J0585&dose=100')

  const calculator = page.getByTestId('dose-calculator')
  await expect(calculator.getByTestId('dose-reason')).toHaveText(
    "This plan lists a price with 5 or more providers only for discarded drug (modifier JW), so we can't price a dose.",
  )
  await expect(calculator.getByTestId('dose-math')).toHaveCount(0)
})
