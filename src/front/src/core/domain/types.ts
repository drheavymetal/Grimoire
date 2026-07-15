// Domain types shared across platforms. No DOM, no platform coupling.

export type Rank = 'Known' | 'Obscure' | 'Hidden' | 'Forgotten' | 'Nameless';

export type ArtistKind = 'Person' | 'Group' | 'Orchestra' | 'Choir';

export type ReleaseType = 'Album' | 'Ep' | 'Demo' | 'Split' | 'Live' | 'Compilation';

export type EdgeKind =
  | 'MemberOf'
  | 'SideProject'
  | 'Collaboration'
  | 'Teacher'
  | 'Student'
  | 'InfluencedBy';

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
  abstractUrl: string | null;
  imageUrl: string | null;
  links: Record<string, string> | null;
  // The real lyrical subject matter, as Metal Archives records it (not the C21 title-mining
  // approximation). Empty when the band was never matched on Metallum or carries no themes.
  lyricalThemes: string[];
  // The genre string exactly as Metal Archives writes it (e.g. "Melodic Death Metal"). Null when
  // the band has no Metallum match. A caption beside the tags, never a structural field (invariant 5).
  metalArchivesGenre: string | null;
  releases: Release[];
  edges: ArtistEdge[];
}

// ---------------------------------------------------------------------------
// Browse "see all" (2026-07-15): the explicit NAMED door out of a chip. A tag or a
// theme opens a paged grid of the real bands under it — the opposite of the blind rite.
// ---------------------------------------------------------------------------

// One band in a browse grid: enough to render a card and link to the ficha. NOT blind — this
// is the "see all" surface, so the name is shown (like Scenes).
export interface BandCard {
  id: string;
  name: string;
  rank: Rank | null;
  country: string | null;
  kind: ArtistKind;
}

// A page of a browse listing: the total under the tag/theme and the current slice of bands.
export interface BrowseResult {
  total: number;
  bands: BandCard[];
}

// Which theme namespace a needle belongs to: the real Metal Archives lyrical themes, or the C21
// title-mining approximation (its keys are the TitleLexicon ids the ficha renders today).
export type ThemeKind = 'lyrical' | 'mined';

// The optional scope carried into a rite by a chip's "Invocar a ciegas": an arbitrary tag substring
// or a theme needle. All optional — absent means a fully open, blind rite. The rite STAYS blind; a
// scope only narrows the pool, it never reveals name, genre or theme on the card.
export interface RiteScope {
  genreNeedle?: string;
  themeNeedle?: string;
  themeKind?: ThemeKind;
}

// ---------------------------------------------------------------------------
// Movement V — recording features over the tracklist: B5 (discography tracklist),
// C7 (duration as an axis), C21 (song-title mining), C10 (the version graph).
// ---------------------------------------------------------------------------

// One track of a release (B5): its position, title and length in milliseconds. `lengthMs` is null
// when MusicBrainz never timed it — the UI shows an em dash, never a fabricated duration.
export interface Track {
  position: number;
  title: string;
  lengthMs: number | null;
}

// A band on the duration axis (C7): its mean track length in ms over its timed recordings only,
// and the number of tracks that average rests on (the sample size).
export interface ArtistDuration {
  id: string;
  name: string;
  rank: Rank | null;
  country: string | null;
  timedTrackCount: number;
  averageMs: number;
}

// One approximated lyrical theme and how many of a band's titles evoke it (C21).
export interface ThemeCount {
  theme: string;
  count: number;
}

// The title-mining result for a band (C21): the themes its titles evoke (most present first) and
// how many titles the approximation ran over. It is an approximation from titles, not curated.
export interface ArtistThemes {
  titleCount: number;
  themes: ThemeCount[];
}

// One cross-artist cover in the version graph (C10): who was covered, who covered them, the
// MusicBrainz relation, and the covered song's title (the graph edge cannot carry the song).
export interface CoverEdge {
  originalArtistId: string;
  originalArtistName: string;
  coverArtistId: string;
  coverArtistName: string;
  relation: string;
  title: string;
}

