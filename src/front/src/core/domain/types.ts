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
  // The artist on the OTHER end from the one being viewed: the member when viewing a
  // band, the band when viewing a person. The backend resolves it so the lineup timeline
  // (B7/B8) and the member page (B10) can label each row without a second lookup.
  counterpartId: string;
  counterpartName: string;
  counterpartKind: ArtistKind;
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

// ---------------------------------------------------------------------------
// Lineage (movement IV): the graph features B16, B19, B11, B3, C5, C8, C17.
// A graph is nodes (artists) plus edges (relations). The same shapes feed every
// view; the shared GraphCanvas paints them.
// ---------------------------------------------------------------------------

// Which special place a node holds in its view: the ego of a Bloodline, the two
// ends of a Six Degrees path, or a plain node otherwise.
export type GraphNodeRole = 'ego' | 'source' | 'target' | 'node';

export interface GraphNode {
  id: string;
  name: string;
  kind: ArtistKind;
  rank: Rank | null;
  role: GraphNodeRole;
}

// An edge: a shared-membership link (person↔band) or a declared influence (band→band).
export interface GraphEdge {
  source: string;
  target: string;
  kind: 'member' | 'influence';
  label: string | null;
}

export interface Graph {
  nodes: GraphNode[];
  edges: GraphEdge[];
}

// A Six Degrees result (B19): the ordered chain band → member → band …, and the
// band-to-band hop count. An empty `nodes` means the two bands are not connected.
export interface PathResult {
  nodes: GraphNode[];
  degrees: number;
}

// One band a departing member joined next (B11 diaspora).
export interface DiasporaDestination {
  bandId: string;
  bandName: string;
  bandRank: Rank | null;
  joinedYear: string | null;
}

export interface DiasporaMember {
  memberId: string;
  memberName: string;
  leftDate: string | null;
  destinations: DiasporaDestination[];
}

// A band's diaspora (B11): its departed members and where each went.
export interface Diaspora {
  band: GraphNode;
  members: DiasporaMember[];
}

// One band a musician played in (B3), with their stint and instruments.
export interface MemberBand {
  bandId: string;
  bandName: string;
  bandKind: ArtistKind;
  bandRank: Rank | null;
  beginDate: string | null;
  endDate: string | null;
  instruments: string[];
}

// All the bands one musician played in (B3).
export interface MemberBands {
  member: GraphNode;
  bands: MemberBand[];
}

// A neighbour found near the interpolated midpoint of two bands (C5, the missing link).
export interface MissingLinkNeighbour {
  id: string;
  name: string;
  kind: ArtistKind;
  rank: Rank | null;
  distance: number;
}

export interface MissingLink {
  from: GraphNode;
  to: GraphNode;
  between: MissingLinkNeighbour[];
}

// A guided walk through the lineage (C8, Rabbit Hole).
export interface RabbitHole {
  steps: GraphNode[];
}

// ---------------------------------------------------------------------------
// The Atlas (C18/B22): the whole catalogue as a 2D star field. Each star is an
// artist's embedding projected to the plane by the backend (PCA of the centred
// embeddings). The empty regions between the clusters are the gaps (B23).
// ---------------------------------------------------------------------------

// One star: an artist with a 2D position. Rank tints it; null rank is a plain star.
export interface AtlasStar {
  id: string;
  name: string;
  kind: ArtistKind;
  rank: Rank | null;
  x: number;
  y: number;
}

// The signed-in user's taste, projected into the same plane as the stars ("you are here").
// Null when the caller is anonymous or has no taste vector yet.
export interface AtlasTaste {
  x: number;
  y: number;
}

// The Atlas payload: every projected star, plus the caller's taste position when known.
export interface Atlas {
  stars: AtlasStar[];
  taste: AtlasTaste | null;
}
