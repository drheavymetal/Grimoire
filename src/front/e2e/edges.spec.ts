import { test, expect } from '@playwright/test';

// A nonexistent label id must land on the designed "no such label" state (the API answers 404 —
// verified), with the back affordance, never a white screen. Round 1 covered the artist 404; the
// label page is a separate not-found path.
test('a nonexistent label id shows the designed not-found state', async ({ page }) => {
  await page.goto('/label/00000000-0000-0000-0000-000000000000');
  await expect(page.getByText('No such label in the grimoire.')).toBeVisible();
  await expect(page.getByRole('link', { name: /All labels/ })).toBeVisible();
});
