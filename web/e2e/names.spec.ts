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
