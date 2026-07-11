import { test, expect } from '@playwright/test';
import { API, registerApi, signedIn } from './helpers';

// C23 — crossed grimoires: share your code, paste a friend's, and see what each of you has that the
// other lacks. Driven with two real accounts: A is signed into the page, B exists through the API
// and lends A their real grimoire code.
test('cross a real friend code renders the three columns', async ({ page, request }) => {
  await signedIn(page, request); // user A, in the browser session
  const friend = await registerApi(request); // user B, API only

  // B's real grimoire code (a capability the friend would share).
  const codeRes = await request.get(`${API}/api/rite/grimoire/code`, {
    headers: { Authorization: `Bearer ${friend.tokens.accessToken}` },
  });
  expect(codeRes.ok()).toBeTruthy();
  const friendCode = ((await codeRes.json()) as { code: string }).code;
  expect(friendCode.length).toBeGreaterThan(10);

  await page.goto('/grimoire');
  const crossed = page.getByRole('heading', { name: 'Crossed grimoires' }).locator('..');
  await expect(crossed).toBeVisible();
  // A's own code is shown to share.
  await expect(crossed.getByText('Your grimoire code')).toBeVisible();

  await crossed.getByPlaceholder(/./).fill(friendCode);
  await crossed.getByRole('button', { name: 'Cross', exact: true }).click();

  // A valid cross resolves the three columns (both fresh grimoires → designed empty copy in each).
  await expect(crossed.getByText('What they have that you lack')).toBeVisible();
  await expect(crossed.getByText('Common ground')).toBeVisible();
  await expect(crossed.getByText('What you have that they lack')).toBeVisible();
});

// A bogus code is a designed "no grimoire answers to that code", not a raw error (API answers 400).
test('a bogus grimoire code shows the designed invalid state', async ({ page, request }) => {
  await signedIn(page, request);
  await page.goto('/grimoire');
  const crossed = page.getByRole('heading', { name: 'Crossed grimoires' }).locator('..');
  await crossed.getByPlaceholder(/./).fill('not-a-real-code-xyz');
  await crossed.getByRole('button', { name: 'Cross', exact: true }).click();
  await expect(crossed.getByText('No grimoire answers to that code.')).toBeVisible();
});
