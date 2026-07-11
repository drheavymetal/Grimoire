import { test, expect } from '@playwright/test';
import { DARKTHRONE } from './helpers';

const GEALDYR = '25692f8b-d171-4b08-ba23-7e714aa8fd4e'; // a folk band with no member edges (D23)

// A nonexistent artist id must land on the designed 404 state, never a white screen.
test('a nonexistent artist id shows the designed not-found state', async ({ page }) => {
  await page.goto('/artist/00000000-0000-0000-0000-000000000000');
  await expect(page.getByText(/That page has been torn out/)).toBeVisible();
  // The app shell survives: the back-to-search affordance is there.
  await expect(page.getByRole('link', { name: /Back to search/ })).toBeVisible();
});

// A band with no membership edges degrades to the designed empty Gantt, not a crash.
test('a band with no member edges shows the empty lineup state', async ({ page }) => {
  await page.goto(`/artist/${GEALDYR}`);
  await expect(page.locator('h1 span')).toHaveText('Gealdýr');
  // The lineup section is present as a header, and it renders a designed empty state (no member
  // bars) rather than breaking. (Tolerant to live data: if edges arrive, the timeline group
  // appears instead — either way the section does not crash.)
  await expect(page.getByText('Lineup timeline')).toBeVisible();
  const empty = page.getByText(/No lineup traced yet/);
  const gantt = page.getByRole('group', { name: /Lineup timeline/ });
  await expect(empty.or(gantt)).toBeVisible();
});

// Invariant 5 / R2: if the lineage graph itself throws while laying out or painting (a pathological
// payload, a d3-force blow-up at scale), the GraphErrorBoundary degrades that one section to a
// designed empty state and leaves the rest of the ficha standing — never a white screen for the
// whole route. We force the crash by returning a malformed bloodline payload (edges is not an array,
// so the headless layout throws mid-render).
test('a graph that throws while rendering degrades locally, not the whole page', async ({ page }) => {
  await page.route('**/api/lineage/**/bloodline**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        nodes: [{ id: 'x', name: 'X', kind: 'Group', role: 'ego' }],
        edges: 'boom',
      }),
    });
  });

  await page.goto(`/artist/${DARKTHRONE}`);

  // The page shell survives: the artist name still renders.
  await expect(page.locator('h1 span').first()).toHaveText('Darkthrone');
  // The Bloodline section is present, and its graph collapses to the designed boundary fallback
  // rather than tearing down the ficha.
  await expect(page.getByRole('heading', { name: 'Bloodline' })).toBeVisible();
  await expect(page.getByText('Could not draw the lineage.')).toBeVisible();
});
