import { describe, expect, it, vi } from 'vitest';
import { createGrimoireClient } from './client';

// Node environment (no DOM): core stays portable (D12). These bite on the wire contract of the
// blind duel (C2) and guess the decade (C27): the endpoints, the bodies, and the 204 empty state.

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function clientWith(fetchImpl: ReturnType<typeof vi.fn>) {
  return createGrimoireClient('http://api.test', {
    fetchImpl: fetchImpl as unknown as typeof fetch,
    getAccessToken: () => 't',
  });
}

describe('duel client contract', () => {
  it('posts the filters to /duel and parses the two blind sides', async () => {
    const served = {
      left: { token: 'L', audioUrl: 'http://api.test/api/rite/L/audio' },
      right: { token: 'R', audioUrl: 'http://api.test/api/rite/R/audio' },
    };
    const fetchImpl = vi.fn(async () => jsonResponse(served));
    const client = clientWith(fetchImpl);

    const result = await client.duel({ comfort: 0.7 });

    expect(result).toEqual(served);
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/rite/duel');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({
      comfort: 0.7,
      country: null,
      decadeFrom: null,
      decadeTo: null,
    });
  });

  it('returns null when the duel ring cannot field two (HTTP 204)', async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 204 }));
    const client = clientWith(fetchImpl);

    await expect(client.duel({ comfort: 0.5 })).resolves.toBeNull();
  });

  it('posts the winner and loser tokens to /duel/resolve', async () => {
    const reveal = { reveal: { artist: { id: 'a' }, why: { distance: 0.4, sharedTags: [], sharedMembers: [] } } };
    const fetchImpl = vi.fn(async () => jsonResponse(reveal));
    const client = clientWith(fetchImpl);

    await client.resolveDuel('W', 'Lz');

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/rite/duel/resolve');
    expect(JSON.parse(init.body as string)).toEqual({ winnerToken: 'W', loserToken: 'Lz' });
  });
});

describe('decade client contract', () => {
  it('posts the comfort to /decade and parses the blind served band', async () => {
    const served = { token: 'D', audioUrl: 'http://api.test/api/rite/D/audio' };
    const fetchImpl = vi.fn(async () => jsonResponse(served));
    const client = clientWith(fetchImpl);

    const result = await client.serveDecade(0.5);

    expect(result).toEqual(served);
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/rite/decade');
    expect(JSON.parse(init.body as string)).toEqual({ comfort: 0.5 });
  });

  it('returns null when no scorable band is in reach (HTTP 204)', async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 204 }));
    const client = clientWith(fetchImpl);

    await expect(client.serveDecade(0.5)).resolves.toBeNull();
  });

  it('posts the bet to /{token}/guess, nulling the optional fields left blank', async () => {
    const scored = {
      artist: { id: 'a' },
      decade: { guess: '1980s', actual: '1980s', outcome: 'hit', points: 2 },
      country: { guess: '', actual: 'NO', outcome: 'miss', points: 0 },
      subgenre: { guess: 'black', actual: 'black metal', outcome: 'hit', points: 1 },
      totalPoints: 3,
      maxPoints: 4,
    };
    const fetchImpl = vi.fn(async () => jsonResponse(scored));
    const client = clientWith(fetchImpl);

    const result = await client.guessDecade('tok', { decade: 1985, subgenre: 'black' });

    expect(result).toEqual(scored);
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('http://api.test/api/rite/tok/guess');
    expect(JSON.parse(init.body as string)).toEqual({ decade: 1985, country: null, subgenre: 'black' });
  });
});
