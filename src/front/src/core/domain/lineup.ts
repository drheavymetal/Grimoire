// Pure, portable logic for the Lineup Timeline — the Gantt (features B7, B8, B10).
//
// This file is the whole render technique's brain: it owns the interval logic and the
// x-from-year / y-from-row layout. It touches NO DOM and imports nothing from ui/ or
// platform/ (invariant 6, D12/D18): the ui/ layer paints the returned numbers with SVG
// primitives (react-native-svg accepts the same). It uses its OWN technique — not
// d3-force, not react-force-graph — because a timeline's axes are deterministic, not a
// force simulation. Auto-fit to the year range happens by transforming positions in JS
// here (never by scaling an SVG <g>), so stroke widths stay honest.
//
// Everything here is a pure function of its inputs and is tested without a browser.

import type { ArtistEdge } from './types';

// ---------------------------------------------------------------------------
// B8 — which members were active on a given date (interval intersection).
//
// Faithful TypeScript port of the C# LineupIntervalResolver.MembersActiveOn
// (src/shared/GrimoireLibrary/Services/LineupIntervalResolver.cs). An edge is active
// when its interval contains the date, treating BOTH ends as inclusive. A null
// beginDate is an open start (always begun); a null endDate is an open end (still
// active). Only MemberOf edges count. Inverting either bound (inclusive -> exclusive,
// or swapping the comparison) breaks the mirrored tests — that is the point.
// ---------------------------------------------------------------------------

// Normalise an ISO date to its comparable 'YYYY-MM-DD' prefix. Equal-length zero-padded
// ISO dates compare lexicographically in chronological order, so no Date object (and no
// timezone hazard) is needed.
function isoDay(iso: string): string {
  return iso.slice(0, 10);
}

export function membersActiveOn(edges: readonly ArtistEdge[], date: string): ArtistEdge[] {
  const day = isoDay(date);
  const active: ArtistEdge[] = [];

  for (const edge of edges) {
    if (edge.kind !== 'MemberOf') {
      continue;
    }

    const startedByDate = edge.beginDate === null || isoDay(edge.beginDate) <= day;
    const notYetEnded = edge.endDate === null || isoDay(edge.endDate) >= day;

    if (startedByDate && notYetEnded) {
      active.push(edge);
    }
  }

  return active;
}

// ---------------------------------------------------------------------------
// Instrument -> colour (stable map + legend).
//
// Raw MusicBrainz instrument strings are messy ("drums (drum set)", "electric bass
// guitar", "lead vocals"). They are folded into a small, stable set of families so the
// bar colour is legible and the legend is short. Rare instruments (violin, bagpipe,
// hurdy gurdy…) fold to 'other' on purpose — the rare-instrument feature (C15) is a
// later wave. No instrument at all also folds to 'other' (a neutral colour, never a
// guess). Order of the checks matters: "bass guitar" contains "guitar", so bass is
// tested before guitar.
// ---------------------------------------------------------------------------

export type InstrumentFamily = 'guitar' | 'bass' | 'drums' | 'vocals' | 'keys' | 'other';

// The families in legend order. Kept as data (single words, portable) so the ui/ legend
// iterates it and localises each label, rather than hardcoding a list.
export const INSTRUMENT_FAMILIES: readonly InstrumentFamily[] = [
  'vocals',
  'guitar',
  'bass',
  'drums',
  'keys',
  'other',
];

// Family -> colour, as plain colour strings (portable data: react-native-svg takes the
// same strings; no CSS variable, which would not survive the RN port). A restrained,
// muted palette that reads on both the paper (light) and void (dark) backgrounds, and
// deliberately avoids acid green (the metal-app cliché, DESIGN 5) and oxblood (reserved
// for Banish, DESIGN 5). 'other' is a near-neutral grey.
export const FAMILY_COLORS: Record<InstrumentFamily, string> = {
  guitar: 'oklch(0.72 0.12 95)', // gold, kin to the sulphur accent
  bass: 'oklch(0.56 0.09 255)', // slate blue
  drums: 'oklch(0.62 0.11 45)', // rust
  vocals: 'oklch(0.62 0.11 350)', // dried rose (not oxblood)
  keys: 'oklch(0.60 0.10 300)', // muted violet
  other: 'oklch(0.60 0.02 90)', // near-neutral grey
};

export function instrumentFamily(raw: string | null | undefined): InstrumentFamily {
  if (raw === null || raw === undefined) {
    return 'other';
  }

  const s = raw.toLowerCase();

  if (s.includes('vocal') || s.includes('vox') || s.includes('choir')) {
    return 'vocals';
  }

  if (s.includes('drum') || s.includes('percussion') || s.includes('membranophone')) {
    return 'drums';
  }

  if (s.includes('bass')) {
    return 'bass';
  }

  if (
    s.includes('keyboard') ||
    s.includes('piano') ||
    s.includes('synth') ||
    s.includes('organ') ||
    s.includes('harpsichord')
  ) {
    return 'keys';
  }

  if (s.includes('guitar')) {
    return 'guitar';
  }

  return 'other';
}

