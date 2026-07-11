import { test, expect } from '@playwright/test';
import { seedTasteApi, signedIn } from './helpers';

// Guess the decade (feature C27), driven through the UI: a band plays blind, the user bets a decade,
// country and subgenre, then it is revealed and scored, and the session scoreboard grows. Anti-leak:
// nothing about the band shows before the bet, and the audio arrives only through the proxy.
test('decade: serve blind, bet, reveal and score, scoreboard grows', async ({ page, request }) => {
  const audioProxyHits: string[] = [];
  page.on('request', (req) => {
    if (/\/api\/rite\/[0-9a-f-]{36}\/audio$/.test(req.url())) {
      audioProxyHits.push(req.url());
    }
  });

  const account = await signedIn(page, request);
  await seedTasteApi(request, account.tokens.accessToken);

  await page.goto('/decade');
  await expect(page.getByRole('heading', { name: 'Guess the Decade' })).toBeVisible();

  // Serve a scorable band. Retry a few times in case the scorable ring lands empty (live data — D25).
  let listening = false;
  for (let attempt = 0; attempt < 4 && !listening; attempt++) {
    await page.getByRole('button', { name: /Serve a band|Next band/ }).click();
    const bet = page.getByText('Your bet');
    const empty = page.getByText('Nothing scorable in reach');
    await expect(bet.or(empty).first()).toBeVisible();
    listening = await bet.isVisible();
  }
  expect(listening, 'the scorable ring never served a band in 4 attempts').toBeTruthy();

  // Blind means blind: no score is shown while listening, and the audio came through the proxy.
  await expect(page.getByText('Listen blind')).toBeVisible();
  await expect(page.getByText(/This round:/)).toHaveCount(0);
  expect(audioProxyHits.length, 'audio must arrive through the proxy').toBeGreaterThan(0);

  // Place a bet: a decade, a country and a subgenre.
  await page.getByLabel('Decade', { exact: true }).selectOption({ index: 0 });
  await page.getByPlaceholder('NO').fill('NO');
  await page.getByPlaceholder('black metal').fill('black metal');
  await page.getByRole('button', { name: 'Reveal and score' }).click();

  // The band is revealed and scored; the running scoreboard shows one round.
  await expect(page.getByText(/This round:/)).toBeVisible();
  await expect(page.getByText(/across 1 rounds/)).toBeVisible();
  await expect(page.locator('a[href^="/artist/"]').first()).toBeVisible();
});
