import { describe, expect, it, vi } from 'vitest';
import { ApiError, createGrimoireClient } from './client';

// Runs in a plain Node environment (no DOM): core stays portable (D12).
describe('createGrimoireClient', () => {
  it('builds the proxied cover URL from the base URL and mbid', () => {
    const client = createGrimoireClient('http://api.test/');
    expect(client.coverUrl('3ab57384-506c-33ff-9ecc-e5a6134e17bd')).toBe(
      'http://api.test/api/covers/release-group/3ab57384-506c-33ff-9ecc-e5a6134e17bd',
    );
  });

  it('normalises a base URL that already carries the /api prefix', async () => {
    const fetchImpl = vi.fn(
      async () =>
        new Response(JSON.stringify([]), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
    );
    const client = createGrimoireClient('https://grimoire.test/api', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
    });

    await client.searchArtists('darkthrone', 20);

    const [url] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit?];
    expect(url).toContain('https://grimoire.test/api/artists?');
    expect(url).not.toContain('/api/api/');
    expect(client.coverUrl('3ab57384-506c-33ff-9ecc-e5a6134e17bd')).toBe(
      'https://grimoire.test/api/covers/release-group/3ab57384-506c-33ff-9ecc-e5a6134e17bd',
    );
  });

  it('requests the search endpoint with the query and limit', async () => {
    const fetchImpl = vi.fn(
      async () =>
        new Response(JSON.stringify([]), {
          status: 200,
          headers: { 'content-type': 'application/json' },
        }),
    );
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
    });

    await client.searchArtists('darkthrone', 20);

    const [url] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit?];
    expect(url).toContain('/api/artists?');
    expect(url).toContain('q=darkthrone');
    expect(url).toContain('limit=20');
  });

  it('throws ApiError carrying the status on a non-ok response', async () => {
    const fetchImpl = vi.fn(async () => new Response('nope', { status: 404 }));
    const client = createGrimoireClient('http://api.test', {
      fetchImpl: fetchImpl as unknown as typeof fetch,
    });

    await expect(client.getArtist('missing')).rejects.toBeInstanceOf(ApiError);
  });
});
