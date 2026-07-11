import { test, expect } from '@playwright/test';
import { uniqueEmail } from './helpers';

// The Rite, the whole tentpole, driven through the UI end to end:
// anonymous -> register -> cold start (choose 5 bands) -> the console -> serve blind (preview
// through the proxy, no name shown) -> Summon -> reveal -> the grimoire grows.
test('cold start, serve blind, summon and grow the grimoire', async ({ page }) => {
  const audioProxyHits: string[] = [];
  page.on('request', (req) => {
    if (/\/api\/rite\/[0-9a-f-]{36}\/audio$/.test(req.url())) {
      audioProxyHits.push(req.url());
    }
  });

  // 1. Anonymous visitor is gated by the auth panel.
  await page.goto('/rite');
  await expect(page.getByRole('heading', { name: 'The Rite' })).toBeVisible();

  // 2. Register a fresh account through the real form.
  await page.getByRole('button', { name: 'No account — create one' }).click();
  await page.locator('input[type=email]').fill(uniqueEmail('rite'));
  await page.locator('input[type=password]').fill('Passw0rd!x2026');
  await page.getByRole('button', { name: 'Create account' }).click();

  // 3. Cold start: choose five bands.
  await expect(page.getByRole('heading', { name: 'Name what you already love' })).toBeVisible();
  const chips = page.locator('button[aria-pressed]');
  await expect(chips.first()).toBeVisible();
  for (let i = 0; i < 5; i++) {
    await chips.nth(i).click();
  }
  await expect(page.getByText('5 of 5 chosen')).toBeVisible();
  await page.getByRole('button', { name: 'Set my bearing' }).click();

  // 4. The console appears (taste is set).
  await expect(page.getByRole('button', { name: 'Invoke a band' })).toBeVisible();

  // 5. Serve a band blind. Retry the ring a few times in case it lands empty (live data).
  let served = false;
  for (let attempt = 0; attempt < 4 && !served; attempt++) {
    await page.getByRole('button', { name: 'Invoke a band' }).click();
    const blind = page.getByText('Listen blind');
    const empty = page.getByText('Nothing answers in that ring');
    await expect(blind.or(empty)).toBeVisible();
    served = await blind.isVisible();
  }
  expect(served, 'the ring never served a band in 4 attempts').toBeTruthy();

  // Blind means blind: the reveal marker is absent while listening.
  await expect(page.getByText('Listen blind')).toBeVisible();
  await expect(page.getByText('Summoned', { exact: true })).toHaveCount(0);
  // The preview was fetched through the proxy capability URL, never a raw iTunes/Deezer URL.
  expect(audioProxyHits.length, 'no audio proxy request fired').toBeGreaterThan(0);

  // 6. Summon reveals the band.
  await page.getByRole('button', { name: 'Summon' }).click();
  await expect(page.getByText('Summoned', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Why you were served this' })).toBeVisible();

  // 7. The grimoire grew: the summoned band is now in it.
  await page.getByRole('link', { name: 'Your grimoire →' }).click();
  await expect(page.getByRole('heading', { name: 'Your grimoire' })).toBeVisible();
  await expect(page.locator('a[href^="/artist/"]').first()).toBeVisible();
});

// Last.fm import is a blocked feature (no API key / no match) but must degrade with dignity:
// a designed "not available / no match" message, never a raw error.
test('cold start Last.fm import shows a dignified unavailable state', async ({ page }) => {
  await page.goto('/rite');
  await page.getByRole('button', { name: 'No account — create one' }).click();
  await page.locator('input[type=email]').fill(uniqueEmail('lastfm'));
  await page.locator('input[type=password]').fill('Passw0rd!x2026');
  await page.getByRole('button', { name: 'Create account' }).click();

  await expect(page.getByRole('heading', { name: 'Name what you already love' })).toBeVisible();
  await page.getByPlaceholder('Last.fm username').fill('zzznobodyxyz-e2e');
  await page.getByRole('button', { name: 'Import', exact: true }).click();

  await expect(
    page.getByText(/not available yet|None of your top artists is in the catalogue/),
  ).toBeVisible();
});
