import { expect, type APIRequestContext, type Page } from '@playwright/test';

// Shared helpers for the E2E suite. Auth is exercised through the real API; where a test needs
// to be *inside* an authenticated session but is not itself about the sign-in flow, it registers
// through the API and injects the token pair into localStorage the way the web platform layer
// stores it (grimoire-access-token / grimoire-refresh-token, see platform/authStore.web.ts).

export const API = 'http://localhost:5080';

// Known-stable seed artists (real MusicBrainz MBIDs, core corpus — see CLAUDE.md).
export const DARKTHRONE = 'e1d59c3d-e185-4d54-8b7c-8682a650a6e6';
export const BEETHOVEN = '656c423a-e270-4e8d-b3c1-c1780d712ee8';

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
