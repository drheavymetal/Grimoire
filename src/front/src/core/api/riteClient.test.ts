import { describe, expect, it, vi } from 'vitest';
import { ApiError, createGrimoireClient } from './client';

// Runs in a plain Node environment (no DOM): core stays portable (D12). These bite on the
// exact wire contract of the Rite engine (see docs/progress/rite-engine.md §4).

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

describe('rite client contract', () => {
  it('attaches the injected access token as a Bearer header on authed calls', async () => {
    const fetchImpl = vi.fn(async () => jsonResponse({ hasTaste: false, summonedCount: 0, updatedAt: null }));
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
      getAccessToken: () => 'the-token',
    });

    await client.getTaste();

    const [, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer the-token');
  });

  it('sends no Authorization header when there is no token', async () => {
    const fetchImpl = vi.fn(async () => jsonResponse({ hasTaste: false, summonedCount: 0, updatedAt: null }));
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
    });

    await client.getTaste();

    const [, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBeUndefined();
  });

  it('returns null when serve gets 204 (empty ring), not an error', async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 204 }));
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
      getAccessToken: () => 't',
    });

    await expect(client.serve({ comfort: 0.5 })).resolves.toBeNull();
  });

  it('parses the served rite on 200 and posts the filters', async () => {
    const served = { token: 'abc', riskPercentile: 0.5, audioUrl: 'http://api.test/api/rite/abc/audio' };
    const fetchImpl = vi.fn(async () => jsonResponse(served));
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
      getAccessToken: () => 't',
    });

    const result = await client.serve({ comfort: 0.8, country: 'NO', decadeFrom: 1990, decadeTo: null });

    expect(result).toEqual(served);
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/rite/serve');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({
      comfort: 0.8,
      country: 'NO',
      decadeFrom: 1990,
      decadeTo: null,
      // Sent since the Rite gained an optional genre (D52): absent means "no genre", not "omit".
      genre: null,
    });
  });

  it('surfaces the 503 from a blocked Last.fm import as an ApiError with that status', async () => {
    const fetchImpl = vi.fn(async () => jsonResponse({ message: 'unavailable' }, 503));
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
      getAccessToken: () => 't',
    });

    await expect(client.importLastFm('someone')).rejects.toMatchObject({ status: 503 });
    await expect(client.importLastFm('someone')).rejects.toBeInstanceOf(ApiError);
  });

  it('posts the chosen action to the resolve endpoint for the token', async () => {
    const fetchImpl = vi.fn(async () => jsonResponse({ state: 'Banished', reveal: null }));
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
      getAccessToken: () => 't',
    });

    const result = await client.resolve('tok-1', 'banish');

    expect(result).toEqual({ state: 'Banished', reveal: null });
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/rite/tok-1/resolve');
    expect(JSON.parse(init.body as string)).toEqual({ action: 'banish' });
  });

  it('posts the picked artist ids to seed', async () => {
    const fetchImpl = vi.fn(async () => jsonResponse({ hasTaste: true, summonedCount: 0, updatedAt: null }));
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
      getAccessToken: () => 't',
    });

    await client.seed(['id-1', 'id-2']);

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/rite/seed');
    expect(JSON.parse(init.body as string)).toEqual({ artistIds: ['id-1', 'id-2'] });
  });

  it('reads the atlas star field, with the taste position when present', async () => {
    const payload = {
      stars: [{ id: 'a', name: 'A', kind: 'Group', rank: 'Known', x: 1.5, y: -2.5 }],
      taste: { x: 0.25, y: 0.75 },
    };
    const fetchImpl = vi.fn(async () => jsonResponse(payload));
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
      getAccessToken: () => 't',
    });

    const result = await client.atlas();

    expect(result).toEqual(payload);
    const [url] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/atlas');
  });
});
