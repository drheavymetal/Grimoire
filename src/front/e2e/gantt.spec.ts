import { test, expect } from '@playwright/test';
import { DARKTHRONE } from './helpers';

// The lineup Gantt (B7/B8) on Darkthrone: member bars, release marks, and hover lighting the
// formation active at a release date (the inactive members dim).
test('the lineup Gantt shows member bars, release marks and hover-highlights the formation', async ({ page }) => {
  await page.goto(`/artist/${DARKTHRONE}`);
  await expect(page.locator('h1 span')).toHaveText('Darkthrone');

  await expect(page.getByText('Lineup timeline')).toBeVisible();
  const gantt = page.getByRole('group', { name: /Lineup timeline/ });
  await expect(gantt).toBeVisible();

  // Member rows (role=link in the gutter) and release marks (role=button) both come from real
  // edges/releases — at least one of each.
  const memberRows = gantt.getByRole('link');
  await expect(memberRows.first()).toBeVisible();
  const releaseMarks = gantt.getByRole('button', { name: /^Release:/ });
  await expect(releaseMarks.first()).toBeVisible();

  // Hover a release mark: the members not in the formation at that date dim (opacity 0.18).
  await releaseMarks.first().hover();
  const dimmed = page.locator('svg g[style*="opacity: 0.18"]');
  await expect(dimmed.first()).toBeVisible();
});
