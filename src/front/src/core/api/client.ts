import type { ArtistDetail, ArtistSummary } from '../domain/types';

// The API client is a pure factory: it takes a base URL and an optional fetch
// implementation. It reads no browser globals and no build-time environment, so it
// is fully portable to React Native (which also provides a global fetch).
export interface GrimoireClient {
  searchArtists(query: string, limit: number, signal?: AbortSignal): Promise<ArtistSummary[]>;
  getArtist(id: string, signal?: AbortSignal): Promise<ArtistDetail>;
}

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export function createGrimoireClient(
  baseUrl: string,
  fetchImpl: typeof fetch = fetch,
): GrimoireClient {
  const root = baseUrl.replace(/\/$/, '');

  async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
    const response = await fetchImpl(`${root}${path}`, {
      headers: { Accept: 'application/json' },
      signal,
    });

    if (!response.ok) {
      throw new ApiError(response.status, `Request to ${path} failed with ${response.status}.`);
    }

    return (await response.json()) as T;
  }

  return {
    searchArtists(query, limit, signal) {
      const params = new URLSearchParams({ q: query, limit: String(limit) });
      return getJson<ArtistSummary[]>(`/api/artists?${params.toString()}`, signal);
    },
    getArtist(id, signal) {
      return getJson<ArtistDetail>(`/api/artists/${encodeURIComponent(id)}`, signal);
    },
  };
}
