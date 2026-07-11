import { test, expect } from '@playwright/test';
import { ACDC, NAMELESS_BAND, BLACK_SABBATH, CRADLE_OF_FILTH } from './helpers';

// B12 — "the disc where everything changed": the release with the most lineup turnover around it.
// Black Sabbath churned hard around The Eternal Idol (1987); the callout names it and marks the
// release row with the turning-point badge. Only shown when the churn is real — never invented.
test('the pivotal-release callout names the disc where the lineup changed', async ({ page }) => {
  await page.goto(`/artist/${BLACK_SABBATH}`);
  await expect(page.locator('h1 span')).toHaveText('Black Sabbath');

  const callout = page.getByRole('heading', { name: 'The disc where everything changed' }).locator('..');
  await expect(callout).toBeVisible();
  // The named release and at least one member who joined or left around it.
  await expect(callout.getByText('The Eternal Idol')).toBeVisible();
  await expect(callout.locator('a[href^="/artist/"]').first()).toBeVisible();

  // The same release carries the turning-point badge down in the discography.
  await expect(page.getByText('Turning point').first()).toBeVisible();
});

// C8 — Rabbit Hole: an opt-in guided walk down the lineage, each step chosen by the previous band's
// connections. It is not fetched until the user falls in, and the walk does not repeat itself.
test('the rabbit hole walks a non-repeating chain and each step clicks through', async ({ page }) => {
  await page.goto(`/artist/${CRADLE_OF_FILTH}`);
  await expect(page.locator('h1 span')).toHaveText('Cradle of Filth');

  const hole = page.locator('section').filter({ has: page.getByRole('heading', { name: 'Rabbit hole' }) });
  await expect(hole).toBeVisible();
  // Nothing is fetched before the user opts in: no steps rendered yet.
  await expect(hole.locator('ol li')).toHaveCount(0);

  await hole.getByRole('button', { name: 'Fall in' }).click();

  // A real walk resolves: more than one step (Cradle has the most edges in the corpus).
  const steps = hole.locator('ol li');
  await expect(steps.first()).toBeVisible();
  expect(await steps.count()).toBeGreaterThan(1);

  // The walk does not repeat: the first two step names differ.
  const first = await steps.nth(0).innerText();
  const second = await steps.nth(1).innerText();
  expect(first).not.toBe(second);

  // A step clicks through to a real ficha.
  await steps.nth(1).getByRole('link').click();
  await expect(page).toHaveURL(/\/artist\/[0-9a-f-]{36}$/);
});

// Typographic degradation (Q1 / DESIGN §3): the name is rendered in the Redaction cut its rank
// earns. A Known band reads crisp (cut 100); a Nameless band is corroded (cut 10). The cut CHANGES
// with the rank — the typography is the datum, not decoration.
test('the band name corrodes by rank: Known is crisp, Nameless is eroded', async ({ page }) => {
  await page.goto(`/artist/${ACDC}`);
  const knownName = page.locator('h1 span').first();
  await expect(knownName).toHaveText('AC/DC');
  // Known -> Redaction 100 (the crispest cut).
  await expect(knownName).toHaveAttribute('style', /Redaction 100/);

  await page.goto(`/artist/${NAMELESS_BAND}`);
  const namelessName = page.locator('h1 span').first();
  await expect(namelessName).toHaveText('Chained and Desperate');
  // Nameless -> Redaction 10 (the most corroded). The trailing quote distinguishes 10 from 100
  // (the browser normalises the inline font-family to double quotes: "Redaction 10").
  await expect(namelessName).toHaveAttribute('style', /Redaction 10"/);
  await expect(namelessName).not.toHaveAttribute('style', /Redaction 100/);
});
