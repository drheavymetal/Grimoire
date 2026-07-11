import { test, expect } from '@playwright/test';
import { ACDC, signedIn } from './helpers';

// C22 — gift a discovery, both ends, driven through the UI: the giver wraps a band into a signed,
// blind link; the recipient opens it, hears it blind through the proxy, and only then turns it over.
// A tampered link is a designed "not a real gift", not a crash.
test('wrap a band as a gift, open it blind, and reveal it', async ({ page, request }) => {
  await signedIn(page, request);

  // Giver: on a band with a real preview (AC/DC), wrap the gift and read back the share link.
  await page.goto(`/artist/${ACDC}`);
  await expect(page.locator('h1 span')).toHaveText('AC/DC');
  const giver = page.getByRole('heading', { name: 'Gift this band' }).locator('..');
  // First click opens the note form; the second actually wraps and mints the link.
  await giver.getByRole('button', { name: 'Wrap it as a gift' }).click();
  await expect(giver.locator('input[type=text]')).toBeVisible();
  await giver.getByRole('button', { name: 'Wrap it as a gift' }).click();
  await expect(giver.getByText('Your gift link — share it')).toBeVisible();
  const link = await giver.locator('code').innerText();
  expect(link).toContain('/gift/');

  // The recipient path is /gift/<token>. Pull the token out of the rendered link and open it.
  const token = link.split('/gift/')[1].trim();
  expect(token.length).toBeGreaterThan(20);

  const audioHits: string[] = [];
  page.on('request', (req) => {
    if (/\/api\/gift\/.+\/audio$/.test(req.url())) {
      audioHits.push(req.url());
    }
  });

  await page.goto(`/gift/${token}`);
  await expect(page.getByRole('heading', { name: 'A gift, face down' })).toBeVisible();
  // Blind: the band name is NOT shown before the reveal.
  await expect(page.getByText('AC/DC')).toHaveCount(0);
  // The audio streams through the anti-leak gift proxy, never a raw iTunes/Deezer URL.
  await expect.poll(() => audioHits.length, { timeout: 15_000 }).toBeGreaterThan(0);

  // Turn it over: now the band is revealed and links to its ficha.
  await page.getByRole('button', { name: 'Reveal the band' }).click();
  await expect(page.getByText('It was')).toBeVisible();
  await expect(page.getByRole('heading').filter({ hasText: 'AC/DC' })).toBeVisible();
  await page.getByRole('link', { name: 'Open the full page' }).click();
  await expect(page).toHaveURL(new RegExp(`/artist/${ACDC}$`));
});

// A tampered or invalid token must land on the designed "not a real gift" state (the API answers
// 404 on an unwrappable token — verified), never a white screen or a leaked band.
test('a tampered gift token shows the designed not-a-gift state', async ({ page }) => {
  await page.goto('/gift/not-a-real-token-deadbeef');
  await expect(page.getByText('Not a real gift')).toBeVisible();
});
