import type {
  AntiRec,
  ArtistDetail,
  ArtistDuration,
  ArtistSummary,
  ArtistThemes,
  Atlas,
  AuthTokens,
  CompareResult,
  CoverWallItem,
  CrossedGrimoires,
  DarkTwin,
  DecadeGuess,
  DecadeScoreResult,
  DecadeServed,
  Diaspora,
  DuelResult,
  DuelServed,
  Gaps,
  Gift,
  GiftBlind,
  Graph,
  GrimoireCode,
  GrimoireEntry,
  LabelDetail,
  LabelSummary,
  MemberBands,
  MemoriamEntry,
  MissingLink,
  NotifyResult,
  OneAlbumBand,
  PathResult,
  PivotalRelease,
  ProlificBand,
  RabbitHole,
  RareInstrument,
  Reflection,
  ReleaseCredits,
  ResolveResult,
  RiteAction,
  Scene,
  SeedCandidate,
  SemanticHit,
  ServedRite,
  ServeFilters,
  TasteStatus,
  Track,
  Trajectory,
  VersionGraph,
  WeeklyRite,
} from '../domain/types';

// A browser push subscription, flattened for the subscribe/unsubscribe endpoints.
export interface PushSubscriptionInput {
  endpoint: string;
  p256dh: string;
  auth: string;
}

// The API client is a pure factory: it takes a base URL, an optional fetch
// implementation, and an optional access-token getter. It reads no browser globals and
// no build-time environment, so it is fully portable to React Native (which also
// provides a global fetch). The token getter is injected by the platform layer, so core
// never touches storage (invariant 6).
export interface GrimoireClient {
  // --- Catalogue (movement I) ---
  searchArtists(query: string, limit: number, signal?: AbortSignal): Promise<ArtistSummary[]>;
  getArtist(id: string, signal?: AbortSignal): Promise<ArtistDetail>;
  /** Per-release credits for a band's discography: performers (member vs guest) and production (B9). */
  artistCredits(id: string, signal?: AbortSignal): Promise<ReleaseCredits[]>;
  /** The release with the most lineup turnover around it (B12). Null when nothing ever changed. */
  pivotalRelease(id: string, signal?: AbortSignal): Promise<PivotalRelease | null>;
  /** The tracklist of one release in a band's discography (B5): position, title, length. */
  releaseTracks(artistId: string, releaseId: string, signal?: AbortSignal): Promise<Track[]>;
  /** The lyrical themes a band's song titles evoke, an approximation (C21). */
  artistThemes(id: string, signal?: AbortSignal): Promise<ArtistThemes>;
  /** The cross-artist covers touching a band's recordings, as a graph plus a list (C10). */
  artistVersions(id: string, signal?: AbortSignal): Promise<VersionGraph>;
  /** Bands ranked by mean track length — the duration axis, funeral doom ↔ grindcore (C7). */
  durationAxis(pole: 'long' | 'short', limit: number, signal?: AbortSignal): Promise<ArtistDuration[]>;
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
  /** The stable cold-start grid: the most-listened bands of every family, taken in turn. */
  seedCandidates(limit: number, signal?: AbortSignal): Promise<SeedCandidate[]>;
  /**
   * The bands nearest to one band, for the grid to unfold underneath it when it is picked (pick
   * Judas Priest, Iron Maiden and the NWOBHM appear below it). The caller drops what it already shows.
   */
  relatedSeeds(artistId: string, limit: number, signal?: AbortSignal): Promise<SeedCandidate[]>;
  seed(artistIds: string[]): Promise<TasteStatus>;
  importLastFm(username: string): Promise<TasteStatus>;
  /** Serves one band blind. Returns null when the ring is empty (HTTP 204). */
  serve(filters: ServeFilters): Promise<ServedRite | null>;
  resolve(token: string, action: RiteAction): Promise<ResolveResult>;
  grimoire(signal?: AbortSignal): Promise<GrimoireEntry[]>;

  // --- The blind duel (C2) ---
  /** Serves two bands blind for a duel. Returns null when the ring cannot supply two (HTTP 204). */
  duel(filters: ServeFilters): Promise<DuelServed | null>;
  /** Resolves a duel: the winner the user preferred over the loser. Moves the taste, reveals the winner. */
  resolveDuel(winnerToken: string, loserToken: string): Promise<DuelResult>;

  // --- Guess the decade (C27) ---
  /** Serves one scorable band blind for the decade game. Returns null when none is in reach (HTTP 204). */
  serveDecade(comfort: number): Promise<DecadeServed | null>;
  /** Scores a decade-game bet and reveals the band. */
  guessDecade(token: string, guess: DecadeGuess): Promise<DecadeScoreResult>;

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

