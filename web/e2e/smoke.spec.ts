import { test, expect } from '@playwright/test'

test('the search page loads', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Search negotiated rates' })).toBeVisible()
})
