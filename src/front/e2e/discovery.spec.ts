import { test, expect } from '@playwright/test';

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

// Explore → Compare (B22): two bands side by side. Deep Purple and Rainbow share Don Airey.
test('compare two bands surfaces shared members and distance', async ({ page }) => {
  await page.goto('/explore');
  await expect(page.getByRole('heading', { name: 'Explore', exact: true })).toBeVisible();
  // Rare instruments and the cover wall are both present sections.
  await expect(page.getByRole('heading', { name: 'Rare instruments' })).toBeVisible();

  const cmp = page.getByRole('heading', { name: 'Compare two bands' }).locator('..');
  await cmp.getByRole('searchbox').first().fill('Deep Purple');
  await cmp.getByRole('button', { name: /Deep Purple/ }).first().click();
  await cmp.getByRole('searchbox').first().fill('Rainbow');
  await cmp.getByRole('button', { name: /^Rainbow/ }).first().click();

  // The comparison renders: the labelled dimensions and the shared member.
  await expect(cmp.getByText('Distance in sound', { exact: true })).toBeVisible();
  await expect(cmp.getByText('Shared members', { exact: true })).toBeVisible();
  await expect(cmp.getByRole('link', { name: 'Don Airey' })).toBeVisible();
});
