import type {
  ArtistDetail,
  ArtistSummary,
  Atlas,
  AuthTokens,
  Diaspora,
  Graph,
  GrimoireEntry,
  MemberBands,
  MissingLink,
  PathResult,
  RabbitHole,
  ResolveResult,
  RiteAction,
  SeedCandidate,
  ServedRite,
  ServeFilters,
  TasteStatus,
} from '../domain/types';

// The API client is a pure factory: it takes a base URL, an optional fetch
// implementation, and an optional access-token getter. It reads no browser globals and
// no build-time environment, so it is fully portable to React Native (which also
// provides a global fetch). The token getter is injected by the platform layer, so core
// never touches storage (invariant 6).
export interface GrimoireClient {
  // --- Catalogue (movement I) ---
  searchArtists(query: string, limit: number, signal?: AbortSignal): Promise<ArtistSummary[]>;
  getArtist(id: string, signal?: AbortSignal): Promise<ArtistDetail>;
  /**
   * URL of the proxied, disk-cached cover for a release-group MBID. Pure string building
   * (no fetch, no DOM), so it stays portable; the UI feeds it to an <img src>.
   */
  coverUrl(mbid: string): string;

  // --- Auth ---
  register(email: string, password: string): Promise<AuthTokens>;
  login(email: string, password: string): Promise<AuthTokens>;
  refresh(refreshToken: string): Promise<AuthTokens>;

  // --- The Rite ---
  getTaste(signal?: AbortSignal): Promise<TasteStatus>;
  seedCandidates(limit: number, signal?: AbortSignal): Promise<SeedCandidate[]>;
  seed(artistIds: string[]): Promise<TasteStatus>;
  importLastFm(username: string): Promise<TasteStatus>;
  /** Serves one band blind. Returns null when the ring is empty (HTTP 204). */
  serve(filters: ServeFilters): Promise<ServedRite | null>;
  resolve(token: string, action: RiteAction): Promise<ResolveResult>;
  grimoire(signal?: AbortSignal): Promise<GrimoireEntry[]>;

  // --- Lineage (movement IV) ---
  /** The ego graph of an artist: shared members + influence, N hops out (B16). */
  bloodline(id: string, hops: number, signal?: AbortSignal): Promise<Graph>;
  /** Shortest path between two bands by shared members (B19). */
  sixDegrees(from: string, to: string, signal?: AbortSignal): Promise<PathResult>;
  /** Where a broken-up band's members went next (B11). */
  diaspora(id: string, signal?: AbortSignal): Promise<Diaspora>;
  /** Every band a musician played in (B3). */
  memberBands(id: string, signal?: AbortSignal): Promise<MemberBands>;
  /** The bands between two others in embedding space (C5). */
  missingLink(from: string, to: string, signal?: AbortSignal): Promise<MissingLink>;
  /** A guided walk through the lineage (C8). */
  rabbitHole(id: string, length: number, signal?: AbortSignal): Promise<RabbitHole>;
  /** The signed-in user's summoned bands and the edges between them (C17). */
  grimoireGraph(signal?: AbortSignal): Promise<Graph>;

  // --- The Atlas (movement VI, C18/B22) ---
  /**
   * The whole catalogue as a 2D star field. Anonymous callers get just the stars; a signed-in
   * caller with a taste vector also gets their projected "you are here" position.
   */
  atlas(signal?: AbortSignal): Promise<Atlas>;
}

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export interface GrimoireClientOptions {
  fetchImpl?: typeof fetch;
  // Returns the current access token, or null when signed out. Injected by platform.
  getAccessToken?: () => string | null;
}

