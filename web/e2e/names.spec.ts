import { expect, test } from '@playwright/test'

// Practice names, against the real API and data. The plan lists many practices under a network's name ("Chinese
// American IPA Assoc/CAIPA" on Ambetter), and the Sep 28 Essential Plan file names tax IDs after other providers. Each
// place shows its own practice's name; the plan's name stays in the source drawer. See playwright.config.ts for E2E_BASE_URL.

test("each place under a network's name shows its own practice, and the drawer keeps the plan's name", async ({ page }) => {
  await page.goto('/?code=00731&plan=ambetter-from-fidelis-care')

  const rows = page.getByTestId('place-row')
  await expect(rows.first()).toBeVisible()
  const names = await rows.locator('.name').allInnerTexts()
  expect(names).toHaveLength(5)
  expect(new Set(names).size).toBe(names.length)
  expect(names.join(' | ')).not.toContain('Chinese American IPA')

  // All 13 places: none is the network's name.
  await page.getByTestId('see-all').click()
  const table = page.getByTestId('provider-table')
  await expect(table).toContainText('Newtown Gastroenterology')
  await expect(table).not.toContainText('Chinese American IPA')

  await rows.filter({ hasText: 'Eighth Avenue Anesthesia' }).click()
  const drawer = page.getByTestId('source-drawer')
  await expect(drawer.getByTestId('source-listed-as')).toHaveText('Listed by the plan as Chinese American IPA Assoc/CAIPA')

  // Its scorecard has the same name.
  await page.goto('/providers/464213772')
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Eighth Avenue Anesthesia')
})

test("a file that names tax IDs after other providers shows each practice's own name", async ({ page }) => {
  await page.goto('/?code=00731&plan=essential-plan')

  const rows = page.getByTestId('place-row')
  await expect(rows.first()).toBeVisible()
  const names = await rows.locator('.name').allInnerTexts()
  expect(new Set(names).size).toBe(names.length)
  // The same practices as on the Ambetter tab, under the same names.
  expect(names).toContain('Eighth Avenue Anesthesia')
  expect(names.join(' | ')).not.toMatch(/MMC Faculty Practice|Bellevue Hospital Center|Jacobi Medical Center/)
})

test("ENT & Allergy's tax ID (13-4000192) has its own name on every page, though the Sep 28 Essential file calls it others", async ({ page }) => {
  // Places: the Essential Plan file lists it as "Memorial Sloan Kettering Cancer Center at Basking Ridge" here.
  await page.goto('/?code=30903&ein=134000192&plan=essential-plan')
  const row = page.getByTestId('place-row').first()
  await expect(row.locator('.name')).toHaveText('ENT & Allergy Associates')
  await row.click()
  await expect(page.getByTestId('source-listed-as')).toHaveText('Listed by the plan as Memorial Sloan Kettering Cancer Center at Basking Ridge')

  await page.goto('/providers/134000192')
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('ENT & Allergy Associates')

  await page.goto('/insights/compare?code=31237')
  await expect(page.locator('main')).toContainText('ENT & Allergy Associates')

  // Aug 31 to Sep 28: the same name on both sides of the change.
  // Its two Aug to Sep rows are the same service relabeled, which the counts leave out unless asked for.
  await page.goto('/changes?code=30903&includeListedElsewhere=true')
  await expect(page.locator('main')).toContainText('ENT & Allergy Associates')
  await expect(page.locator('main')).not.toContainText('Memorial Sloan Kettering')
})

test("the sources page says the Sep 28 Essential file's names don't match, in Details", async ({ page }) => {
  await page.goto('/sources')
  const september = page.getByTestId('source-snapshot').filter({ hasText: 'Sep 28, 2026' })
  await expect(september.getByTestId('source-names')).toBeHidden()
  await september.getByTestId('details-toggle').click()
  await expect(september.getByTestId('source-names')).toHaveText(
    'Provider names in this file match the other files for only 9% of the same tax IDs (about 98% between the other files). ' +
      "Names shown come from the same tax ID in Ambetter's Sep 28 file, else Essential Plan's Aug 31 file. " +
      `134 names couldn't be found: they show as "Unknown practice".`,
  )
  await expect(september.getByTestId('source-names')).toHaveCount(1)
})

test("a network's busiest member keeps its own name in every month", async ({ page }) => {
  // Both are the busiest tax ID under "Beacon IPA LLC" in some month (snapshots 1 to 3: Sep 28, Aug 31, Jul 27).
  for (const snapshot of [1, 2, 3]) {
    await page.goto(`/providers/562325021?snapshot=${snapshot}`)
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Lefferts Medical Associates')
    await page.goto(`/providers/113211425?snapshot=${snapshot}`)
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Advanced Dermatology')
  }
})

test("a name only the mislabelled file gives reads \"Unknown practice\", with the plan's name in the drawer", async ({ page }) => {
  // The Sep 28 Essential Plan file puts "Jacobi Medical Center" on 84 tax IDs; nothing else names 52-2331239.
  await page.goto('/?code=99213&ein=522331239&plan=essential-plan')
  const row = page.getByTestId('place-row').first()
  await expect(row.locator('.name')).toHaveText('Unknown practice')
  await row.click()
  await expect(page.getByTestId('source-listed-as')).toHaveText('Listed by the plan as Jacobi Medical Center')
})

test("a provider search matches the names shown, not the names the mislabelled file gives other tax IDs", async ({ page, request }) => {
  // The Sep 28 Essential file calls Memorial Sloan Kettering at Basking Ridge, among others, "Teladoc".
  const teladoc = await (await request.get('/api/v1/providers?q=teladoc')).json()
  expect(teladoc.map((suggestion: { businessName: string }) => suggestion.businessName)).toEqual(['Teladoc Physicians PC'])

  await page.goto('/providers')
  await page.getByTestId('provider-lookup').locator('input').fill('teladoc')
  await expect(page.getByRole('option')).toHaveCount(1)
  await expect(page.getByRole('option').first()).toContainText('Teladoc Physicians')
  await expect(page.getByRole('listbox')).not.toContainText(/Memorial Sloan|Starrett City/)
})
