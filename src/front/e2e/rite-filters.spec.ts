import { test, expect } from '@playwright/test';
import { registerApi, seedTasteApi, injectAuth } from './helpers';

// C13 — the hard filters on the Rite console: decade and country narrow the ring before a band is
// served. Signed in with a seeded taste, opening the filters and invoking must either serve a band
// (blind) or land on the designed empty-ring state — never an error, never a crash.
test('the rite filters narrow the ring and still serve or degrade cleanly', async ({ page, request }) => {
  const account = await registerApi(request);
  await seedTasteApi(request, account.tokens.accessToken);
  await injectAuth(page, account.tokens);

  await page.goto('/rite');
  // Taste is seeded → the console, not cold start.
  await expect(page.getByRole('button', { name: 'Invoke a band' })).toBeVisible();

  // Open the filters and narrow by country + decade window (an adversarial, deliberately narrow ring).
  await page.getByText('Filters', { exact: true }).click();
  await page.getByPlaceholder('NO', { exact: true }).fill('US');
  await page.getByPlaceholder('1990').fill('1980');
  await page.getByPlaceholder('2010').fill('2015');

  await page.getByRole('button', { name: 'Invoke a band' }).click();

  // Either a band is served blind, or the ring is honestly empty. Both are designed; neither errors.
  const blind = page.getByText('Listen blind');
  const empty = page.getByText('Nothing answers in that ring');
  await expect(blind.or(empty)).toBeVisible();
  await expect(page.getByText('The rite would not begin. Try again.')).toHaveCount(0);
});
