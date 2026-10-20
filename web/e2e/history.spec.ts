import { expect, test } from '@playwright/test'

// Runs against the real API with the three monthly snapshots loaded (2026-07-27, 2026-08-31, 2026-09-28). See
// playwright.config.ts for E2E_BASE_URL.

test("the app bar's snapshot picker moves search to the brief's file and back, and each card shows its trend", async ({ page }) => {
  await page.goto('/?code=45378')
  const card = page.getByTestId('group-card').filter({ has: page.getByTestId('trend-line') }).first()
  // Three files: each one is named, so the one on screen is never skipped; the providers listed in both lead.
  await expect(card.getByTestId('trend-typical')).toContainText(/\$[\d,.]+ \(Jul 27\), \$[\d,.]+ \(Aug 31\) and \$[\d,.]+ \(Sep 28\)|was \$[\d,.]+ in the/)
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
  // The card names the provider; "your rate" is only the providers lens speaking to the provider.
  await expect(page.getByTestId('trend-provider').first()).toContainText(/^.+'s? rate( went from|:| was) \$[\d,.]+/)
})

test('a card whose typical price moved only because different providers are listed never calls it a price change', async ({ page }) => {
  // Ambetter 99213 office with modifier 25: all 37 providers in both the Aug 31 and Sep 28 files kept their rate (#11 review, H1).
  await page.goto('/?code=99213&product=ambetter-from-fidelis-care&setting=office')
  const card = page.locator('[data-group-key="ambetter-from-fidelis-care|CPT|99213|professional|office|25|negotiated"]')
  const line = card.getByTestId('trend-typical')
  await expect(line).toContainText(
    'Of the 37 providers listed in the Aug 31 and Sep 28 files, all kept the same rate. 87 left and 39 joined, so the typical price on this card was',
  )
  await expect(line).not.toContainText(/went up|times as much/)
})

test('the changes page leaves out facility charges with no procedure named until the switch adds them', async ({ page }) => {
  await page.goto('/changes')
  const toggle = page.getByTestId('facility-charges')
  await expect(toggle).toContainText(/Include facility charges with no procedure named \([\d,]+\)/)
  const before = await page.getByTestId('changes-summary').innerText()

  await toggle.locator('input').check()
  await expect(page).toHaveURL(/[?&]includeFacilityCharges=true(&|$)/)
  await expect(page.getByTestId('changes-order')).toContainText('Facility charges with no procedure named come after the rest.')
  await expect(page.getByTestId('changes-summary')).not.toHaveText(before)
})

// Decision 89, as plain.spec.ts checks the search pages: history speaks in plain words too.
test('the changes page, the trends and the history lens lines use no statistics words', async ({ page }) => {
  const banned = /percentile|median|middle half|quartile|×|p90|p10|spread multiple|\bindex\b|comparison group/i

  await page.goto('/changes?minChangePct=5')
  await expect(page.getByTestId('change-row').first()).toBeVisible()
  expect(await page.locator('body').innerText()).not.toMatch(banned)

  for (const route of ['/?code=99213&ein=061036715&lens=providers', '/?code=45378&lens=employers', '/?code=45378&lens=plans']) {
    await page.goto(route)
    await expect(page.getByTestId('trend-line').first()).toBeVisible()
    await expect(page.getByTestId('lens-history')).toBeVisible()
    expect(await page.locator('body').innerText()).not.toMatch(banned)
  }
})
