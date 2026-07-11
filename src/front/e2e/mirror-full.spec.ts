import { test, expect } from '@playwright/test';
import { registerApi, seedTasteApi, injectAuth } from './helpers';

// The Mirror in full (C20, C16, B25, B18, B23): every section reflects the user's own rite history
// back at them. Round 1 only checked the reflection heading. Here, signed in with a seeded taste,
// all five sections must mount with real content or their designed empty state — and never an error.
test('all five mirror sections render for a signed-in user with taste', async ({ page, request }) => {
  const account = await registerApi(request);
  await seedTasteApi(request, account.tokens.accessToken);
  await injectAuth(page, account.tokens);

  await page.goto('/mirror');
  await expect(page.getByRole('heading', { name: 'The Mirror', exact: true })).toBeVisible();

  // Each section header is present (they render regardless of whether there is data yet).
  await expect(page.getByRole('heading', { name: 'The mirror', exact: true })).toBeVisible(); // C20
  await expect(page.getByRole('heading', { name: 'Your trajectory' })).toBeVisible(); // C16
  await expect(page.getByRole('heading', { name: 'Anti-recommendation' })).toBeVisible(); // B25
  await expect(page.getByRole('heading', { name: 'The Dark Twin' })).toBeVisible(); // B18
  await expect(page.getByRole('heading', { name: 'Your gaps' })).toBeVisible(); // B23

  // A fresh user has no summons yet, so the sections show their designed empty copy, not an error.
  await expect(page.getByText('Loading', { exact: false })).toHaveCount(0);
  const errorBanner = page.locator('.text-danger');
  await expect(errorBanner).toHaveCount(0);

  // The gaps section links onward to the Atlas (the dark regions are the gaps).
  await expect(page.getByRole('link', { name: /See the Atlas/ })).toBeVisible();
});
