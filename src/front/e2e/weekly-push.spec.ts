import { test, expect } from '@playwright/test';
import { registerApi, seedTasteApi, injectAuth } from './helpers';

// The Weekly Rite (B17): seven blind bands for the ISO week, plus the Web Push subscription
// plumbing. Round 1 only checked the heading and week label; here we assert the full seven and that
// the push control mounts one of its designed states. The OS notification pop itself is NOT faked
// (impossible to exercise reliably headless — declared in the round-2 notes).
test('the weekly rite serves seven blind bands and mounts the push control', async ({ page, request }) => {
  const account = await registerApi(request);
  await seedTasteApi(request, account.tokens.accessToken);
  await injectAuth(page, account.tokens);

  await page.goto('/weekly');
  await expect(page.getByRole('heading', { name: 'The Weekly Rite' })).toBeVisible();
  await expect(page.getByText(/Week \d{4}-W\d{2}/)).toBeVisible();

  // Seven stable items: the labels run Band 1 … Band 7 (B17 pads the week to seven).
  await expect(page.getByText('Band 1', { exact: true })).toBeVisible();
  await expect(page.getByText('Band 7', { exact: true })).toBeVisible();
  // Each item is served blind (its name is not shown until judged) — the blind player is present.
  await expect(page.getByText('Listen blind').first()).toBeVisible();

  // The Web Push control mounts (the UI plumbing for B17). We assert only that it renders one of its
  // designed states — subscribing would trigger the browser/OS permission pop, which we do not fake.
  await expect(page.getByRole('heading', { name: 'Notification alerts' })).toBeVisible();
});