  // --- Movement V — Scenes, Labels, Explore ---
  /** City + decade + tag clusters (B20/C11). */
  scenes(minSize: number, signal?: AbortSignal): Promise<Scene[]>;
  /** Every label that carries a release, most releases first (B21). */
  labels(signal?: AbortSignal): Promise<LabelSummary[]>;
  /** A label's page: its releases, each linking to the band (B21). */
  label(id: string, signal?: AbortSignal): Promise<LabelDetail>;
  /** Bands with exactly one album and nothing else (C24). */
  oneAlbumBands(signal?: AbortSignal): Promise<OneAlbumBand[]>;
  /** Bands that released more than they have lived (C25). */
  hyperprolific(signal?: AbortSignal): Promise<ProlificBand[]>;
  /** Compare two bands by tags, sound and shared members (B24). */
  compare(a: string, b: string, signal?: AbortSignal): Promise<CompareResult>;
  /** Free-text semantic search over the embedding space (B2). */
  semanticSearch(query: string, limit: number, signal?: AbortSignal): Promise<SemanticHit[]>;
  /** A diverse wall of album covers (C6). */
  coverWall(limit: number, signal?: AbortSignal): Promise<CoverWallItem[]>;
  /** The split graph: bands joined by a shared split release (C9). */
  splits(signal?: AbortSignal): Promise<Graph>;

  // --- Movement V — gift a discovery (C22) ---
  /** Wraps a band as a blind, signed gift. Returns the shareable capability token. */
  createGift(artistId: string, note: string | null): Promise<Gift>;
  /** What the recipient sees before deciding: the note and the blind audio URL (never the band). */
  peekGift(token: string, signal?: AbortSignal): Promise<GiftBlind>;
  /** Turns the gift over: reveals the full band. */
  revealGift(token: string): Promise<ArtistDetail>;

  // --- Movement V — crossed grimoires (C23) ---
  /** The caller's own grimoire code, to share with a friend. */
  grimoireCode(signal?: AbortSignal): Promise<GrimoireCode>;
  /** Crosses the caller's grimoire with a friend's (by their code). */
  crossGrimoires(other: string, signal?: AbortSignal): Promise<CrossedGrimoires>;

  // --- Movement VI — Weekly Rite + Web Push (B17) ---
  /** The VAPID public key the browser needs to subscribe to push. */
  vapidPublicKey(signal?: AbortSignal): Promise<string>;
  /** Stores (or refreshes) the caller's browser push subscription. */
  subscribePush(subscription: PushSubscriptionInput): Promise<void>;
  /** Removes the caller's push subscription for an endpoint. */
  unsubscribePush(subscription: PushSubscriptionInput): Promise<void>;
  /** The current ISO week's seven blind bands (same week -> same seven). */
  weekly(signal?: AbortSignal): Promise<WeeklyRite>;
  /** Triggers a Weekly-Rite push to the caller's subscriptions (manual/test). */
  notifyWeekly(): Promise<NotifyResult>;

  // --- Movement VI — the mirror and cartography (C20, C16, B25, B18, B23) ---
  /** The mirror (C20): what fraction of blind rejections match your favourite genre. */
  reflection(signal?: AbortSignal): Promise<Reflection>;
  /** Your taste trajectory over time (C16). */
  trajectory(signal?: AbortSignal): Promise<Trajectory>;
  /** The band predicted to repel you, and why (B25). */
  antiRec(signal?: AbortSignal): Promise<AntiRec>;
  /** The nearest-taste, most-disjoint user (B18). */
  darkTwin(signal?: AbortSignal): Promise<DarkTwin>;
  /** Decades, countries and subgenres you have never summoned (B23). */
  gaps(signal?: AbortSignal): Promise<Gaps>;