export function createGrimoireClient(
  baseUrl: string,
  options: GrimoireClientOptions = {},
): GrimoireClient {
  const root = baseUrl.replace(/\/$/, '');
  const fetchImpl = options.fetchImpl ?? fetch;
  const getAccessToken = options.getAccessToken ?? (() => null);

  interface RequestInitLite {
    method?: string;
    body?: unknown;
    signal?: AbortSignal;
    auth?: boolean;
  }

  async function request<T>(path: string, init: RequestInitLite = {}): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' };

    if (init.body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }

    if (init.auth) {
      const token = getAccessToken();
      if (token) {
        headers.Authorization = `Bearer ${token}`;
      }
    }

    const response = await fetchImpl(`${root}${path}`, {
      method: init.method ?? 'GET',
      headers,
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      signal: init.signal,
    });

    // 204 No Content is a legitimate outcome (an empty ring on serve); the caller that
    // needs to see it uses requestMaybe. Everything else that is not ok is an error.
    if (!response.ok) {
      throw new ApiError(response.status, `Request to ${path} failed with ${response.status}.`);
    }

    return (await response.json()) as T;
  }

  // Like request, but returns null on 204 No Content instead of trying to parse a body.
  async function requestMaybe<T>(path: string, init: RequestInitLite = {}): Promise<T | null> {
    const headers: Record<string, string> = { Accept: 'application/json' };

    if (init.body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }

    if (init.auth) {
      const token = getAccessToken();
      if (token) {
        headers.Authorization = `Bearer ${token}`;
      }
    }

    const response = await fetchImpl(`${root}${path}`, {
      method: init.method ?? 'GET',
      headers,
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      signal: init.signal,
    });

    if (response.status === 204) {
      return null;
    }

    if (!response.ok) {
      throw new ApiError(response.status, `Request to ${path} failed with ${response.status}.`);
    }

    return (await response.json()) as T;
  }

  return {
    searchArtists(query, limit, signal) {
      const params = new URLSearchParams({ q: query, limit: String(limit) });
      return request<ArtistSummary[]>(`/api/artists?${params.toString()}`, { signal });
    },
    getArtist(id, signal) {
      return request<ArtistDetail>(`/api/artists/${encodeURIComponent(id)}`, { signal });
    },
    coverUrl(mbid) {
      return `${root}/api/covers/release-group/${encodeURIComponent(mbid)}`;
    },

    register(email, password) {
      return request<AuthTokens>('/api/auth/register', { method: 'POST', body: { email, password } });
    },
    login(email, password) {
      return request<AuthTokens>('/api/auth/login', { method: 'POST', body: { email, password } });
    },
    refresh(refreshToken) {
      return request<AuthTokens>('/api/auth/refresh', { method: 'POST', body: { refreshToken } });
    },

    getTaste(signal) {
      return request<TasteStatus>('/api/rite/taste', { auth: true, signal });
    },
    seedCandidates(limit, signal) {
      const params = new URLSearchParams({ limit: String(limit) });
      return request<SeedCandidate[]>(`/api/rite/seed-candidates?${params.toString()}`, {
        auth: true,
        signal,
      });
    },
    seed(artistIds) {
      return request<TasteStatus>('/api/rite/seed', { method: 'POST', auth: true, body: { artistIds } });
    },
    importLastFm(username) {
      return request<TasteStatus>('/api/rite/import-lastfm', {
        method: 'POST',
        auth: true,
        body: { username },
      });
    },
    serve(filters) {
      return requestMaybe<ServedRite>('/api/rite/serve', {
        method: 'POST',
        auth: true,
        body: {
          comfort: filters.comfort,
          country: filters.country ?? null,
          decadeFrom: filters.decadeFrom ?? null,
          decadeTo: filters.decadeTo ?? null,
        },
      });
    },
    resolve(token, action) {
      return request<ResolveResult>(`/api/rite/${encodeURIComponent(token)}/resolve`, {
        method: 'POST',
        auth: true,
        body: { action },
      });
    },
    grimoire(signal) {
      return request<GrimoireEntry[]>('/api/rite/grimoire', { auth: true, signal });
    },

    bloodline(id, hops, signal) {
      const params = new URLSearchParams({ hops: String(hops) });
      return request<Graph>(`/api/lineage/${encodeURIComponent(id)}/bloodline?${params.toString()}`, { signal });
    },
    sixDegrees(from, to, signal) {
      const params = new URLSearchParams({ from, to });
      return request<PathResult>(`/api/lineage/six-degrees?${params.toString()}`, { signal });
    },
    diaspora(id, signal) {
      return request<Diaspora>(`/api/lineage/${encodeURIComponent(id)}/diaspora`, { signal });
    },
    memberBands(id, signal) {
      return request<MemberBands>(`/api/lineage/${encodeURIComponent(id)}/bands`, { signal });
    },
    missingLink(from, to, signal) {
      const params = new URLSearchParams({ from, to });
      return request<MissingLink>(`/api/lineage/missing-link?${params.toString()}`, { signal });
    },
    rabbitHole(id, length, signal) {
      const params = new URLSearchParams({ length: String(length) });
      return request<RabbitHole>(`/api/lineage/${encodeURIComponent(id)}/rabbit-hole?${params.toString()}`, { signal });
    },
    grimoireGraph(signal) {
      return request<Graph>('/api/lineage/grimoire-graph', { auth: true, signal });
    },

    atlas(signal) {
      // Anonymous-friendly: the endpoint returns stars without a token, and includes the taste
      // position when a valid bearer is attached. auth:true attaches the token only if present.
      return request<Atlas>('/api/atlas', { auth: true, signal });
    },
  };
}
