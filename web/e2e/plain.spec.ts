import { expect, test } from '@playwright/test'

// Decision 89: the core search pages put every number in plain words. Runs against the real API and data.
const BANNED = /percentile|median|quartile|×|p90|p10|spread multiple|\bindex\b|comparison group/i

const routes = [
  '/?code=45378',
  '/?code=99213&npi=1801474036',
  '/?code=99213&npi=1801474036&lens=providers',
  '/?code=0490&lens=plans',
  '/?code=45378&lens=employers&cases=1200',
  '/?code=29881&lens=innovators',
  '/?code=J0585&lens=life-sciences',
  // Has the out-of-network panel too.
  '/?code=97530',
]

test('the home page uses no statistics words', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('start-providers')).toBeVisible()
  await expect(page.getByTestId('dataset-snapshot')).toBeVisible()
  expect(await page.locator('body').innerText()).not.toMatch(BANNED)
})

for (const route of routes) {
  test(`${route} uses no statistics words and says once what the numbers are`, async ({ page }) => {
    await page.goto(route)
    await expect(page.getByTestId('group-card').first()).toBeVisible()
    await expect(page.getByTestId('provider-row').first()).toBeVisible()
    const text = await page.locator('body').innerText()
    expect(text).not.toMatch(BANNED)
    expect(text.match(/not what a person pays/g)).toHaveLength(1)
    await expect(page.getByTestId('like-for-like')).toHaveText('Each card compares like with like: same service, same setting, same plan.')
  })
}

test('the sources page uses no statistics words, and out-of-network amounts are never "typical" or "payments"', async ({ page }) => {
  await page.goto('/sources')
  await expect(page.getByTestId('source-file').first()).toBeVisible()
  const text = await page.locator('body').innerText()
  expect(text).not.toMatch(BANNED)
  expect(text).not.toMatch(/typical|payment|npi \[0\]/i)

  await page.goto('/?code=97530')
  const panel = page.getByTestId('oon-panel')
  await expect(panel.getByTestId('oon-row').first()).toBeVisible()
  expect(await panel.innerText()).not.toMatch(/typical|payment/i)
})
