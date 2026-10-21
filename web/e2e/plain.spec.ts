import { expect, test } from '@playwright/test'

// Decisions 89 and 90: every number in plain words, and the main view simple enough for anyone. Runs against the real
// API and data. What sits behind Details (closed) isn't on the main view.
const BANNED = /percentile|median|quartile|×|p90|p10|spread multiple|\bindex\b|comparison group/i
// Off the main view: codes and IDs belong in Details. Filter chips the reader set (an NPI) are taken out first.
const JARGON = /\bmodifiers?\b|\bEIN\b|tax ID|revenue code|\bNPIs?\b|unique rate|published row|snapshot|provider group/i
// The main view stays short: about 120 words for colonoscopy, a little more with a role line or a dose.
const WORD_BUDGET = 220

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
  // Specialty and location filters.
  '/?code=45378&county=Manhattan&lens=providers',
  '/?code=45378&specialty=gastro&lens=employers',
  // The level ladder and the dose calculator.
  '/?code=99214',
  '/?code=J3301&dose=45&lens=life-sciences',
  '/?code=J0585&dose=100',
]

test('the home page uses no statistics words', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('example-45378')).toBeVisible()
  const text = await page.locator('main').innerText()
  expect(text).not.toMatch(BANNED)
  expect(text).not.toMatch(JARGON)
})

for (const route of routes) {
  test(`${route} is in plain words, short, and says once what the numbers are`, async ({ page }) => {
    await page.goto(route)
    await expect(page.getByTestId('price-tile').first()).toBeVisible()
    await expect(page.getByTestId('place-row').first()).toBeVisible()
    const body = await page.locator('body').innerText()
    expect(body).not.toMatch(BANNED)
    expect(body.match(/Not what a patient pays/g)).toHaveLength(1)

    // The search form's own labels (NPI, Tax ID) and the provider role, which asks for an NPI, are exempt: providers
    // know their NPI. Filter chips the reader set are taken out too.
    let main = await page.locator('main').innerText()
    const exempt = [
      await page.getByTestId('search-form').innerText(),
      ...(await page.getByTestId('lens-providers').allInnerTexts()),
      ...(await page.getByTestId('active-filter').allInnerTexts()),
    ]
    for (const text of exempt) main = main.replace(text, '')
    expect(main).not.toMatch(JARGON)
    expect(main.split(/\s+/).filter(Boolean).length).toBeLessThanOrEqual(WORD_BUDGET)
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
