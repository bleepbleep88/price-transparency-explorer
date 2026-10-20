import { expect, test } from '@playwright/test'

// Runs against the real API and data, like search.spec.ts. See playwright.config.ts for E2E_BASE_URL.

test('a Start here card runs its search with the lens, and the lens explains the result', async ({ page }) => {
  await page.goto('/')
  await page.getByTestId('start-employers-45378').click()

  await expect(page).toHaveURL(/[?&]code=45378(&|$)/)
  await expect(page).toHaveURL(/[?&]lens=employers(&|$)/)
  const panel = page.getByTestId('lens-panel')
  await expect(panel.getByRole('heading', { name: 'What this means for you' })).toBeVisible()
  await expect(panel.getByTestId('cost-line').first()).toBeVisible()

  // Switching lens and typing a case count only change the reading: no new search request.
  let rateRequests = 0
  page.on('request', (request) => {
    rateRequests += Number(request.url().includes('/api/v1/rates?'))
  })
  await panel.getByRole('button', { name: 'Plans', exact: true }).click()
  await expect(page).toHaveURL(/[?&]lens=plans(&|$)/)
  await expect(panel.getByRole('button', { name: 'Plans', exact: true, pressed: true })).toBeVisible()
  await panel.getByRole('button', { name: 'Employers', exact: true }).click()
  await expect(page).toHaveURL(/[?&]lens=employers(&|$)/)
  await page.getByTestId('cases-input').locator('input').fill('1200')
  await expect(page).toHaveURL(/[?&]cases=1200(&|$)/)
  await expect(panel.getByTestId('yearly-gap')).toContainText(/≈ \$[\d,]+ a year for .+: the gap per case × your 1,200 cases/)
  expect(rateRequests).toBe(0)

  // A different code is a different service: the lens stays, the case count goes.
  await page.getByRole('combobox', { name: 'Billing code' }).fill('66984')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page).toHaveURL(/[?&]code=66984(&|$)/)
  await expect(page).toHaveURL(/[?&]lens=employers(&|$)/)
  await expect(page).not.toHaveURL(/[?&]cases=/)
  await expect(panel.getByTestId('lens-employers')).toBeVisible()
  await expect(panel.getByTestId('yearly-gap')).toHaveCount(0)

  // No rates, no reading: the hint explains, and the lens panel stays away.
  await page.getByRole('combobox', { name: 'Billing code' }).fill('J9999')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page.getByTestId('hint-code_not_found')).toBeVisible()
  await expect(page.getByTestId('lens-panel')).toHaveCount(0)
})

test('the dark theme turns the whole ledger dark', async ({ page }) => {
  await page.goto('/?code=45378')
  await expect(page.getByTestId('group-card').first()).toBeVisible()
  const app = page.locator('.v-application')
  await expect(app).toHaveCSS('background-color', 'rgb(246, 242, 234)')

  await page.getByRole('button', { name: 'Switch to dark theme' }).click()

  await expect(app).toHaveClass(/v-theme--dark/)
  await expect(app).toHaveCSS('background-color', 'rgb(18, 22, 20)')
  await expect(page.getByTestId('group-card').first()).toHaveCSS('background-color', 'rgb(26, 31, 28)')
  await expect(page.getByRole('button', { name: 'Switch to light theme' })).toBeVisible()
})

// Vuetify's stylesheet declares its CSS layer order. If a component's CSS loads first, the order flips and the reset's
// `button { font: inherit }` gives every button the body text's weight (400).
test("buttons keep Vuetify's button type", async ({ page }) => {
  await page.goto('/?code=45378')
  await expect(page.getByTestId('group-card').first()).toBeVisible()
  await expect(page.getByTestId('search-submit')).toHaveCSS('font-weight', '500')
})
