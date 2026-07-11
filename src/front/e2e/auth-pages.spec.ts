import { test, expect } from '@playwright/test';
import { registerApi, seedTasteApi, injectAuth } from './helpers';

// The Weekly Rite (B23) and the Mirror (movement V) are auth-gated. Signed in with a seeded
// taste, they render their real content — not the auth panel, not the "seed your taste" gate.

test('the weekly rite renders for a signed-in user with taste', async ({ page, request }) => {
  const account = await registerApi(request);
  await seedTasteApi(request, account.tokens.accessToken);
  await injectAuth(page, account.tokens);

  await page.goto('/weekly');
  await expect(page.getByRole('heading', { name: 'The Weekly Rite' })).toBeVisible();
  // Taste is seeded, so it is not the "seed your taste first" gate.
  await expect(page.getByText('Your taste first')).toHaveCount(0);
  // The week label (Week YYYY-Www) proves the real weekly payload rendered.
  await expect(page.getByText(/Week \d{4}-W\d{2}/)).toBeVisible();
});

test('the mirror renders its sections for a signed-in user with taste', async ({ page, request }) => {
  const account = await registerApi(request);
  await seedTasteApi(request, account.tokens.accessToken);
  await injectAuth(page, account.tokens);

  await page.goto('/mirror');
  await expect(page.getByRole('heading', { name: 'The Mirror', exact: true })).toBeVisible();
  // The reflection section renders (its own designed empty state when there are no summons yet).
  await expect(page.getByRole('heading', { name: 'The mirror', exact: true })).toBeVisible();
});