// The version graph of a band (C10): the shared graph payload plus the list of individual covers
// with their song titles. Empty when no one has covered this band's recordings.
export interface VersionGraph {
  graph: Graph;
  versions: CoverEdge[];
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
  // Optional genre lane (a RiteGenres key, e.g. "black-metal"). Null/absent = fully open, blind.
  genre?: string | null;
  // Scoped-rite needles (2026-07-15): narrow the blind pool by an arbitrary lowercase tag substring
  // (`genreNeedle`) or a lyrical/mined theme (`themeNeedle` + `themeKind`). All optional and default
  // undefined — backward-compatible with a plain serve. The tasting stays blind either way.
  genreNeedle?: string;
  themeNeedle?: string;
  themeKind?: ThemeKind;
}

// An entry in the user's grimoire: a summoned band and when it was summoned.
export interface GrimoireEntry {
  artist: ArtistSummary;
  resolvedAt: string;
}

// ---------------------------------------------------------------------------
// The blind duel (feature C2, DECISIONS D16): two bands served blind, the user
// picks one. The pairwise preference (Bradley-Terry) teaches the taste more than
// a lone like. A duel is served with the same ServeFilters as a plain serve.
// ---------------------------------------------------------------------------

// One side of a duel, served blind: only the capability token and the proxied audio
// URL. No name, country, cover or genre — the whole point is to judge by ear.
export interface DuelSide {
  token: string;
  audioUrl: string;
}

// The two blind bands of a duel. `null` on the wire (HTTP 204) means the ring could
// not supply two distinct bands — a designed empty state, not an error (D25).
export interface DuelServed {
  left: DuelSide;
  right: DuelSide;
}

// The outcome of a duel: the winner is revealed (it entered the grimoire) and the taste
// moved toward it and away from the loser. Reuses the summon reveal shape.
export interface DuelResult {
  reveal: RiteReveal;
}

// ---------------------------------------------------------------------------
// Guess the decade (feature C27): The Rite with a scoreboard. 45 s blind, the user
// bets a decade, a country and a subgenre, then the band is revealed and scored
// against its real data. The scoreboard is accumulated in the session by the front.
// ---------------------------------------------------------------------------

// One blind band for the decade game: the capability token and the proxied audio URL.
export interface DecadeServed {
  token: string;
  audioUrl: string;
}

// The player's bet. `decade` is any year in the decade (e.g. 1985 for the 1980s);
// country and subgenre are optional — a player bets only what they are sure of.
export interface DecadeGuess {
  decade: number;
  country?: string | null;
  subgenre?: string | null;
}

export type GuessOutcome = 'hit' | 'close' | 'miss';

// One scored dimension: what was bet, the truth, the outcome and the points earned.
export interface DecadeDimension {
  guess: string;
  actual: string;
  outcome: GuessOutcome;
  points: number;
}

