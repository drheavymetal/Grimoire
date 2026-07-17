import { test, expect } from '@playwright/test';
import { unfoldSection } from './helpers';

// Scenes (B18): a city + decade + sound, with real bands linking to their fichas.
test('scenes list real bands that link to their ficha', async ({ page }) => {
  await page.goto('/scenes');
  await expect(page.getByRole('heading', { name: 'Scenes' })).toBeVisible();
  const bandLink = page.locator('a[href^="/artist/"]').first();
  await expect(bandLink).toBeVisible();
});

// Labels (B21): the label roster, click through to a label's page (the door to its bands).
test('labels list and open a label roster', async ({ page }) => {
  await page.goto('/labels');
  await expect(page.getByRole('heading', { name: 'Labels' })).toBeVisible();
  const labelLink = page.locator('a[href^="/label/"]').first();
  await expect(labelLink).toBeVisible();
  await labelLink.click();
  await expect(page).toHaveURL(/\/label\/[0-9a-f-]{36}$/);
  // The label page shows its name as the heading and at least one roster entry or a designed empty state.
  await expect(page.locator('h1')).toBeVisible();
});

// The Atlas (B20): a star field painted from real xy embeddings.
test('the atlas paints a star field from real coordinates', async ({ page }) => {
  await page.goto('/atlas');
  await expect(page.getByRole('heading', { name: 'The Atlas' })).toBeVisible();
  await expect(page.getByRole('img', { name: /Atlas star field/ })).toBeVisible();
});

// In Memoriam (B17): the dead, with the bands they played in.
test('in memoriam lists the departed with their bands', async ({ page }) => {
  await page.goto('/memoriam');
  await expect(page.getByRole('heading', { name: 'In Memoriam' })).toBeVisible();
  // At least one entry with a band link.
  await expect(page.locator('a[href^="/artist/"]').first()).toBeVisible();
});

// Explore → Compare (B22): two bands side by side. Deep Purple and Ritchie Blackmore's Rainbow share
// Don Airey (and Blackmore, Glover, Joe Lynn Turner).
//
// The band must be pinned by country and year, not by `.first()`. The corpus holds THREE bands named
// "Rainbow" and trigram search returns the two American ones first; the one this test means is the
// GB 1974 band. `.first()` silently compared Deep Purple against a US psychedelic act that shares no
// member with it, so the shared-member assertion was measuring nothing. The picker renders
// "name  country · year", which is what makes the disambiguation expressible as a role name.
test('compare two bands surfaces shared members and distance', async ({ page }) => {
  await page.goto('/explore');
  await expect(page.getByRole('heading', { name: 'Explore', exact: true })).toBeVisible();
  // Rare instruments and the cover wall are both present sections — their headers show even folded.
  await expect(page.getByRole('heading', { name: 'Rare instruments' })).toBeVisible();

  // Compare starts folded; unfold it before driving the pickers.
  const cmp = await unfoldSection(page, 'Compare two bands');
  await cmp.getByRole('searchbox').first().fill('Deep Purple');
  await cmp.getByRole('button', { name: 'Deep Purple GB · 1968' }).first().click();
  await cmp.getByRole('searchbox').first().fill('Rainbow');
  await cmp.getByRole('button', { name: 'Rainbow GB · 1974' }).first().click();

  // The comparison renders: the labelled dimensions and the shared member.
  await expect(cmp.getByText('Distance in sound', { exact: true })).toBeVisible();
  await expect(cmp.getByText('Shared members', { exact: true })).toBeVisible();
  await expect(cmp.getByRole('link', { name: 'Don Airey' })).toBeVisible();
});