  // --- Movement III — In Memoriam (C12) and rare instruments (C15) ---
  /** The musicians in the grimoire who have died, chronological, with their bands (C12). */
  memoriam(signal?: AbortSignal): Promise<MemoriamEntry[]>;
  /** The rare instruments outside the standard rock kit, and who plays each (C15). */
  rareInstruments(signal?: AbortSignal): Promise<RareInstrument[]>;
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
  // The base URL is an ORIGIN — every path below already carries the /api prefix the controllers
  // are routed on. A base that also ends in /api (an easy mistake when the deployment routes by
  // PathPrefix) is normalised away here, so it cannot silently produce /api/api/... 404s.
  const root = baseUrl.replace(/\/+$/, '').replace(/\/api$/, '');
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
    artistCredits(id, signal) {
      return request<ReleaseCredits[]>(`/api/artists/${encodeURIComponent(id)}/credits`, { signal });
    },
    pivotalRelease(id, signal) {
      // 204 No Content when the band's lineup never changed around any dated release.
      return requestMaybe<PivotalRelease>(`/api/artists/${encodeURIComponent(id)}/pivotal-release`, { signal });
    },
    releaseTracks(artistId, releaseId, signal) {
      return request<Track[]>(
        `/api/artists/${encodeURIComponent(artistId)}/releases/${encodeURIComponent(releaseId)}/tracks`,
        { signal },
      );
    },
    artistThemes(id, signal) {
      return request<ArtistThemes>(`/api/artists/${encodeURIComponent(id)}/themes`, { signal });
    },
    artistVersions(id, signal) {
      return request<VersionGraph>(`/api/artists/${encodeURIComponent(id)}/versions`, { signal });
    },
    durationAxis(pole, limit, signal) {
      const params = new URLSearchParams({ pole, limit: String(limit) });
      return request<ArtistDuration[]>(`/api/catalogue/duration-axis?${params.toString()}`, { signal });
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
    relatedSeeds(artistId, limit, signal) {
      const params = new URLSearchParams({ limit: String(limit) });
      return request<SeedCandidate[]>(
        `/api/rite/seed-candidates/${encodeURIComponent(artistId)}/related?${params.toString()}`,
        { auth: true, signal },
      );
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

    duel(filters) {
      return requestMaybe<DuelServed>('/api/rite/duel', {
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
    resolveDuel(winnerToken, loserToken) {
      return request<DuelResult>('/api/rite/duel/resolve', {
        method: 'POST',
        auth: true,
        body: { winnerToken, loserToken },
      });
    },
    serveDecade(comfort) {
      return requestMaybe<DecadeServed>('/api/rite/decade', {
        method: 'POST',
        auth: true,
        body: { comfort },
      });
    },
    guessDecade(token, guess) {
      return request<DecadeScoreResult>(`/api/rite/${encodeURIComponent(token)}/guess`, {
        method: 'POST',
        auth: true,
        body: { decade: guess.decade, country: guess.country ?? null, subgenre: guess.subgenre ?? null },
      });
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

    scenes(minSize, signal) {
      const params = new URLSearchParams({ minSize: String(minSize) });
      return request<Scene[]>(`/api/scenes?${params.toString()}`, { signal });
    },
    labels(signal) {
      return request<LabelSummary[]>('/api/labels', { signal });
    },
    label(id, signal) {
      return request<LabelDetail>(`/api/labels/${encodeURIComponent(id)}`, { signal });
    },
    oneAlbumBands(signal) {
      return request<OneAlbumBand[]>('/api/catalogue/one-album', { signal });
    },
    hyperprolific(signal) {
      return request<ProlificBand[]>('/api/catalogue/hyperprolific', { signal });
    },
    compare(a, b, signal) {
      const params = new URLSearchParams({ a, b });
      return request<CompareResult>(`/api/compare?${params.toString()}`, { signal });
    },
    semanticSearch(query, limit, signal) {
      const params = new URLSearchParams({ q: query, limit: String(limit) });
      return request<SemanticHit[]>(`/api/semantic?${params.toString()}`, { signal });
    },
    coverWall(limit, signal) {
      const params = new URLSearchParams({ limit: String(limit) });
      return request<CoverWallItem[]>(`/api/covers/wall?${params.toString()}`, { signal });
    },
    splits(signal) {
      return request<Graph>('/api/splits', { signal });
    },

    createGift(artistId, note) {
      return request<Gift>('/api/gift', { method: 'POST', auth: true, body: { artistId, note } });
    },
    peekGift(token, signal) {
      return request<GiftBlind>(`/api/gift/${encodeURIComponent(token)}`, { signal });
    },
    revealGift(token) {
      return request<ArtistDetail>(`/api/gift/${encodeURIComponent(token)}/reveal`, { method: 'POST' });
    },

    grimoireCode(signal) {
      return request<GrimoireCode>('/api/rite/grimoire/code', { auth: true, signal });
    },
    crossGrimoires(other, signal) {
      const params = new URLSearchParams({ other });
      return request<CrossedGrimoires>(`/api/rite/grimoire/compare?${params.toString()}`, {
        auth: true,
        signal,
      });
    },

    async vapidPublicKey(signal) {
      const key = await request<{ publicKey: string }>('/api/push/vapid-public-key', { signal });
      return key.publicKey;
    },
    async subscribePush(subscription) {
      // The endpoint returns 204 No Content; requestMaybe tolerates the empty body.
      await requestMaybe<null>('/api/push/subscribe', { method: 'POST', auth: true, body: subscription });
    },
    async unsubscribePush(subscription) {
      await requestMaybe<null>('/api/push/unsubscribe', { method: 'POST', auth: true, body: subscription });
    },
    weekly(signal) {
      return request<WeeklyRite>('/api/weekly', { auth: true, signal });
    },
    notifyWeekly() {
      return request<NotifyResult>('/api/weekly/notify', { method: 'POST', auth: true });
    },

    reflection(signal) {
      return request<Reflection>('/api/mirror/reflection', { auth: true, signal });
    },
    trajectory(signal) {
      return request<Trajectory>('/api/mirror/trajectory', { auth: true, signal });
    },
    antiRec(signal) {
      return request<AntiRec>('/api/mirror/anti-rec', { auth: true, signal });
    },
    darkTwin(signal) {
      return request<DarkTwin>('/api/mirror/dark-twin', { auth: true, signal });
    },
    gaps(signal) {
      return request<Gaps>('/api/mirror/gaps', { auth: true, signal });
    },

    memoriam(signal) {
      return request<MemoriamEntry[]>('/api/memoriam', { signal });
    },
    rareInstruments(signal) {
      return request<RareInstrument[]>('/api/instruments/rare', { signal });
    },
  };
}
