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
  await expect(surgeryCenter).toContainText('Filed under revenue code 0490')
  await expect(page.getByTestId('provider-row').first()).toBeVisible()
})

test('an EIN from the 99213 results narrows the table to that provider and shows how it compares', async ({ page }) => {
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
  await expect(page.getByTestId('group-highlights').first()).toContainText('How your provider compares')
  // Previous results stay on screen while the new ones load, so wait for the table to settle.
  await expect
    .poll(async () => [...new Set(await page.getByTestId('row-ein').allInnerTexts())])
    .toEqual([`EIN ${ein}`])
})

test('a Manhattan location filter on 45378 keeps only providers that practice there', async ({ page }) => {
  await page.goto('/?code=45378')
  await expect(page.getByTestId('provider-row').first()).toBeVisible()
  const total = page.getByTestId('row-total')
  const rowCount = async () => Number((await total.innerText()).replace(/\D/g, ''))
  const unfiltered = await rowCount()

  await page.getByText('More filters').click()
  await page.getByRole('combobox', { name: 'Location' }).click()
  await page.getByRole('option', { name: /^Manhattan/ }).click()
  await page.getByRole('button', { name: 'Search', exact: true }).click()

  await expect(page).toHaveURL(/[?&]county=Manhattan(&|$)/)
  await expect(page.getByTestId('active-filter').filter({ hasText: 'Location: Manhattan' })).toBeVisible()
  // The server narrows the rows; once they arrive, every row leads with its Manhattan place.
  await expect.poll(rowCount).toBeLessThan(unfiltered)
  await expect
    .poll(async () => {
      const lines = await page.getByTestId('row-who').allInnerTexts()
      return lines.length > 0 && lines.every((line) => line.includes('Manhattan, NY'))
    })
    .toBe(true)
  await expect(page.getByTestId('group-highlights').first()).toContainText('Where providers in "Manhattan" sit')
  await expect(page.getByTestId('directory-source')).toContainText('national provider registry (NPPES, October 2026)')
})
