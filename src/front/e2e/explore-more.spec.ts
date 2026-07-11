import { test, expect } from '@playwright/test';

// The Explore hub angles round 1 did not exercise deeply: the wall of covers (C6), the rare
// instruments click-through (C15), and the split network graph (C9). Each reads live data and
// degrades to a designed empty state — the assertions tolerate the ETL still populating.

// C6 — the wall of covers: real Cover Art Archive art, each tile a door to the band's ficha.
test('the cover wall paints real art that clicks through to the band', async ({ page }) => {
  await page.goto('/explore');
  const wall = page.getByRole('heading', { name: 'Wall of covers' }).locator('..');
  await expect(wall).toBeVisible();

  const empty = wall.getByText('No covers to show yet.');
  const tile = wall.locator('a[href^="/artist/"]').first();
  await expect(empty.or(tile)).toBeVisible();

  // With live art present, the first tile navigates to a real ficha.
  if (await tile.isVisible()) {
    await tile.click();
    await expect(page).toHaveURL(/\/artist\/[0-9a-f-]{36}$/);
  }
});

// C15 — rare instruments: the folk/orchestral colour outside the rock kit, and who plays it. Each
// player and their band click through. Round 1 only asserted the section header existed.
test('rare instruments click through to the player band ficha', async ({ page }) => {
  await page.goto('/explore');
  const rare = page.getByRole('heading', { name: 'Rare instruments' }).locator('..');
  await expect(rare).toBeVisible();

  // At least one instrument card with a player linking to a band ficha (live corpus has flute etc.).
  const link = rare.locator('a[href^="/artist/"]').first();
  await expect(link).toBeVisible();
  await link.click();
  await expect(page).toHaveURL(/\/artist\/[0-9a-f-]{36}$/);
});

// C9 — the split network: who shared a split release with whom, as a real force graph. Degrades to
// a designed empty state when no split resolves both partners into the corpus.
test('the split network renders a real graph or its designed empty state', async ({ page }) => {
  await page.goto('/explore');
  const splits = page.getByRole('heading', { name: 'The split network' }).locator('..');
  await expect(splits).toBeVisible();

  const emptyText = splits.getByText(/No split resolves both bands/);
  const graph = splits.getByRole('group');
  await expect(emptyText.or(graph)).toBeVisible();
});
