// Domain types shared across platforms. No DOM, no platform coupling.

export type Rank = 'Known' | 'Obscure' | 'Hidden' | 'Forgotten' | 'Nameless';

export type ArtistKind = 'Person' | 'Group' | 'Orchestra' | 'Choir';

export type ReleaseType = 'Album' | 'Ep' | 'Demo' | 'Split' | 'Live' | 'Compilation';

export type EdgeKind = 'MemberOf' | 'SideProject' | 'Collaboration' | 'Teacher' | 'InfluencedBy';

export interface ArtistSummary {
  id: string;
  name: string;
  country: string | null;
  formedYear: number | null;
  rank: Rank | null;
}

export interface Release {
  id: string;
  mbid: string;
  title: string;
  type: ReleaseType;
  releaseDate: string | null;
  coverUrl: string | null;
}

export interface ArtistEdge {
  fromId: string;
  toId: string;
  kind: EdgeKind;
  beginDate: string | null;
  endDate: string | null;
  instruments: string[];
}

export interface ArtistDetail {
  id: string;
  name: string;
  sortName: string | null;
  kind: ArtistKind;
  country: string | null;
  city: string | null;
  formedYear: number | null;
  dissolvedYear: number | null;
  listeners: number | null;
  rank: Rank | null;
  tags: string[];
  abstract: string | null;
  imageUrl: string | null;
  links: Record<string, string> | null;
  releases: Release[];
  edges: ArtistEdge[];
}

// ---------------------------------------------------------------------------
// Auth (JWT pair returned by /api/auth/*). The tokens are persisted by the
// platform layer, never by core — core only carries the shape.
// ---------------------------------------------------------------------------

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
}

// ---------------------------------------------------------------------------
// The Rite (features B13, B14, C1, C4, C13, C17)
// ---------------------------------------------------------------------------

// Whether the caller already has a taste vector, so the UI knows to run cold start.
export interface TasteStatus {
  hasTaste: boolean;
  summonedCount: number;
  updatedAt: string | null;
}

// A pickable band on the cold-start "choose five" screen. NOT blind: the user is
// choosing bands they already know, so name and origin are shown here.
export interface SeedCandidate {
  id: string;
  name: string;
  country: string | null;
  formedYear: number | null;
}

// A rite served blind (SPEC 5.3): no name, genre, country or cover — only the
// capability token, the risk percentile, and the proxied audio URL. The origin
// preview URL never reaches the client.
export interface ServedRite {
  token: string;
  riskPercentile: number;
  audioUrl: string;
}

export type RiteAction = 'summon' | 'banish' | 'again';

// The resolved state as the API reports it (PascalCase enum over the wire).
export type RiteState = 'Served' | 'Summoned' | 'Banished' | 'Again';

// "Why you were served this" (feature C4), only present on a summon reveal.
export interface RiteExplanation {
  distance: number;
  sharedTags: string[];
  sharedMembers: string[];
}

// The reveal payload after a summon: the full band plus the explanation.
export interface RiteReveal {
  artist: ArtistDetail;
  why: RiteExplanation;
}

// The outcome of resolving a rite. `reveal` is present ONLY on summon.
export interface ResolveResult {
  state: RiteState;
  reveal: RiteReveal | null;
}

// Hard filters for a serve (feature C13): decade and country only. Format and rank
// are deliberately absent — format is not modelled and rank is null, so offering
// either would render a lie.
export interface ServeFilters {
  comfort: number;
  country?: string | null;
  decadeFrom?: number | null;
  decadeTo?: number | null;
}

// An entry in the user's grimoire: a summoned band and when it was summoned.
export interface GrimoireEntry {
  artist: ArtistSummary;
  resolvedAt: string;
}