// The family for a member's row: driven by the primary (first) instrument. No
// instruments -> 'other' (neutral).
export function familyForInstruments(instruments: readonly string[]): InstrumentFamily {
  if (instruments.length === 0) {
    return 'other';
  }

  return instrumentFamily(instruments[0]);
}

// ---------------------------------------------------------------------------
// Members from edges — the row model (B7 rows, reused for B10).
//
// Uses the precomputed counterpart fields, so the SAME function builds band rows
// (counterpart = person) and person rows (counterpart = band). Rows are ordered by
// tenure start (founders first); members with an unknown start sink to the bottom.
// ---------------------------------------------------------------------------

export interface LineupMember {
  id: string; // counterpart id — for click-through and highlight
  label: string; // counterpart name
  instruments: string[];
  family: InstrumentFamily;
  beginDate: string | null;
  endDate: string | null;
  beginYear: number | null;
  endYear: number | null;
}

function yearOf(iso: string | null): number | null {
  if (iso === null) {
    return null;
  }

  const y = Number(iso.slice(0, 4));
  return Number.isFinite(y) ? y : null;
}

export function membersFromEdges(edges: readonly ArtistEdge[]): LineupMember[] {
  const members: LineupMember[] = [];

  for (const edge of edges) {
    if (edge.kind !== 'MemberOf') {
      continue;
    }

    members.push({
      id: edge.counterpartId,
      label: edge.counterpartName,
      instruments: edge.instruments,
      family: familyForInstruments(edge.instruments),
      beginDate: edge.beginDate,
      endDate: edge.endDate,
      beginYear: yearOf(edge.beginDate),
      endYear: yearOf(edge.endDate),
    });
  }

  members.sort((a, b) => {
    // Known start first (ascending); unknown start (null) sinks to the bottom.
    const ab = a.beginYear ?? Number.POSITIVE_INFINITY;
    const bb = b.beginYear ?? Number.POSITIVE_INFINITY;
    if (ab !== bb) {
      return ab - bb;
    }

    // Then longest tenure first: an open end (still active) counts as ongoing.
    const ae = a.endYear ?? Number.POSITIVE_INFINITY;
    const be = b.endYear ?? Number.POSITIVE_INFINITY;
    if (ae !== be) {
      return be - ae;
    }

    return a.label.localeCompare(b.label);
  });

  return members;
}

// ---------------------------------------------------------------------------
// Release marks — the vertical ticks on the timeline (B7).
// ---------------------------------------------------------------------------

export interface ReleaseMark {
  id: string;
  title: string;
  date: string; // ISO date, for the B8 highlight intersection
  year: number;
}

export function releaseMarksFromReleases(
  releases: readonly { id: string; title: string; releaseDate: string | null }[],
): ReleaseMark[] {
  const marks: ReleaseMark[] = [];

  for (const release of releases) {
    if (release.releaseDate === null) {
      continue;
    }

    const year = yearOf(release.releaseDate);
    if (year === null) {
      continue;
    }

    marks.push({ id: release.id, title: release.title, date: release.releaseDate, year });
  }

  marks.sort((a, b) => a.date.localeCompare(b.date));
  return marks;
}

// ---------------------------------------------------------------------------
// The layout — x from year, y from row. Pure; the ui/ layer only paints the numbers.
// ---------------------------------------------------------------------------

export interface LineupViewport {
  width: number; // measured container width in px (auto-fit target)
  rowHeight: number;
  rowGap: number;
  padLeft: number;
  padRight: number;
  padTop: number;
  padBottom: number;
  headerHeight: number; // room above the rows for the year axis + release marks
  currentYear: number; // injected so open-ended bars and the domain are deterministic
}

export interface LaidOutBar {
  memberId: string;
  rowIndex: number;
  x: number;
  y: number;
  width: number;
  height: number;
  openStart: boolean; // begin unknown -> runs to the left edge
  openEnd: boolean; // still active -> runs to the right edge
  unknownSpan: boolean; // both dates null -> no asserted span (rendered distinctly)
  family: InstrumentFamily;
  color: string;
  label: string;
  instruments: string[];
  beginYear: number | null;
  endYear: number | null;
}

export interface LaidOutRelease {
  id: string;
  title: string;
  date: string;
  year: number;
  x: number;
}

export interface YearTick {
  year: number;
  x: number;
}

export interface LineupLayout {
  domain: { minYear: number; maxYear: number };
  bars: LaidOutBar[];
  releases: LaidOutRelease[];
  ticks: YearTick[];
  width: number;
  height: number;
  plotLeft: number;
  plotRight: number;
  plotWidth: number;
}

