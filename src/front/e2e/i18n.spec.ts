import { test, expect } from '@playwright/test';

// i18n from the first commit (invariant 7): toggling the language swaps every string. Default
// is English; the toggle button reads "ES" to switch to Spanish.
test('toggling the language switches the UI between en and es', async ({ page }) => {
  await page.goto('/');
  const nav = page.getByRole('navigation');
  await expect(nav.getByRole('link', { name: 'The Rite' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Search the grimoire' })).toBeVisible();

  await nav.getByRole('button', { name: 'ES', exact: true }).click();

  await expect(nav.getByRole('link', { name: 'El Rito' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Busca en el grimorio' })).toBeVisible();

  // And back to English.
  await nav.getByRole('button', { name: 'EN', exact: true }).click();
  await expect(nav.getByRole('link', { name: 'The Rite' })).toBeVisible();
});
