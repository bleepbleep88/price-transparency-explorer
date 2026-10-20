import { expect, test } from '@playwright/test'

// Runs against the real API with the three monthly snapshots loaded (2026-07-27, 2026-08-31, 2026-09-28). See
// playwright.config.ts for E2E_BASE_URL.

test("the app bar's snapshot picker moves search to the brief's file and back, and each card shows its trend", async ({ page }) => {
  await page.goto('/?code=45378')
  const card = page.getByTestId('group-card').filter({ has: page.getByTestId('trend-line') }).first()
  await expect(card.getByTestId('trend-typical')).toContainText(/^The typical price (went from \$[\d,.]+ \(Jul 27\) to \$[\d,.]+ \(Sep 28\)|was \$)/)
  await expect(card.getByTestId('trend-line')).not.toContainText(/median|percentile|quartile/i)
  await expect(page.getByText('Snapshot Sep 28, 2026')).toBeVisible()

  await page.getByTestId('snapshot-picker').click()
  await page.getByTestId('snapshot-option-2026-08-31').click()

  await expect(page).toHaveURL(/[?&]snapshot=\d+(&|$)/)
  await expect(page).toHaveURL(/[?&]code=45378(&|$)/)
  await expect(page.getByText('Snapshot Aug 31, 2026')).toBeVisible()
  await expect(page.getByTestId('snapshot-picker')).toContainText('Aug 31, 2026')

  // A new search stays on the picked file.
  await page.getByRole('combobox', { name: 'Billing code' }).fill('29881')
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await expect(page).toHaveURL(/[?&]code=29881(&|$)/)
  await expect(page).toHaveURL(/[?&]snapshot=\d+(&|$)/)
  await expect(page.getByText('Snapshot Aug 31, 2026')).toBeVisible()

  // The newest file is the default, so picking it leaves the URL without a snapshot.
  await page.getByTestId('snapshot-picker').click()
  await page.getByTestId('snapshot-option-2026-09-28').click()
  await expect(page).not.toHaveURL(/[?&]snapshot=/)
  await expect(page.getByText('Snapshot Sep 28, 2026')).toBeVisible()
})

test("the changes page compares the newest file with the one before, narrows to rates that went up and a code, and leads to the provider's trend", async ({
  page,
}) => {
  await page.goto('/changes')
  await expect(page.getByTestId('changes-summary')).toContainText(/^Between Aug 31, 2026 and Sep 28, 2026: [\d,]+ rates went up/)
  await expect(page.getByTestId('change-row').first()).toBeVisible()

  await page.getByTestId('changes-kind-increased').click()
  await expect(page).toHaveURL(/[?&]kind=increased(&|$)/)
  await page.getByTestId('changes-code').locator('input').fill('99213')
  await page.getByTestId('changes-apply').click()
  await expect(page).toHaveURL(/[?&]code=99213(&|$)/)
  // Previous results stay on screen while the new ones load, so wait for the table to settle.
  await expect.poll(async () => [...new Set(await page.getByTestId('change-row').locator('span').first().allInnerTexts())]).toEqual(['CPT 99213'])
  await expect(page.getByTestId('change-text').first()).toHaveText(/^Up \$[\d,.]+ \((under 1%|\d+%|about [\d.]+ times as much)\)$/)

  // Leaving out rates only listed differently keeps the changed ones.
  await page.getByTestId('changes-hide-listed').locator('input').check()
  await expect(page).toHaveURL(/[?&]listedElsewhere=false(&|$)/)
  await expect(page.getByTestId('listed-elsewhere')).toHaveCount(0)

  // A row leads to that provider's rows in search, where the card follows its rate across the files.
  await page.getByTestId('change-search').first().click()
  await expect(page).toHaveURL(/[?&]ein=[^&]+/)
  await expect(page).toHaveURL(/[?&]group=[^&]+/)
  await expect(page.getByTestId('provider-row').first()).toBeVisible()
  await expect(page.getByTestId('trend-provider').first()).toContainText(/^Your rate( went from|:| was) \$[\d,.]+/)
})
