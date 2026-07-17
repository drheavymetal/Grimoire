import type {
  AnswerRoundResult,
  AntiRec,
  ArtistDetail,
  ArtistDuration,
  ArtistSummary,
  ArtistThemes,
  Atlas,
  AuthTokens,
  BandCard,
  BrowseResult,
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
  Friend,
  FriendAtlasPoint,
  FriendDuel,
  FriendRequests,
  Gaps,
  Gift,
  GiftBlind,
  Graph,
  GrimoireCode,
  GrimoireEntry,
  LabelDetail,
  LabelSummary,
  LeaderboardEntry,
  LogoutAllResult,
  MemberBands,
  MemoriamEntry,
  MissingLink,
  Notification,
  NotifyResult,
  OneAlbumBand,
  PathResult,
  PivotalRelease,
  Profile,
  ProlificBand,
  RabbitHole,
  RareInstrument,
  RebuildResult,
  Reflection,
  ReleaseCredits,
  ReseedMode,
  ReseedResult,
  ResolveResult,
  RiteAction,
  Scene,
  SeedCandidate,
  Session,
  SemanticHit,
  ServedRite,
  ServeFilters,
  TasteStatus,
  ThemeKind,
  Track,
  Trajectory,
  VerdictGame,
  VerdictGameAvailability,
  VerdictGameConsent,
  VerdictGameSummary,
  VerdictGuess,
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
  /** Revokes the current session's refresh token (D28). Best-effort on sign out. Returns 204. */
  logout(refreshToken: string): Promise<void>;
  /** Revokes every session of the caller ("log out everywhere"). Returns how many were revoked. */
  logoutAll(): Promise<LogoutAllResult>;
  /** The caller's active sessions, one per sign-in, with the current one flagged (D28). */
  sessions(signal?: AbortSignal): Promise<Session[]>;

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

  // --- Browse "see all" (the named door out of a chip) ---
  /** Every band under a raw lowercase tag substring, paged. NAMED (not blind) — the "see all" door. */
  browseByTag(needle: string, skip: number, take: number, signal?: AbortSignal): Promise<BrowseResult>;
  /** Every band under a theme (real lyrical or C21 mined), paged. NAMED — the "see all" door. */
  browseByTheme(key: string, kind: ThemeKind, skip: number, take: number, signal?: AbortSignal): Promise<BrowseResult>;

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

  // --- The user profile (2026-07-15) ---
  /** The signed-in listener's profile: depth score, counts, deepest cut, and the shape of the grimoire. */
  getProfile(signal?: AbortSignal): Promise<Profile>;
  /** The bands the listener has pinned as taste anchors (the editable seed set). */
  getAnchors(signal?: AbortSignal): Promise<BandCard[]>;
  /** Pins a band as a taste anchor. Idempotent server-side; returns nothing (204). */
  addAnchor(artistId: string): Promise<void>;
  /** Unpins a taste anchor. Returns nothing (204). */
  removeAnchor(artistId: string): Promise<void>;
  /** Re-seeds the taste vector from the pinned anchors' mean. 400 when no anchor is usable. */
  rebuildTaste(): Promise<RebuildResult>;
  /**
   * Reselects the taste from a fresh pick of bands (the sign-up cold-start picker, re-run from the
   * profile). `"fresh"` replaces the anchors and overwrites the taste with the picks' mean; `"add"`
   * unions the picks into the anchors and rebuilds the taste from all of them. 400 when no picked
   * band is usable.
   */
  reseed(artistIds: string[], mode: ReseedMode): Promise<ReseedResult>;
  /** Sets the caller's public handle. 204 on success, 409 taken, 400 bad format (3–30 [a-z0-9_]). */
  updateHandle(handle: string): Promise<void>;
  /**
   * URL of the authenticated grimoire export (a JSON attachment). Pure string building (no fetch,
   * no DOM), so it stays portable; the platform layer fetches it with the bearer and saves the blob.
   */
  profileExportUrl(): string;

  // --- Friends (the FRIENDS wave) ---
  /** The caller's confirmed friends, with each one's rarity numbers and the friendship-edge id. */
  friends(signal?: AbortSignal): Promise<Friend[]>;
  /** The caller's pending friend requests, split into incoming and outgoing. */
  friendRequests(signal?: AbortSignal): Promise<FriendRequests>;
  /**
   * Adds a friend by their handle (or accepts a matching incoming request). 404 unknown handle,
   * 400 adding yourself, 409 already friends/pending. Returns nothing on success.
   */
  requestFriend(handle: string): Promise<void>;
  /** Accepts an incoming friend request by its friendship id. Returns 204. */
  acceptFriend(friendshipId: string): Promise<void>;
  /** Declines an incoming friend request by its friendship id. Returns 204. */
  declineFriend(friendshipId: string): Promise<void>;
  /** Removes a confirmed friend by the friendship id. Returns 204. */
  removeFriend(friendshipId: string): Promise<void>;
  /** Blocks a user by their user id. Returns 204. */
  blockUser(userId: string): Promise<void>;
  /** Unblocks a user by their user id. Returns 204. */
  unblockUser(userId: string): Promise<void>;
  /** The rarity leaderboard: the caller and their friends ranked by Depth Score. */
  leaderboard(signal?: AbortSignal): Promise<LeaderboardEntry[]>;
  /** A friend's grimoire — the same shape as the caller's own. 403 when not friends. */
  friendGrimoire(friendId: string, signal?: AbortSignal): Promise<GrimoireEntry[]>;
  /** Crosses the caller's grimoire with a friend's. Same shape as the compare. 403 when not friends. */
  friendCrossed(friendId: string, signal?: AbortSignal): Promise<CrossedGrimoires>;
  /** A friend's taste projected into the Atlas plane. Both coords null when they have no taste yet. */
  friendAtlasPoint(friendId: string, signal?: AbortSignal): Promise<FriendAtlasPoint>;
  /**
   * Sends a friend a blind gift of a band. 403 when not friends, 404 when the artist is missing.
   * Returns nothing (204). The recipient hears it blind — the name never reaches them until reveal.
   */
  giftToFriend(friendId: string, artistId: string): Promise<void>;
  /** A taste duel with a friend: the two Depth Scores, the crossed counts, and taste alignment. 403 when not friends. */
  friendDuel(friendId: string, signal?: AbortSignal): Promise<FriendDuel>;
  /** Challenges a friend to a taste duel (drops a notification on their side). Returns 204. 403 when not friends. */
  challengeDuel(friendId: string): Promise<void>;

  // --- Notifications (the NOTIFICATIONS wave): a polled in-app inbox, NOT web push ---
  /** A page of the caller's notifications, newest first. */
  notifications(skip: number, take: number, signal?: AbortSignal): Promise<Notification[]>;
  /** How many notifications the caller has not read yet — the sidebar badge (polled ~60s). */
  unreadCount(signal?: AbortSignal): Promise<number>;
  /** Marks one notification as read. Returns 204. */
  markRead(id: string): Promise<void>;
  /** Marks every notification as read; returns how many were marked. */
  markAllRead(): Promise<number>;

  // --- The GAMES wave: "did you summon it, or banish it?" ---
  /** Whether the caller lets friends play the verdict game against their grimoire (null = never asked). */
  verdictGameConsent(signal?: AbortSignal): Promise<VerdictGameConsent>;
  /** Sets that consent. Returns 204. */
  setVerdictGameConsent(optIn: boolean): Promise<void>;
  /** Whether a friend can be played right now, and the honest reason when not. 403 when not friends. */
  verdictGameAvailability(friendId: string, signal?: AbortSignal): Promise<VerdictGameAvailability>;
  /** Deals a new verdict game against a friend. 403 when not friends or not opted in; 409 when their grimoire cannot make one. */
  startVerdictGame(opponentId: string): Promise<VerdictGame>;
  /** Reads one of the caller's games — how the console resumes after a reload. Rounds stay blind until answered. */
  verdictGame(gameId: string, signal?: AbortSignal): Promise<VerdictGame>;
  /** The caller's games, newest first: the ones they played and the ones played against them. */
  verdictGames(signal?: AbortSignal): Promise<VerdictGameSummary[]>;
  /** Answers a round: `summon` or `banish`. Reveals the band and returns the running score. */
  answerVerdictRound(token: string, verdict: VerdictGuess): Promise<AnswerRoundResult>;

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
    async logout(refreshToken) {
      // 204 No Content on success; requestMaybe tolerates the empty body.
      await requestMaybe<null>('/api/auth/logout', {
        method: 'POST',
        auth: true,
        body: { refreshToken },
      });
    },
    logoutAll() {
      return request<LogoutAllResult>('/api/auth/logout-all', { method: 'POST', auth: true });
    },
    sessions(signal) {
      return request<Session[]>('/api/auth/sessions', { auth: true, signal });
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
          genre: filters.genre ?? null,
          // The three scope needles are backward-compatible: JSON.stringify drops the undefined
          // keys, so a plain serve sends exactly what it did before (contract 2026-07-15).
          genreNeedle: filters.genreNeedle,
          themeNeedle: filters.themeNeedle,
          themeKind: filters.themeKind,
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

    browseByTag(needle, skip, take, signal) {
      const params = new URLSearchParams({ skip: String(skip), take: String(take) });
      return request<BrowseResult>(
        `/api/browse/tag/${encodeURIComponent(needle)}?${params.toString()}`,
        { signal },
      );
    },
    browseByTheme(key, kind, skip, take, signal) {
      const params = new URLSearchParams({ kind, skip: String(skip), take: String(take) });
      return request<BrowseResult>(
        `/api/browse/theme/${encodeURIComponent(key)}?${params.toString()}`,
        { signal },
      );
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

    getProfile(signal) {
      return request<Profile>('/api/profile', { auth: true, signal });
    },
    getAnchors(signal) {
      return request<BandCard[]>('/api/profile/anchors', { auth: true, signal });
    },
    async addAnchor(artistId) {
      // The endpoint returns 204 No Content; requestMaybe tolerates the empty body.
      await requestMaybe<null>('/api/profile/anchors', {
        method: 'POST',
        auth: true,
        body: { artistId },
      });
    },
    async removeAnchor(artistId) {
      await requestMaybe<null>(`/api/profile/anchors/${encodeURIComponent(artistId)}`, {
        method: 'DELETE',
        auth: true,
      });
    },
    rebuildTaste() {
      return request<RebuildResult>('/api/profile/rebuild-taste', { method: 'POST', auth: true });
    },
    reseed(artistIds, mode) {
      return request<ReseedResult>('/api/profile/reseed', {
        method: 'POST',
        auth: true,
        body: { artistIds, mode },
      });
    },
    async updateHandle(handle) {
      // 204 on success; 409 (taken) and 400 (bad format) surface as ApiError for the caller to read.
      await requestMaybe<null>('/api/profile/handle', {
        method: 'PUT',
        auth: true,
        body: { handle },
      });
    },
    profileExportUrl() {
      return `${root}/api/profile/export`;
    },

    friends(signal) {
      return request<Friend[]>('/api/friends', { auth: true, signal });
    },
    friendRequests(signal) {
      return request<FriendRequests>('/api/friends/requests', { auth: true, signal });
    },
    async requestFriend(handle) {
      // Success may be 200/201/204; requestMaybe tolerates them. 404/400/409 surface as ApiError.
      await requestMaybe<null>('/api/friends/request', {
        method: 'POST',
        auth: true,
        body: { handle },
      });
    },
    async acceptFriend(friendshipId) {
      await requestMaybe<null>(`/api/friends/${encodeURIComponent(friendshipId)}/accept`, {
        method: 'POST',
        auth: true,
      });
    },
    async declineFriend(friendshipId) {
      await requestMaybe<null>(`/api/friends/${encodeURIComponent(friendshipId)}/decline`, {
        method: 'POST',
        auth: true,
      });
    },
    async removeFriend(friendshipId) {
      await requestMaybe<null>(`/api/friends/${encodeURIComponent(friendshipId)}`, {
        method: 'DELETE',
        auth: true,
      });
    },
    async blockUser(userId) {
      await requestMaybe<null>(`/api/friends/${encodeURIComponent(userId)}/block`, {
        method: 'POST',
        auth: true,
      });
    },
    async unblockUser(userId) {
      await requestMaybe<null>(`/api/friends/${encodeURIComponent(userId)}/block`, {
        method: 'DELETE',
        auth: true,
      });
    },
    leaderboard(signal) {
      return request<LeaderboardEntry[]>('/api/friends/leaderboard', { auth: true, signal });
    },
    friendGrimoire(friendId, signal) {
      return request<GrimoireEntry[]>(`/api/friends/${encodeURIComponent(friendId)}/grimoire`, {
        auth: true,
        signal,
      });
    },
    friendCrossed(friendId, signal) {
      return request<CrossedGrimoires>(`/api/friends/${encodeURIComponent(friendId)}/crossed`, {
        auth: true,
        signal,
      });
    },
    friendAtlasPoint(friendId, signal) {
      return request<FriendAtlasPoint>(`/api/friends/${encodeURIComponent(friendId)}/atlas-point`, {
        auth: true,
        signal,
      });
    },
    async giftToFriend(friendId, artistId) {
      // 204 on success; 403 (not friends) and 404 (artist missing) surface as ApiError.
      await requestMaybe<null>(`/api/friends/${encodeURIComponent(friendId)}/gift`, {
        method: 'POST',
        auth: true,
        body: { artistId },
      });
    },
    friendDuel(friendId, signal) {
      return request<FriendDuel>(`/api/friends/${encodeURIComponent(friendId)}/duel`, {
        auth: true,
        signal,
      });
    },
    async challengeDuel(friendId) {
      // 204 on success; 403 (not friends) surfaces as ApiError.
      await requestMaybe<null>(`/api/friends/${encodeURIComponent(friendId)}/duel/challenge`, {
        method: 'POST',
        auth: true,
      });
    },

    notifications(skip, take, signal) {
      const params = new URLSearchParams({ skip: String(skip), take: String(take) });
      return request<Notification[]>(`/api/notifications?${params.toString()}`, { auth: true, signal });
    },
    async unreadCount(signal) {
      const body = await request<{ count: number }>('/api/notifications/unread-count', {
        auth: true,
        signal,
      });
      return body.count;
    },
    async markRead(id) {
      // The endpoint returns 204 No Content; requestMaybe tolerates the empty body.
      await requestMaybe<null>(`/api/notifications/${encodeURIComponent(id)}/read`, {
        method: 'POST',
        auth: true,
      });
    },
    verdictGameConsent(signal) {
      return request<VerdictGameConsent>('/api/games/verdict/consent', { auth: true, signal });
    },
    async setVerdictGameConsent(optIn) {
      // 204 on success; requestMaybe tolerates the empty body.
      await requestMaybe<null>('/api/games/verdict/consent', {
        method: 'PUT',
        auth: true,
        body: { optIn },
      });
    },
    verdictGameAvailability(friendId, signal) {
      return request<VerdictGameAvailability>(
        `/api/games/verdict/availability/${encodeURIComponent(friendId)}`,
        { auth: true, signal },
      );
    },
    startVerdictGame(opponentId) {
      return request<VerdictGame>('/api/games/verdict', {
        method: 'POST',
        auth: true,
        body: { opponentId },
      });
    },
    verdictGame(gameId, signal) {
      return request<VerdictGame>(`/api/games/verdict/${encodeURIComponent(gameId)}`, {
        auth: true,
        signal,
      });
    },
    verdictGames(signal) {
      return request<VerdictGameSummary[]>('/api/games/verdict', { auth: true, signal });
    },
    answerVerdictRound(token, verdict) {
      return request<AnswerRoundResult>(
        `/api/games/rounds/${encodeURIComponent(token)}/answer`,
        { method: 'POST', auth: true, body: { verdict } },
      );
    },

    async markAllRead() {
      const body = await request<{ marked: number }>('/api/notifications/read-all', {
        method: 'POST',
        auth: true,
      });
      return body.marked;
    },

    memoriam(signal) {
      return request<MemoriamEntry[]>('/api/memoriam', { signal });
    },
    rareInstruments(signal) {
      return request<RareInstrument[]>('/api/instruments/rare', { signal });
    },
  };
}
