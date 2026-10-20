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

  // Cases a year turn the per-case gap into a yearly figure, without a new search.
  await page.getByTestId('cases-input').locator('input').fill('1200')
  await expect(page).toHaveURL(/[?&]cases=1200(&|$)/)
  await expect(panel.getByTestId('yearly-gap')).toContainText(/≈ \$[\d,]+\.\d{2} a year if those 1,200 cases went to/)

  // The lens survives a new search.
  await page.getByRole('combobox', { name: 'Billing code' }).fill('66984')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page).toHaveURL(/[?&]code=66984(&|$)/)
  await expect(page).toHaveURL(/[?&]lens=employers(&|$)/)
  await expect(panel.getByTestId('lens-employers')).toBeVisible()
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