const MIN_BAR_WIDTH = 3;

// Fractional year of an ISO date, for sub-year x precision on release marks.
function fractionalYear(iso: string): number {
  const y = Number(iso.slice(0, 4));
  const m = Number(iso.slice(5, 7)) || 1;
  const d = Number(iso.slice(8, 10)) || 1;
  return y + ((m - 1) * 30.44 + (d - 1)) / 365.25;
}

// A short list of tick years across [min, max], on a "nice" step (~4–8 ticks).
export function niceYearTicks(minYear: number, maxYear: number): number[] {
  const span = Math.max(1, maxYear - minYear);
  const steps = [1, 2, 5, 10, 20, 25, 50, 100];
  let step = steps[steps.length - 1];
  for (const candidate of steps) {
    if (span / candidate <= 8) {
      step = candidate;
      break;
    }
  }

  const ticks: number[] = [];
  const start = Math.ceil(minYear / step) * step;
  for (let year = start; year <= maxYear; year += step) {
    ticks.push(year);
  }

  // Always anchor the ends so the axis frames the data even on tiny spans.
  if (ticks.length === 0 || ticks[0] !== minYear) {
    ticks.unshift(minYear);
  }
  if (ticks[ticks.length - 1] !== maxYear) {
    ticks.push(maxYear);
  }

  return ticks;
}

export function layoutLineup(
  members: readonly LineupMember[],
  releases: readonly ReleaseMark[],
  vp: LineupViewport,
): LineupLayout {
  // --- Domain from the data (auto-fit to the year range). ---
  const years: number[] = [];
  let anyOpenEnd = false;

  for (const m of members) {
    if (m.beginYear !== null) {
      years.push(m.beginYear);
    }
    if (m.endYear !== null) {
      years.push(m.endYear);
    }
    // A known start with no end is still active: the domain must reach "now".
    if (m.beginDate !== null && m.endDate === null) {
      anyOpenEnd = true;
    }
  }

  for (const r of releases) {
    years.push(r.year);
  }

  if (anyOpenEnd) {
    years.push(vp.currentYear);
  }

  let minYear: number;
  let maxYear: number;
  if (years.length === 0) {
    // No dated information at all: give a small honest span rather than divide by zero.
    minYear = vp.currentYear - 1;
    maxYear = vp.currentYear;
  } else {
    minYear = Math.floor(Math.min(...years));
    maxYear = Math.ceil(Math.max(...years));
  }
  if (maxYear <= minYear) {
    maxYear = minYear + 1;
  }

  const plotLeft = vp.padLeft;
  const plotRight = Math.max(vp.padLeft + 1, vp.width - vp.padRight);
  const plotWidth = plotRight - plotLeft;

  const xForYear = (fracYear: number): number => {
    const t = (fracYear - minYear) / (maxYear - minYear);
    const clamped = Math.min(1, Math.max(0, t));
    return plotLeft + clamped * plotWidth;
  };

  // --- Bars: y from row, x from year. ---
  const bars: LaidOutBar[] = members.map((m, rowIndex) => {
    const openStart = m.beginDate === null;
    const unknownSpan = m.beginDate === null && m.endDate === null;
    const openEnd = m.endDate === null && !unknownSpan;

    const xStart = openStart ? plotLeft : xForYear(fractionalYear(m.beginDate as string));
    const xEnd = m.endDate === null ? plotRight : xForYear(fractionalYear(m.endDate));

    const y = vp.headerHeight + vp.padTop + rowIndex * (vp.rowHeight + vp.rowGap);
    const width = Math.max(MIN_BAR_WIDTH, xEnd - xStart);

    return {
      memberId: m.id,
      rowIndex,
      x: xStart,
      y,
      width,
      height: vp.rowHeight,
      openStart,
      openEnd,
      unknownSpan,
      family: m.family,
      color: FAMILY_COLORS[m.family],
      label: m.label,
      instruments: m.instruments,
      beginYear: m.beginYear,
      endYear: m.endYear,
    };
  });

  // --- Release marks. ---
  const laidOutReleases: LaidOutRelease[] = releases.map((r) => ({
    id: r.id,
    title: r.title,
    date: r.date,
    year: r.year,
    x: xForYear(fractionalYear(r.date)),
  }));

  // --- Year axis ticks. ---
  const ticks: YearTick[] = niceYearTicks(minYear, maxYear).map((year) => ({
    year,
    x: xForYear(year),
  }));

  const rowsHeight =
    members.length === 0 ? 0 : members.length * vp.rowHeight + (members.length - 1) * vp.rowGap;
  const height = vp.headerHeight + vp.padTop + rowsHeight + vp.padBottom;

  return {
    domain: { minYear, maxYear },
    bars,
    releases: laidOutReleases,
    ticks,
    width: vp.width,
    height,
    plotLeft,
    plotRight,
    plotWidth,
  };
}
