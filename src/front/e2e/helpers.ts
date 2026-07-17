import { expect, type APIRequestContext, type Page } from '@playwright/test';

// Shared helpers for the E2E suite. Auth is exercised through the real API; where a test needs
// to be *inside* an authenticated session but is not itself about the sign-in flow, it registers
// through the API and injects the token pair into localStorage the way the web platform layer
// stores it (grimoire-access-token / grimoire-refresh-token, see platform/authStore.web.ts).

export const API = 'http://localhost:5080';

// Known-stable seed artists (real MusicBrainz MBIDs, core corpus — see CLAUDE.md).
export const DARKTHRONE = 'e1d59c3d-e185-4d54-8b7c-8682a650a6e6';
export const BEETHOVEN = '656c423a-e270-4e8d-b3c1-c1780d712ee8';

// Round-2 fixtures, each verified against the live DB before being fixed here.
// AC/DC: rank Known (Redaction cut 100 — crisp) and has a real preview_url (so it is giftable).
export const ACDC = 'ac9b5441-c3f1-445a-993e-4b617011b501';
// Accept: rank Known, has an embedding — the "to" end of the missing-link interpolation.
export const ACCEPT = 'ee812da2-af98-4eac-b351-fc620eecee85';
// Chained and Desperate: rank Nameless (Redaction cut 10 — corroded, nearly illegible).
export const NAMELESS_BAND = 'c0341cce-3b2a-4664-994d-f57bdeeaf07c';
// Black Sabbath: heavy lineup churn → a real pivotal release ("The Eternal Idol", 1987) — B12.
export const BLACK_SABBATH = 'ec38dc15-8c29-4820-9e60-a6bd53cb3961';
// Cradle of Filth: the most member edges in the corpus (41) → a deep, non-repeating rabbit hole — C8.
export const CRADLE_OF_FILTH = 'ff870e77-2260-430a-bcbe-fb70d1509aa2';

const PASSWORD = 'Passw0rd!x2026';

export function uniqueEmail(prefix = 'e2e'): string {
  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}@grimoire.test`;
}

export interface Tokens {
  accessToken: string;
  refreshToken: string;
}

export interface Account {
  email: string;
  password: string;
  tokens: Tokens;
}

export async function registerApi(request: APIRequestContext, email = uniqueEmail()): Promise<Account> {
  const res = await request.post(`${API}/api/auth/register`, {
    data: { email, password: PASSWORD },
  });
  expect(res.ok(), `register failed: ${res.status()} ${await res.text()}`).toBeTruthy();
  const tokens = (await res.json()) as Tokens;
  expect(tokens.accessToken).toBeTruthy();
  expect(tokens.refreshToken).toBeTruthy();
  return { email, password: PASSWORD, tokens };
}

// Seeds the taste vector through the API so a test that is not about cold-start can land straight
// on The Rite / Weekly / Mirror. Picks the first five seed candidates.
export async function seedTasteApi(request: APIRequestContext, accessToken: string): Promise<void> {
  const auth = { Authorization: `Bearer ${accessToken}` };
  const candRes = await request.get(`${API}/api/rite/seed-candidates?limit=8`, { headers: auth });
  expect(candRes.ok()).toBeTruthy();
  const candidates = (await candRes.json()) as Array<{ id: string }>;
  const ids = candidates.slice(0, 5).map((c) => c.id);
  expect(ids.length).toBe(5);
  const seedRes = await request.post(`${API}/api/rite/seed`, {
    headers: auth,
    data: { artistIds: ids },
  });
  expect(seedRes.ok(), `seed failed: ${seedRes.status()}`).toBeTruthy();
}

// Injects the token pair into localStorage before any app script runs, so AuthProvider boots
// already signed in (it will rotate the refresh token on load — that is fine, it is valid 16 days).
export async function injectAuth(page: Page, tokens: Tokens): Promise<void> {
  await page.addInitScript((t) => {
    window.localStorage.setItem('grimoire-access-token', t.accessToken);
    window.localStorage.setItem('grimoire-refresh-token', t.refreshToken);
  }, tokens);
}

// Registers via API + injects the session into the page. Returns the account.
export async function signedIn(page: Page, request: APIRequestContext): Promise<Account> {
  const account = await registerApi(request);
  await injectAuth(page, account.tokens);
  return account;
}

// Unfolds a collapsible section of a hub page (Explore) and returns its container. The section's
// title IS the disclosure button, nested inside the h2 — so the heading stays a direct child of the
// section and `.locator('..')` still resolves the section, as the rest of the suite assumes.
// Returns the section scope so callers can chain queries into it. Idempotent-ish by design: it only
// clicks when the section is folded, so a test can call it without knowing the persisted default.
export async function unfoldSection(page: Page, title: string) {
  const heading = page.getByRole('heading', { name: title, exact: true });
  const toggle = heading.getByRole('button');
  await expect(toggle).toBeVisible();

  if ((await toggle.getAttribute('aria-expanded')) === 'false') {
    await toggle.click();
  }

  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  return heading.locator('..');
}
