import { test, expect } from '@playwright/test';
import { BEETHOVEN } from './helpers';

// A composer ficha (D11): an artist with works renders the composer body — grouped works and the
// master–apprentice lineage — instead of the band Gantt. Beethoven studied with Haydn.
test('a composer ficha shows works and the teacher lineage', async ({ page }) => {
  await page.goto(`/artist/${BEETHOVEN}`);
  await expect(page.locator('h1 span')).toHaveText('Ludwig van Beethoven');

  // The hero is the grouped list of works.
  await expect(page.getByRole('heading', { name: 'Works' })).toBeVisible();
  // At least one concrete work is listed.
  const works = page.locator('ul li').filter({ hasText: /.+/ });
  await expect(works.first()).toBeVisible();

  // The lineage: Beethoven studied with Joseph Haydn — a clickable link to Haydn's page.
  await expect(page.getByRole('link', { name: 'Joseph Haydn' }).first()).toBeVisible();

  // It is a composer, so there is no band lineup Gantt.
  await expect(page.getByText('Lineup timeline')).toHaveCount(0);
});
