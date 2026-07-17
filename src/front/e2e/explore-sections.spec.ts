import { test, expect, type Page } from '@playwright/test';
import { unfoldSection } from './helpers';

// The Explore hub's folding sections. A reader called the page "inmenso, tarda en cargar": mounting
// it fired six queries and forty-eight cover images for content nobody scrolls to. Folding is the
// fix, so these tests are about COST, not chrome — the assertion that matters is that a folded
// section fires nothing. If the request count does not drop, the feature does not exist.
//
// Why "folded" asserts an exact 0 but "unfolded" only asserts >= 1: main.tsx renders under
// <StrictMode>, which double-invokes effects in dev, so every ENABLED query fetches twice against
// the dev server (twice in dev, once in a production build). That doubling is pre-existing and not
// what these tests are about. Zero is the number that carries the claim, and zero is exact.

// The endpoint behind each foldable section, by its heading.
const SECTION_ENDPOINTS: Array<{ title: string; path: string }> = [
  { title: 'The duration axis', path: '/api/catalogue/duration-axis' },
  { title: 'Rare instruments', path: '/api/instruments/rare' },
  { title: 'The one-album bands', path: '/api/catalogue/one-album' },
  { title: 'The relentless', path: '/api/catalogue/hyperprolific' },
  { title: 'The split network', path: '/api/splits' },
];

const WALL_PATH = '/api/covers/wall';

// Records every API + cover-image request the page makes, from before navigation until stopped.
function recordRequests(page: Page): string[] {
  const seen: string[] = [];
  page.on('request', (req) => {
    const url = req.url();
    if (url.includes('/api/')) {
      seen.push(url);
    }
  });
  return seen;
}

function countMatching(urls: string[], fragment: string): number {
  return urls.filter((u) => u.includes(fragment)).length;
}

test('a folded section fires no request, and unfolding is what pays for it', async ({ page }) => {
  const requests = recordRequests(page);

  await page.goto('/explore');
  await expect(page.getByRole('heading', { name: 'Explore', exact: true })).toBeVisible();
  // The wall is the one section open by default — wait for it to settle so the count is not a race.
  await expect(page.getByRole('heading', { name: 'Wall of covers' }).getByRole('button')).toHaveAttribute(
    'aria-expanded',
    'true',
  );
  await page.waitForLoadState('networkidle');

  // The default page pays for the wall and nothing else. This is the whole feature: five endpoints
  // that used to load on mount now load never, until asked.
  expect(countMatching(requests, WALL_PATH)).toBeGreaterThan(0);
  for (const section of SECTION_ENDPOINTS) {
    expect(countMatching(requests, section.path), `${section.title} must not load while folded`).toBe(0);
  }

  // Unfolding each section fetches that section, and wakes nothing else up with it.
  for (const section of SECTION_ENDPOINTS) {
    await unfoldSection(page, section.title);
    await expect
      .poll(() => countMatching(requests, section.path), { message: `${section.title} must load once unfolded` })
      .toBeGreaterThan(0);
  }

  // Folding and unfolding again does not refetch: React Query still holds it (staleTime 60s). Asserted
  // as "no NEW requests" rather than an absolute, so StrictMode's dev doubling cannot mask a refetch.
  const beforeRefold = countMatching(requests, '/api/instruments/rare');
  await page.getByRole('heading', { name: 'Rare instruments' }).getByRole('button').click();
  await unfoldSection(page, 'Rare instruments');
  await expect(page.getByRole('heading', { name: 'Rare instruments' }).locator('..')).toBeVisible();
  expect(countMatching(requests, '/api/instruments/rare')).toBe(beforeRefold);
});

test('the fold state survives a reload, per reader', async ({ page }) => {
  await page.goto('/explore');

  // Fold the wall, unfold the relentless — the inverse of the default, so nothing passes by accident.
  await page.getByRole('heading', { name: 'Wall of covers' }).getByRole('button').click();
  await unfoldSection(page, 'The relentless');

  await page.reload();

  await expect(page.getByRole('heading', { name: 'Wall of covers' }).getByRole('button')).toHaveAttribute(
    'aria-expanded',
    'false',
  );
  await expect(page.getByRole('heading', { name: 'The relentless' }).getByRole('button')).toHaveAttribute(
    'aria-expanded',
    'true',
  );

  // And the restored state is what drives the cost: a folded wall does not fetch on the next visit.
  const requests = recordRequests(page);
  await page.goto('/explore');
  await page.waitForLoadState('networkidle');
  expect(countMatching(requests, WALL_PATH)).toBe(0);
  expect(countMatching(requests, '/api/catalogue/hyperprolific')).toBeGreaterThan(0);
});

test('corrupt persisted state falls back to the default instead of breaking the page', async ({ page }) => {
  await page.addInitScript(() => {
    window.localStorage.setItem('grimoire-explore-sections', '{"wall": tru');
  });

  await page.goto('/explore');

  // The page renders, and the default (wall open, the rest folded) is what it falls back to.
  await expect(page.getByRole('heading', { name: 'Explore', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Wall of covers' }).getByRole('button')).toHaveAttribute(
    'aria-expanded',
    'true',
  );
  await expect(page.getByRole('heading', { name: 'The split network' }).getByRole('button')).toHaveAttribute(
    'aria-expanded',
    'false',
  );
});

test('every section header stays readable while folded, in both languages', async ({ page }) => {
  await page.goto('/explore');

  // A folded section still announces itself: title, hint, and the state on the control.
  const splits = page.getByRole('heading', { name: 'The split network' });
  await expect(splits).toBeVisible();
  await expect(splits.getByRole('button')).toHaveAttribute('aria-expanded', 'false');
  await expect(splits.locator('..').getByText(/Bands that shared one record/)).toBeVisible();
  await expect(splits.getByRole('button')).toContainText('Show');

  // The language lives in localStorage ('grimoire-lang'), not the URL — same as i18n-routes.spec.
  await page.addInitScript(() => {
    window.localStorage.setItem('grimoire-lang', 'es');
  });
  await page.goto('/explore');

  const splitsEs = page.getByRole('heading', { name: 'La red de splits' });
  await expect(splitsEs).toBeVisible();
  await expect(splitsEs.getByRole('button')).toContainText('Mostrar');
});
