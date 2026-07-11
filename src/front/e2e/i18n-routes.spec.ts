import { test, expect, type Page } from '@playwright/test';

// i18n across several routes (invariant 7), beyond round 1's single-page toggle. The language is
// persisted in localStorage ('grimoire-lang'), so we set it before load and walk real routes,
// asserting each renders in the chosen language with no leak from the other.
async function setLang(page: Page, lang: 'es' | 'en'): Promise<void> {
  await page.addInitScript((l) => {
    window.localStorage.setItem('grimoire-lang', l);
  }, lang);
}

test('several routes render fully in Spanish', async ({ page }) => {
  await setLang(page, 'es');

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Busca en el grimorio' })).toBeVisible();
  // The nav is Spanish and shows no English label.
  const nav = page.getByRole('navigation');
  await expect(nav.getByRole('link', { name: 'El Rito' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'Explore', exact: true })).toHaveCount(0);

  await page.goto('/explore');
  await expect(page.getByRole('heading', { name: 'Explorar' })).toBeVisible();

  await page.goto('/scenes');
  await expect(page.getByRole('heading', { name: 'Escenas' })).toBeVisible();

  await page.goto('/lineage');
  await expect(page.getByRole('heading', { name: 'Linaje', exact: true })).toBeVisible();
});

test('several routes render fully in English', async ({ page }) => {
  await setLang(page, 'en');

  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Search the grimoire' })).toBeVisible();
  const nav = page.getByRole('navigation');
  await expect(nav.getByRole('link', { name: 'The Rite' })).toBeVisible();
  await expect(nav.getByRole('link', { name: 'El Rito' })).toHaveCount(0);

  await page.goto('/explore');
  await expect(page.getByRole('heading', { name: 'Explore', exact: true })).toBeVisible();

  await page.goto('/scenes');
  await expect(page.getByRole('heading', { name: 'Scenes' })).toBeVisible();
});
