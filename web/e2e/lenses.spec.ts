import { expect, test } from '@playwright/test'

// Runs against the real API and data, like search.spec.ts. See playwright.config.ts for E2E_BASE_URL.

test('an example runs its search, and "I\'m a" gives one line for the role', async ({ page }) => {
  await page.goto('/')
  await page.getByTestId('example-45378').click()
  await expect(page).toHaveURL(/[?&]code=45378(&|$)/)
  await expect(page.getByTestId('price-tile').first()).toBeVisible()

  // Picking a role and typing a case count only change the reading: no new search request.
  let rateRequests = 0
  page.on('request', (request) => {
    rateRequests += Number(request.url().includes('/api/v1/rates?'))
  })
  await page.getByTestId('lens-pick-plans').click()
  await expect(page).toHaveURL(/[?&]lens=plans(&|$)/)
  await expect(page.getByTestId('lens-plans').getByTestId('role-line')).toContainText(/Most places get|Too few places/)
  await page.getByTestId('lens-pick-employers').click()
  await expect(page).toHaveURL(/[?&]lens=employers(&|$)/)
  await page.getByTestId('cases-input').locator('input').fill('1200')
  await expect(page).toHaveURL(/[?&]cases=1200(&|$)/)
  await expect(page.getByTestId('yearly-gap')).toContainText(/About \$[\d,.]+ less a year for your 1,200 cases at lower-priced providers\./)
  expect(rateRequests).toBe(0)

  // A different code is a different service: the role stays, the case count goes.
  await page.getByRole('combobox', { name: 'Service or code' }).fill('66984')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page).toHaveURL(/[?&]code=66984(&|$)/)
  await expect(page).toHaveURL(/[?&]lens=employers(&|$)/)
  await expect(page).not.toHaveURL(/[?&]cases=/)
  await expect(page.getByTestId('lens-employers')).toBeVisible()
  await expect(page.getByTestId('yearly-gap')).toHaveCount(0)

  // No prices, no role line: the hint explains.
  await page.getByRole('combobox', { name: 'Service or code' }).fill('J9999')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page.getByTestId('no-prices')).toHaveText('No prices for this in these files.')
  await page.getByTestId('no-prices-why-toggle').click()
  await expect(page.getByTestId('hint-code_not_found')).toBeVisible()
  await expect(page.getByTestId('lens-panel')).toHaveCount(0)
})

test('the dark theme turns the whole page dark', async ({ page }) => {
  await page.goto('/?code=45378')
  await expect(page.getByTestId('price-tile').first()).toBeVisible()
  const app = page.locator('.v-application')
  await expect(app).toHaveCSS('background-color', 'rgb(246, 242, 234)')

  await page.getByRole('button', { name: 'Switch to dark theme' }).click()

  await expect(app).toHaveClass(/v-theme--dark/)
  await expect(app).toHaveCSS('background-color', 'rgb(18, 22, 20)')
  await expect(page.getByTestId('price-tile').first()).toHaveCSS('background-color', 'rgb(26, 31, 28)')
  await expect(page.getByRole('button', { name: 'Switch to light theme' })).toBeVisible()
})

// Vuetify's stylesheet declares its CSS layer order. If a component's CSS loads first, the order flips and the reset's
// `button { font: inherit }` gives every button the body text's weight (400).
test("buttons keep Vuetify's button type", async ({ page }) => {
  await page.goto('/?code=45378')
  await expect(page.getByTestId('price-tile').first()).toBeVisible()
  await expect(page.getByTestId('search-submit')).toHaveCSS('font-weight', '500')
})
