import { test, expect } from '@playwright/test';
import { DARKTHRONE } from './helpers';

// Bloodline (B16) is the ego graph on any artist ficha: real nodes from the membership graph,
// each a clickable node.
test('the bloodline graph renders real nodes on the ficha', async ({ page }) => {
  await page.goto(`/artist/${DARKTHRONE}`);
  await expect(page.getByRole('heading', { name: 'Bloodline' })).toBeVisible();

  const graph = page.getByRole('group', { name: /Lineage graph/ });
  await expect(graph).toBeVisible();
  // Nodes are role=button (name = the artist name); the ego (Darkthrone) must be one of them.
  await expect(graph.getByRole('button', { name: 'Darkthrone' })).toBeVisible();
  await expect(await graph.getByRole('button').count()).toBeGreaterThan(0);
});

// Six Degrees (B19): the shortest chain of shared members between two bands. Deep Purple and
// Rainbow are connected through Don Airey (verified in the graph) — degree 1.
test('six degrees traces a real path between two connected bands', async ({ page }) => {
  await page.goto('/lineage');
  await expect(page.getByRole('heading', { name: 'Lineage', exact: true })).toBeVisible();

  const six = page.getByRole('heading', { name: 'Six Degrees of Metal' }).locator('..');

  // Pick "From" — the first search box in this tool.
  await six.getByRole('searchbox').first().fill('Deep Purple');
  await six.getByRole('button', { name: /Deep Purple/ }).first().click();
  // Once "From" collapses to a chip, the remaining search box is "To".
  await six.getByRole('searchbox').first().fill('Rainbow');
  await six.getByRole('button', { name: /^Rainbow/ }).first().click();

  // A real path resolves: both endpoints and the connecting member appear.
  await expect(six.getByRole('link', { name: 'Deep Purple' })).toBeVisible();
  await expect(six.getByRole('link', { name: 'Rainbow' })).toBeVisible();
  await expect(six.getByRole('link', { name: 'Don Airey' })).toBeVisible();
});
