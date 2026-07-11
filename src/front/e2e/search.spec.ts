import { test, expect } from '@playwright/test';

// Search → artist ficha. Trigram search is diacritic/typo tolerant (pg_trgm): "darkthron"
// resolves to Darkthrone. The name renders in the Redaction cut its rank earns (typographic
// degradation): Darkthrone is Obscure → cut 70.
test('trigram search tolerates a typo and opens the ficha', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Search the grimoire' })).toBeVisible();

  await page.getByRole('searchbox').fill('darkthron');
  const row = page.getByRole('link', { name: /Darkthrone/ });
  await expect(row).toBeVisible();
  await row.click();

  await expect(page).toHaveURL(/\/artist\/[0-9a-f-]{36}$/);
  const name = page.locator('h1 span').first();
  await expect(name).toHaveText('Darkthrone');
  // Typographic degradation by rank: Obscure -> Redaction 70.
  await expect(name).toHaveAttribute('style', /Redaction 70/);
});

test('a nonsense query shows the designed empty state, not a crash', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('searchbox').fill('zzzxqnowaythisexists');
  await expect(page.getByText(/Nothing answers to that name here/)).toBeVisible();
});

test('semantic (by meaning) search returns ranked hits', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'By meaning' }).click();
  await page.getByRole('searchbox').fill('cold atmospheric black metal');
  // At least one hit resolves to an artist link; each shows a distance.
  await expect(page.getByText(/distance/i).first()).toBeVisible({ timeout: 20_000 });
  const hits = page.locator('ul li a[href^="/artist/"]');
  await expect(hits.first()).toBeVisible();
});
