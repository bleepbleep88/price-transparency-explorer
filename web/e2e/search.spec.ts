import { expect, test } from '@playwright/test'

// Both flows run against the real API and data. See playwright.config.ts for E2E_BASE_URL.

test('knee arthroscopy (29881) is explained as a facility-only rate', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Search negotiated rates' })).toBeVisible()

  await page.getByRole('combobox', { name: 'Billing code' }).fill('29881')
  await page.getByRole('button', { name: 'Search', exact: true }).click()

  await expect(page).toHaveURL(/[?&]code=29881(&|$)/)
  await expect(page.getByTestId('hint-code_facility_only')).toContainText('revenue code 0490')

  const surgeryCenter = page.getByTestId('group-card').filter({ hasText: 'Ambulatory surgery center' }).first()
  await expect(surgeryCenter).toContainText('Facility fee')
  await expect(surgeryCenter).toContainText('Revenue code 0490')
  await expect(page.getByTestId('provider-row').first()).toBeVisible()
})

test('an EIN from the 99213 results narrows the table to that provider and shows where it sits', async ({ page }) => {
  await page.goto('/?code=99213')
  const rows = page.getByTestId('provider-row')
  await expect(rows.first()).toBeVisible()

  // The first provider with a real tax ID: masked ones can't be typed back in.
  const provider = rows.filter({ hasNotText: 'Unverified tax ID' }).first()
  const ein = (await provider.getByTestId('row-ein').innerText()).replace(/^EIN\s+/, '').trim()
  expect(ein).toMatch(/^\d{2}-\d{7}$/)

  await page.getByRole('textbox', { name: 'EIN (optional)' }).fill(ein)
  await page.getByRole('button', { name: 'Search', exact: true }).click()

  await expect(page).toHaveURL(new RegExp(`[?&]ein=${ein}(&|$)`))
  await expect(page.getByTestId('active-filter').filter({ hasText: `EIN ${ein}` })).toBeVisible()
  await expect(page.getByTestId('group-highlights').first()).toContainText('Where your provider sits')
  // Previous results stay on screen while the new ones load, so wait for the table to settle.
  await expect
    .poll(async () => [...new Set(await page.getByTestId('row-ein').allInnerTexts())])
    .toEqual([`EIN ${ein}`])
})
