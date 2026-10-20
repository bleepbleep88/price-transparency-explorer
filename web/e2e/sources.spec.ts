import { expect, test } from '@playwright/test'

// Runs against the real API and data, like search.spec.ts. See playwright.config.ts for E2E_BASE_URL.

test('a code with out-of-network amounts shows the panel, and the innovators lens leads to every source file', async ({ page }) => {
  // Revenue code 0450 (emergency room): both products' allowed-amount files list ER facility amounts.
  await page.goto('/?code=0450&lens=plans')
  const panel = page.getByTestId('oon-panel')
  await expect(panel.getByRole('heading', { name: 'Out of network' })).toBeVisible()
  await expect(panel.getByTestId('oon-row')).toHaveCount(2)
  await expect(panel.getByTestId('oon-row').first()).toContainText('Facility fee')
  await expect(panel.getByTestId('oon-share').first()).toContainText(
    /^(Providers usually billed (about [\d.]+ times|about \d+% (more|less) than|about the same as) what the plan allowed\.|Only \d+ providers?: too few to compare\.)$/,
  )
  await expect(panel).not.toContainText(/median|percentile|quartile|typical|payment/i)
  await expect(panel.getByTestId('oon-notes')).toContainText("The files don't say whether an amount covers one visit or several")
  await expect(panel.getByTestId('oon-notes')).toContainText('20 or more claims')
  await expect(page.getByTestId('lens-oon-line')).toContainText(/^Out of network \(.+\), (providers usually billed .+ what the plan allowed|the plan allowed|the amounts the plan allowed)/)

  // A code nobody was paid for out of network has no panel.
  await page.getByRole('combobox', { name: 'Billing code' }).fill('99213')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page.getByTestId('group-card').first()).toBeVisible()
  await expect(page.getByTestId('oon-panel')).toHaveCount(0)

  await page.getByTestId('lens-pick-innovators').click()
  await page.getByTestId('lens-sources-link').click()
  await expect(page).toHaveURL(/\/sources$/)
  await expect(page.getByRole('heading', { name: 'Sources and data quality', level: 1 })).toBeVisible()

  const files = page.getByTestId('source-file')
  await expect(files).toHaveCount(4)
  await expect(files.filter({ hasText: 'In-network rates' })).toHaveCount(2)
  const outOfNetwork = files.filter({ hasText: 'Out-of-network allowed amounts' })
  await expect(outOfNetwork).toHaveCount(2)
  await expect(outOfNetwork.first()).toContainText(/[\d,]+ amounts in the file became [\d,]+ different amounts after removing exact copies\./)
  await expect(outOfNetwork.first().getByRole('link', { name: 'Open the file' })).toHaveAttribute('href', /allowed-amounts\.json$/)
  await expect(files.first().getByRole('link', { name: 'Open the file' })).toHaveAttribute('href', /in-network\.json$/)
  await expect(files.first()).toContainText(/SHA-256 [0-9a-f]{64}/)
})
