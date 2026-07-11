import { test, expect } from '@playwright/test';

// Adversarial auth: the protected pages must gate an anonymous visitor behind the auth panel and
// never leak their content. Grimoire gates in place (shows the sign-in form) rather than redirecting
// — either way, the page's own content heading must be absent for a signed-out user.

const GATED: Array<{ path: string; contentHeading: string }> = [
  { path: '/weekly', contentHeading: 'The Weekly Rite' },
  { path: '/mirror', contentHeading: 'The Mirror' },
  { path: '/grimoire', contentHeading: 'Your grimoire' },
];

for (const { path, contentHeading } of GATED) {
  test(`anonymous visitor to ${path} hits the auth gate, not the content`, async ({ page }) => {
    await page.goto(path);
    // The auth panel is up: the sign-in form fields are present.
    await expect(page.locator('input[type=email]')).toBeVisible();
    await expect(page.locator('input[type=password]')).toBeVisible();
    // The protected content heading is NOT rendered.
    await expect(page.getByRole('heading', { name: contentHeading, exact: true })).toHaveCount(0);
  });
}