// The reveal and score of a decade round: the full band (to develop the name and link
// to the ficha), the three scored dimensions, and the round total.
export interface DecadeScoreResult {
  artist: ArtistDetail;
  decade: DecadeDimension;
  country: DecadeDimension;
  subgenre: DecadeDimension;
  totalPoints: number;
  maxPoints: number;
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

// An edge: a shared-membership link (person↔band), a declared influence (band→band), a shared
// split (band↔band, C9), a pedagogical relation (master→apprentice, movement VII), or a cover /
// version (recording→recording collapsed to artist→artist, C10). The painter draws influence and
// cover dashed accent, teacher solid accent, and the rest a faint solid line.
export interface GraphEdge {
  source: string;
  target: string;
  kind: 'member' | 'influence' | 'split' | 'teacher' | 'cover';
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

// ---------------------------------------------------------------------------
// Movement V — Scenes (B20/C11): a city + decade + tag cluster of bands. Not a
// country map (D17) — the local scene is the unit.
// ---------------------------------------------------------------------------

export interface SceneBand {
  id: string;
  name: string;
  rank: Rank | null;
}

export interface Scene {
  city: string;
  decade: number;
  tag: string;
  size: number;
  bands: SceneBand[];
}

// ---------------------------------------------------------------------------
// Movement V — Labels as a door (B21).
// ---------------------------------------------------------------------------

export interface LabelSummary {
  id: string;
  name: string;
  country: string | null;
  releaseCount: number;
}

export interface LabelRelease {
  id: string;
  mbid: string;
  title: string;
  type: ReleaseType;
  releaseDate: string | null;
  artistId: string;
  artistName: string;
  artistRank: Rank | null;
}

export interface LabelDetail {
  id: string;
  name: string;
  country: string | null;
  releases: LabelRelease[];
}

// ---------------------------------------------------------------------------
// Movement V — catalogue curiosities (C24 one-album, C25 hyperprolific).
// ---------------------------------------------------------------------------

export interface OneAlbumBand {
  id: string;
  name: string;
  rank: Rank | null;
  country: string | null;
  albumId: string;
  albumMbid: string;
  albumTitle: string;
  albumDate: string | null;
}

export interface ProlificBand {
  id: string;
  name: string;
  rank: Rank | null;
  formedYear: number;
  releaseCount: number;
  ratio: number;
}

// ---------------------------------------------------------------------------
// Movement V — compare two bands (B24).
// ---------------------------------------------------------------------------

export interface CompareBand {
  id: string;
  name: string;
  rank: Rank | null;
  country: string | null;
  tags: string[];
}

export interface SharedMember {
  id: string;
  name: string;
}

export interface CompareResult {
  a: CompareBand;
  b: CompareBand;
  sharedTags: string[];
  tagSimilarity: number;
  vectorDistance: number | null;
  sharedMembers: SharedMember[];
}

// ---------------------------------------------------------------------------
// Movement V — semantic search (B2): free text over the embedding space.
// ---------------------------------------------------------------------------

export interface SemanticHit {
  id: string;
  name: string;
  country: string | null;
  formedYear: number | null;
  rank: Rank | null;
  distance: number;
}

// ---------------------------------------------------------------------------
// Movement V — the wall of covers (C6).
// ---------------------------------------------------------------------------

export interface CoverWallItem {
  releaseId: string;
  mbid: string;
  title: string;
  releaseDate: string | null;
  artistId: string;
  artistName: string;
  artistRank: Rank | null;
}

// ---------------------------------------------------------------------------
// Movement V — gift a discovery (C22): the band sent face down and signed.
// ---------------------------------------------------------------------------

// The giver's minted gift: the opaque capability token to share, plus the note echoed back.
export interface Gift {
  token: string;
  note: string | null;
}

// What the recipient sees before deciding: the signed note and the blind audio URL — never the band.
export interface GiftBlind {
  note: string | null;
  audioUrl: string;
}

// ---------------------------------------------------------------------------
// Movement V — crossed grimoires (C23).
// ---------------------------------------------------------------------------

export interface GrimoireCode {
  code: string;
}

export interface CrossedGrimoires {
  theirsOnly: ArtistSummary[];
  yoursOnly: ArtistSummary[];
  shared: ArtistSummary[];
}

// ---------------------------------------------------------------------------
// Movement VI — the Weekly Rite (B17) and its Web Push delivery.
// ---------------------------------------------------------------------------

// One of the week's seven, served blind (SPEC 5.3): the capability token, the risk, the
// proxied audio URL — never the name. `resolved` is true when already judged this week.
export interface WeeklyItem {
  token: string;
  riskPercentile: number;
  audioUrl: string;
  state: RiteState;
  resolved: boolean;
}

// The current ISO week's seven blind bands. Same week -> same seven for everyone.
export interface WeeklyRite {
  weekKey: string;
  items: WeeklyItem[];
}

// The per-subscription tally of a Weekly-Rite push trigger.
export interface NotifyResult {
  sent: number;
  pruned: number;
  failed: number;
}

// ---------------------------------------------------------------------------
// Movement VI — the mirror (C20).
// ---------------------------------------------------------------------------

// "X% of the bands you rejected blind belong to your favourite genre." `hasData` is
// false until there is a favourite genre and something banished to measure against.
export interface Reflection {
  hasData: boolean;
  favouriteTag: string | null;
  banishedTotal: number;
  banishedMatching: number;
  fraction: number;
}

// ---------------------------------------------------------------------------
// Movement VI — your trajectory (C16).
// ---------------------------------------------------------------------------

// One snapshot on the taste path: when, the depth score then, the drift from the previous
// snapshot, and its Atlas-plane projection (null when it could not be placed).
export interface TrajectoryPoint {
  createdAt: string;
  depthScore: number;
  drift: number;
  x: number | null;
  y: number | null;
}

// The whole taste path in chronological order, plus the total drift from first to last.
export interface Trajectory {
  points: TrajectoryPoint[];
  totalDrift: number;
}

// ---------------------------------------------------------------------------
// Movement VI — anti-recommendation (B25).
// ---------------------------------------------------------------------------

// The band the engine predicts you will reject, revealed, with why: how close it sits to
// what you banished, how far from what you love, and which of its tags you rejected.
export interface AntiRecBand {
  id: string;
  name: string;
  country: string | null;
  formedYear: number | null;
  rank: Rank | null;
  tags: string[];
  distanceToRepulsion: number;
  distanceToTaste: number;
  sharedRejectedTags: string[];
}

// The anti-recommendation. `hasData` is false until the user has banished something.
export interface AntiRec {
  hasData: boolean;
  band: AntiRecBand | null;
}

// ---------------------------------------------------------------------------
// Movement VI — the Dark Twin (B18).
// ---------------------------------------------------------------------------

// The user whose taste is closest to yours yet whose collection is most disjoint —
// anonymous. `theirsOnly` is what they have summoned that you have not. `hasData` is
// false with too few users (the honest empty state).
export interface DarkTwin {
  hasData: boolean;
  tasteSimilarity: number;
  disjointness: number;
  sharedCount: number;
  theirsOnly: ArtistSummary[];
}

// ---------------------------------------------------------------------------
// Movement VI — gaps (B23).
// ---------------------------------------------------------------------------

// One untouched region of the catalogue: its label (decade, country or tag) and how many
// bands live there.
export interface GapBucket {
  label: string;
  catalogueCount: number;
}

// The decades, countries and subgenres the user has never summoned — the dark Atlas.
export interface Gaps {
  decades: GapBucket[];
  countries: GapBucket[];
  subgenres: GapBucket[];
}

// ---------------------------------------------------------------------------
// Movement III — per-release credits (B9). Who played what on each release,
// separating official members from guests, plus production. Keyed by release id
// so the artist page matches it to the discography it already holds.
// ---------------------------------------------------------------------------

// A performer on a release: their instruments and whether they were an official member or a
// guest/session player (the D9 distinction — an official member and a guest are different facts).
export interface PerformerCredit {
  artistId: string;
  name: string;
  rank: Rank | null;
  instruments: string[];
  isGuest: boolean;
}

// A production credit: who produced, engineered, mixed or mastered the release.
export interface ProductionCredit {
  artistId: string;
  name: string;
  role: string;
}

// The credits of one release, keyed by releaseId. A release the ETL never reached is simply
// absent from the list, and the front renders a designed "no credits" state for it.
export interface ReleaseCredits {
  releaseId: string;
  performers: PerformerCredit[];
  production: ProductionCredit[];
}

// ---------------------------------------------------------------------------
// Movement III — "the disc where everything changed" (B12): the release with the
// most lineup turnover around its date, and who joined and left near it.
// ---------------------------------------------------------------------------

export interface TurnoverMember {
  id: string;
  name: string;
}

export interface PivotalRelease {
  releaseId: string;
  title: string;
  year: number | null;
  score: number;
  joined: TurnoverMember[];
  left: TurnoverMember[];
}

// ---------------------------------------------------------------------------
// Movement III — In Memoriam (C12): musicians in the grimoire who have died,
// with their death date/place (Wikidata P570/P20) and the bands they played in.
// ---------------------------------------------------------------------------

export interface MemoriamBand {
  id: string;
  name: string;
  rank: Rank | null;
}

export interface MemoriamEntry {
  id: string;
  name: string;
  deathDate: string;
  deathPlace: string | null;
  bands: MemoriamBand[];
}

// ---------------------------------------------------------------------------
// Movement III — rare instruments (C15): the folk/orchestral colour outside the
// standard rock kit, and who plays each.
// ---------------------------------------------------------------------------

export interface RareInstrumentPlayer {
  artistId: string;
  name: string;
  bandId: string;
  bandName: string;
  bandRank: Rank | null;
}

export interface RareInstrument {
  instrument: string;
  playerCount: number;
  players: RareInstrumentPlayer[];
}
