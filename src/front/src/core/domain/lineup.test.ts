import { describe, expect, it } from 'vitest';
import type { ArtistEdge } from './types';
import {
  FAMILY_COLORS,
  INSTRUMENT_FAMILIES,
  familyForInstruments,
  instrumentFamily,
  layoutLineup,
  membersActiveOn,
  membersFromEdges,
  niceYearTicks,
  releaseMarksFromReleases,
  type LineupViewport,
} from './lineup';

// A minimal MemberOf edge builder. counterpart* mirror the backend contract.
function edge(partial: Partial<ArtistEdge>): ArtistEdge {
  return {
    fromId: 'p',
    toId: 'b',
    kind: 'MemberOf',
    beginDate: null,
    endDate: null,
    instruments: [],
    counterpartId: 'p',
    counterpartName: 'Member',
    counterpartKind: 'Person',
    ...partial,
  };
}

// ---------------------------------------------------------------------------
// B8 — membersActiveOn: this mirrors LineupIntervalResolverTests (C#). The bounds are
// INCLUSIVE and nulls are open; inverting either breaks these cases on purpose.
// ---------------------------------------------------------------------------

describe('membersActiveOn (port of LineupIntervalResolver)', () => {
  it('closed interval: date strictly inside is active', () => {
    const e = edge({ beginDate: '1988-01-01', endDate: '1993-01-01' });
    expect(membersActiveOn([e], '1990-06-01')).toHaveLength(1);
  });

  it('closed interval: date outside is inactive', () => {
    const e = edge({ beginDate: '1988-01-01', endDate: '1993-01-01' });
    expect(membersActiveOn([e], '1995-01-01')).toHaveLength(0);
    expect(membersActiveOn([e], '1985-01-01')).toHaveLength(0);
  });

  it('boundaries are inclusive on both ends', () => {
    const e = edge({ beginDate: '1988-01-01', endDate: '1993-01-01' });
    expect(membersActiveOn([e], '1988-01-01'), 'begin day inclusive').toHaveLength(1);
    expect(membersActiveOn([e], '1993-01-01'), 'end day inclusive').toHaveLength(1);
  });

  it('open start (null begin): always begun, active at/before any date', () => {
    const e = edge({ beginDate: null, endDate: '1993-01-01' });
    expect(membersActiveOn([e], '1900-01-01')).toHaveLength(1);
    expect(membersActiveOn([e], '1993-01-01')).toHaveLength(1);
    expect(membersActiveOn([e], '1993-01-02')).toHaveLength(0);
  });

  it('open end (null end): still active, active at/after begin', () => {
    const e = edge({ beginDate: '1986-01-01', endDate: null });
    expect(membersActiveOn([e], '1985-12-31')).toHaveLength(0);
    expect(membersActiveOn([e], '1986-01-01')).toHaveLength(1);
    expect(membersActiveOn([e], '2026-01-01')).toHaveLength(1);
  });

  it('fully open (both null): active on any date', () => {
    const e = edge({ beginDate: null, endDate: null });
    expect(membersActiveOn([e], '1970-01-01')).toHaveLength(1);
  });

  it('ignores non-membership edges', () => {
    const e = edge({ kind: 'InfluencedBy', beginDate: '1980-01-01', endDate: '1990-01-01' });
    expect(membersActiveOn([e], '1985-01-01')).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// Instrument -> family -> colour (stable map).
// ---------------------------------------------------------------------------

describe('instrument family classification', () => {
  it('folds the messy real MusicBrainz strings', () => {
    expect(instrumentFamily('lead vocals')).toBe('vocals');
    expect(instrumentFamily('background vocals')).toBe('vocals');
    expect(instrumentFamily('drums (drum set)')).toBe('drums');
    expect(instrumentFamily('percussion')).toBe('drums');
    expect(instrumentFamily('membranophone')).toBe('drums');
    expect(instrumentFamily('electric guitar')).toBe('guitar');
    expect(instrumentFamily('acoustic guitar')).toBe('guitar');
    expect(instrumentFamily('keyboard')).toBe('keys');
    expect(instrumentFamily('piano')).toBe('keys');
  });

  it('classifies bass before guitar ("bass guitar" contains "guitar")', () => {
    expect(instrumentFamily('bass guitar')).toBe('bass');
    expect(instrumentFamily('electric bass guitar')).toBe('bass');
    expect(instrumentFamily('bass')).toBe('bass');
  });

  it('folds rare instruments and unknowns to the neutral family', () => {
    expect(instrumentFamily('violin')).toBe('other');
    expect(instrumentFamily('bagpipe')).toBe('other');
    expect(instrumentFamily('hurdy gurdy')).toBe('other');
    expect(instrumentFamily('')).toBe('other');
    expect(instrumentFamily(null)).toBe('other');
    expect(instrumentFamily(undefined)).toBe('other');
  });

  it('the row family is the primary (first) instrument; empty -> other', () => {
    expect(familyForInstruments(['lead vocals', 'bass', 'guitar'])).toBe('vocals');
    expect(familyForInstruments([])).toBe('other');
  });

  it('every family has a distinct colour', () => {
    const colours = INSTRUMENT_FAMILIES.map((f) => FAMILY_COLORS[f]);
    expect(colours.every((c) => typeof c === 'string' && c.length > 0)).toBe(true);
    expect(new Set(colours).size).toBe(INSTRUMENT_FAMILIES.length);
  });
});

// ---------------------------------------------------------------------------
// membersFromEdges — row model, ordering, both view directions.
// ---------------------------------------------------------------------------

describe('membersFromEdges', () => {
  it('orders founders first and sinks unknown-start members to the bottom', () => {
    const edges = [
      edge({ counterpartId: 'late', counterpartName: 'Late', beginDate: '1990-01-01' }),
      edge({ counterpartId: 'unk', counterpartName: 'Unknown', beginDate: null }),
      edge({ counterpartId: 'founder', counterpartName: 'Founder', beginDate: '1986-01-01' }),
    ];
    const members = membersFromEdges(edges);
    expect(members.map((m) => m.id)).toEqual(['founder', 'late', 'unk']);
  });

  it('uses the counterpart, so it works when viewing a person (rows = bands)', () => {
    const edges = [
      edge({ counterpartId: 'band1', counterpartName: 'Darkthrone', counterpartKind: 'Group' }),
    ];
    const [row] = membersFromEdges(edges);
    expect(row.label).toBe('Darkthrone');
    expect(row.id).toBe('band1');
  });
});

// ---------------------------------------------------------------------------
// Layout — x from year, y from row, auto-fit, open bars.
// ---------------------------------------------------------------------------

const VP: LineupViewport = {
  width: 1000,
  rowHeight: 20,
  rowGap: 6,
  padLeft: 100,
  padRight: 20,
  padTop: 10,
  padBottom: 10,
  headerHeight: 40,
  currentYear: 2026,
};

describe('layoutLineup', () => {
  it('maps the domain endpoints to the plot edges (auto-fit)', () => {
    const members = membersFromEdges([
      edge({ counterpartId: 'a', beginDate: '1990-01-01', endDate: '2000-01-01' }),
    ]);
    const layout = layoutLineup(members, [], VP);
    expect(layout.domain.minYear).toBe(1990);
    expect(layout.domain.maxYear).toBe(2000);
    // The first tick sits at the left plot edge, the last at the right.
    expect(layout.ticks[0].x).toBeCloseTo(layout.plotLeft, 5);
    expect(layout.ticks[layout.ticks.length - 1].x).toBeCloseTo(layout.plotRight, 5);
  });

  it('places a closed bar between its begin and end, y stepping by row', () => {
    const members = membersFromEdges([
      edge({ counterpartId: 'a', beginDate: '1990-01-01', endDate: '2000-01-01' }),
      edge({ counterpartId: 'b', beginDate: '1995-01-01', endDate: '2000-01-01' }),
    ]);
    const layout = layoutLineup(members, [], VP);
    const [first, second] = layout.bars;
    expect(first.x).toBeCloseTo(layout.plotLeft, 5); // 1990 = min -> left edge
    expect(first.x + first.width).toBeCloseTo(layout.plotRight, 5); // 2000 = max -> right edge
    expect(second.x).toBeGreaterThan(first.x); // 1995 sits to the right of 1990
    expect(second.y - first.y).toBeCloseTo(VP.rowHeight + VP.rowGap, 5);
    expect(first.openStart).toBe(false);
    expect(first.openEnd).toBe(false);
  });

  it('open end (still active) runs to the right edge and pulls the domain to currentYear', () => {
    const members = membersFromEdges([edge({ counterpartId: 'a', beginDate: '1986-01-01', endDate: null })]);
    const layout = layoutLineup(members, [], VP);
    expect(layout.domain.maxYear).toBe(2026); // reached "now"
    const [bar] = layout.bars;
    expect(bar.openEnd).toBe(true);
    expect(bar.unknownSpan).toBe(false);
    expect(bar.x + bar.width).toBeCloseTo(layout.plotRight, 5);
  });

  it('open start runs from the left edge', () => {
    const members = membersFromEdges([edge({ counterpartId: 'a', beginDate: null, endDate: '1995-01-01' })]);
    const layout = layoutLineup(members, [], VP);
    const [bar] = layout.bars;
    expect(bar.openStart).toBe(true);
    expect(bar.x).toBeCloseTo(layout.plotLeft, 5);
  });

  it('unknown span (no dates) spans the plot but is flagged, not asserted', () => {
    const members = membersFromEdges([edge({ counterpartId: 'a', beginDate: null, endDate: null })]);
    const layout = layoutLineup(members, [], VP);
    const [bar] = layout.bars;
    expect(bar.unknownSpan).toBe(true);
    expect(bar.beginYear).toBeNull();
    expect(bar.endYear).toBeNull();
  });

  it('auto-fits: doubling the container width scales positions proportionally', () => {
    const members = membersFromEdges([
      edge({ counterpartId: 'a', beginDate: '1990-01-01', endDate: '2010-01-01' }),
    ]);
    const narrow = layoutLineup(members, [], VP);
    const wide = layoutLineup(members, [], { ...VP, width: 2000 });
    // A midpoint year's offset within the plot scales with plotWidth.
    const narrowFrac = (narrow.ticks[0].x - narrow.plotLeft) / narrow.plotWidth;
    const wideFrac = (wide.ticks[0].x - wide.plotLeft) / wide.plotWidth;
    expect(wideFrac).toBeCloseTo(narrowFrac, 5);
    expect(wide.plotWidth).toBeGreaterThan(narrow.plotWidth);
  });

  it('positions release marks by date and keeps them within the plot', () => {
    const members = membersFromEdges([
      edge({ counterpartId: 'a', beginDate: '1990-01-01', endDate: '2000-01-01' }),
    ]);
    const marks = releaseMarksFromReleases([
      { id: 'r1', title: 'First', releaseDate: '1991-06-15' },
      { id: 'r2', title: 'Undated', releaseDate: null },
    ]);
    expect(marks).toHaveLength(1); // the undated release is dropped, not invented
    const layout = layoutLineup(members, marks, VP);
    const [mark] = layout.releases;
    expect(mark.x).toBeGreaterThanOrEqual(layout.plotLeft);
    expect(mark.x).toBeLessThanOrEqual(layout.plotRight);
  });

  it('survives an empty lineup without dividing by zero', () => {
    const layout = layoutLineup([], [], VP);
    expect(layout.bars).toHaveLength(0);
    expect(layout.domain.maxYear).toBeGreaterThan(layout.domain.minYear);
  });
});

describe('niceYearTicks', () => {
  it('frames the ends and keeps the count reasonable', () => {
    const ticks = niceYearTicks(1986, 2026);
    expect(ticks[0]).toBe(1986);
    expect(ticks[ticks.length - 1]).toBe(2026);
    expect(ticks.length).toBeLessThanOrEqual(10);
  });
});
