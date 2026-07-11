import { test, expect } from '@playwright/test';

// C5 — the missing link: "I like A and B, what is *between* them?" Interpolates emb = (A+B)/2 and
// returns the real neighbours of that midpoint. Driven through the two-band picker on /lineage,
// scoped to the missing-link tool (Six Degrees lives on the same page and must not be touched).
test('the missing link interpolates real neighbours between two bands', async ({ page }) => {
  await page.goto('/lineage');
  const tool = page.getByRole('heading', { name: 'The missing link' }).locator('..');
  await expect(tool).toBeVisible();

  // From = AC/DC, To = Accept (both Known, both carry an embedding — verified against the DB).
  await tool.getByRole('searchbox').first().fill('AC/DC');
  await tool.getByRole('button', { name: /AC\/DC/ }).first().click();
  await tool.getByRole('searchbox').first().fill('Accept');
  await tool.getByRole('button', { name: /^Accept/ }).first().click();

  // The between-label proves both ends resolved; at least one interpolated band shows a distance.
  await expect(tool.getByText(/Between AC\/DC and Accept/)).toBeVisible();
  const between = tool.locator('ul li a[href^="/artist/"]');
  await expect(between.first()).toBeVisible();
  await expect(tool.getByText(/distance \d/).first()).toBeVisible();

  // The interpolated band clicks through to its own ficha (a real neighbour, not a placeholder).
  await between.first().click();
  await expect(page).toHaveURL(/\/artist\/[0-9a-f-]{36}$/);
});
