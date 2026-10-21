import { expect, test } from '@playwright/test'

// Specialty names are Medicare's, from CMS's taxonomy crosswalk (public domain), not the NUCC's display names. Against the
// real API and data (see playwright.config.ts).

type Suggestion = { specialty: string; group: string | null; npis: number }

test("the specialty list offers Medicare's names", async ({ request }) => {
  const suggest = async (q: string): Promise<Suggestion[]> =>
    (await request.get(`/api/v1/specialties?q=${encodeURIComponent(q)}&limit=50`)).json()

  expect((await suggest('gastro'))[0]).toMatchObject({ specialty: 'Gastroenterology', group: 'Physician' })
  expect((await suggest('nurse practitioner'))[0]).toMatchObject({ specialty: 'Nurse Practitioner', group: null })
  expect((await suggest('chiropractor')).map((s) => s.specialty)).toContain('Chiropractic')
  // "physician" finds the physician specialties through their group. The NUCC's names end in "Physician"
  // ("Gastroenterology Physician"); Medicare's never do.
  const physicians = await suggest('physician')
  expect(physicians.map((s) => s.specialty)).toContain('Gastroenterology')
  expect(physicians.filter((s) => s.specialty.endsWith(' Physician'))).toEqual([])
})

test('a provider row names its specialty the Medicare way', async ({ page }) => {
  await page.goto('/?code=45378&specialty=gastro')
  await expect(page.getByText('Gastroenterology', { exact: false }).first()).toBeVisible()
  await expect(page.getByText('Gastroenterology Physician')).toHaveCount(0)
})
