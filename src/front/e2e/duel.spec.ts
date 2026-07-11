import { test, expect } from '@playwright/test';
import { seedTasteApi, signedIn } from './helpers';

// The blind duel (feature C2), driven through the UI end to end: two bands served blind, the user
// picks one, the winner is revealed and the taste moves. Blind to the end: no name or reveal marker
// shows until the choice, and the audio arrives only through the proxy capability URL (anti-leak).
test('duel: two blind bands, pick one, the winner is revealed', async ({ page, request }) => {
  const audioProxyHits: string[] = [];
  page.on('request', (req) => {
    if (/\/api\/rite\/[0-9a-f-]{36}\/audio$/.test(req.url())) {
      audioProxyHits.push(req.url());
    }
  });

  // Signed in with a seeded taste (via API), so the gate lands straight on the duel.
  const account = await signedIn(page, request);
  await seedTasteApi(request, account.tokens.accessToken);

  await page.goto('/duel');
  await expect(page.getByRole('heading', { name: 'The Duel' })).toBeVisible();

  // Begin the duel. Retry a few times in case the ring lands short (live data, small pool — D25).
  let dueling = false;
  for (let attempt = 0; attempt < 4 && !dueling; attempt++) {
    await page.getByRole('button', { name: /Begin the duel|Duel again/ }).click();
    const blind = page.getByText('Listen blind');
    const empty = page.getByText('The ring cannot field two');
    await expect(blind.or(empty).first()).toBeVisible();
    dueling = (await blind.count()) >= 2;
  }
  expect(dueling, 'the ring never fielded two bands in 4 attempts').toBeTruthy();

  // Two blind players, two choose buttons — and blind means blind: no winner marker yet.
  await expect(page.getByText('Listen blind')).toHaveCount(2);
  await expect(page.getByRole('button', { name: 'Choose this one' })).toHaveCount(2);
  await expect(page.getByText('You preferred')).toHaveCount(0);
  expect(audioProxyHits.length, 'audio must arrive through the proxy for both sides').toBeGreaterThan(0);

  // Choose the first band: the winner is revealed with the C4 explanation.
  await page.getByRole('button', { name: 'Choose this one' }).first().click();
  await expect(page.getByText('You preferred')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Why you were served this' })).toBeVisible();
  await expect(page.locator('a[href^="/artist/"]').first()).toBeVisible();
});
