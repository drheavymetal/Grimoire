import { useMemo, useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import {
  FAMILY_COLORS,
  INSTRUMENT_FAMILIES,
  layoutLineup,
  membersActiveOn,
  membersFromEdges,
  releaseMarksFromReleases,
  type InstrumentFamily,
  type LineupViewport,
} from '../../core/domain/lineup';
import type { ArtistEdge, ArtistKind, Release } from '../../core/domain/types';
import { prefersReducedMotion } from '../../platform/motion.web';
import { useMeasuredWidth } from './useMeasuredWidth';

// The Lineup Timeline — the Gantt (B7/B8/B10). It is the hero of the artist page, standing
// where a header photo would in any other music app (DESIGN 6): Grimoire has no rights to
// band photos, so it sells the band's structure in time instead.
//
// Its own render technique (D18): a pure layout in core/ turns years into x and rows into
// y, and this file paints the numbers with SVG primitives only — <rect>, <line>, <text> —
// which react-native-svg accepts unchanged. No d3-force, no canvas. Auto-fit happens by
// recomputing the layout for the measured width (positions transformed in JS), never by
// scaling a <g>.

// The layout viewport, minus the width which is measured live.
const VIEWPORT: Omit<LineupViewport, 'width' | 'currentYear'> = {
  rowHeight: 26,
  rowGap: 8,
  padLeft: 148,
  padRight: 24,
  padTop: 8,
  padBottom: 30,
  headerHeight: 44,
};

interface Props {
  edges: ArtistEdge[];
  releases: Release[];
  viewedKind: ArtistKind;
}

export function LineupTimeline({ edges, releases, viewedKind }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [containerRef, width] = useMeasuredWidth<HTMLDivElement>();
  const [activeDate, setActiveDate] = useState<string | null>(null);
  const reduced = prefersReducedMotion();

  const members = useMemo(() => membersFromEdges(edges), [edges]);
  const marks = useMemo(() => releaseMarksFromReleases(releases), [releases]);

  // B8 — the set of members active on the focused/hovered release's date.
  const activeIds = useMemo(() => {
    if (activeDate === null) {
      return null;
    }
    return new Set(membersActiveOn(edges, activeDate).map((e) => e.counterpartId));
  }, [edges, activeDate]);

  // Degrade with dignity (R2): a band with no membership data gets a designed empty state
  // that says what is missing, not a broken frame.
  if (members.length === 0) {
    const key = viewedKind === 'Person' ? 'lineup.emptyPerson' : 'lineup.emptyBand';
    return (
      <div className="mt-4 border border-line border-dashed p-6 text-center">
        <p className="font-mono text-xs uppercase text-muted">{t('lineup.title')}</p>
        <p className="mt-2 font-body text-sm text-muted">{t(key)}</p>
      </div>
    );
  }

  const effectiveWidth = width > 0 ? width : 640;
  const layout = layoutLineup(members, marks, {
    ...VIEWPORT,
    width: effectiveWidth,
    currentYear: new Date().getUTCFullYear(),
  });

  const familiesPresent = INSTRUMENT_FAMILIES.filter((f) => layout.bars.some((b) => b.family === f));
  const barTransition = reduced ? undefined : 'opacity 150ms ease-out';

  const goToArtist = (id: string): void => {
    void navigate({ to: '/artist/$artistId', params: { artistId: id } });
  };

  return (
    <figure className="mt-4">
      <figcaption className="flex items-baseline justify-between">
        <span className="font-mono text-xs uppercase text-muted">{t('lineup.title')}</span>
        <span className="font-mono text-[0.65rem] uppercase text-muted">{t('lineup.hint')}</span>
      </figcaption>

      <div ref={containerRef} className="mt-2 w-full overflow-x-auto">
        <svg
          width={effectiveWidth}
          height={layout.height}
          className="block text-muted"
          role="group"
          aria-label={t('lineup.aria', { count: members.length })}
        >
          {/* Year axis: faint vertical guides + labels top and bottom. */}
          {layout.ticks.map((tick) => (
            <g key={`tick-${tick.year}-${tick.x.toFixed(1)}`}>
              <line
                x1={tick.x}
                x2={tick.x}
                y1={VIEWPORT.headerHeight - 8}
                y2={layout.height - VIEWPORT.padBottom + 6}
                stroke="currentColor"
                strokeOpacity={0.14}
              />
              <text
                x={tick.x}
                y={layout.height - VIEWPORT.padBottom + 20}
                textAnchor="middle"
                className="font-mono"
                fontSize={10}
                fill="currentColor"
              >
                {tick.year}
              </text>
            </g>
          ))}

          {/* Release marks (B7). Focusable/hoverable: they light up the active lineup (B8). */}
          {layout.releases.map((mark) => {
            const isActive = activeDate === mark.date;
            return (
              <g
                key={mark.id}
                tabIndex={0}
                role="button"
                aria-label={t('lineup.releaseMark', { title: mark.title, year: mark.year })}
                onMouseEnter={() => setActiveDate(mark.date)}
                onMouseLeave={() => setActiveDate((d) => (d === mark.date ? null : d))}
                onFocus={() => setActiveDate(mark.date)}
                onBlur={() => setActiveDate((d) => (d === mark.date ? null : d))}
                style={{ cursor: 'pointer', outline: 'none' }}
              >
                {/* Wide invisible hit area for pointer + keyboard focus. */}
                <rect
                  x={mark.x - 5}
                  y={VIEWPORT.headerHeight - 14}
                  width={10}
                  height={layout.height - VIEWPORT.headerHeight - VIEWPORT.padBottom + 14}
                  fill="transparent"
                />
                <line
                  x1={mark.x}
                  x2={mark.x}
                  y1={VIEWPORT.headerHeight - 14}
                  y2={layout.height - VIEWPORT.padBottom + 2}
                  stroke={isActive ? 'var(--color-accent)' : 'currentColor'}
                  strokeOpacity={isActive ? 1 : 0.4}
                  strokeWidth={isActive ? 2 : 1}
                />
                <circle cx={mark.x} cy={VIEWPORT.headerHeight - 14} r={isActive ? 3.5 : 2.5} fill={isActive ? 'var(--color-accent)' : 'currentColor'} />
              </g>
            );
          })}

          {/* Member rows: name in the gutter, a colour-by-instrument bar on the timeline. */}
          {layout.bars.map((bar) => {
            const dim = activeIds !== null && !activeIds.has(bar.memberId);
            const years = formatYears(bar.beginYear, bar.endYear, bar.unknownSpan);
            const detail = [bar.label, bar.instruments.join(', '), years]
              .filter((s) => s.length > 0)
              .join(' · ');
            const centerY = bar.y + bar.height / 2;

            return (
              <g key={bar.memberId} style={{ opacity: dim ? 0.18 : 1, transition: barTransition }}>
                <title>{detail}</title>

                {/* Gutter name — clickable, keyboard-activable (B10 click-through to the row). */}
                <text
                  x={8}
                  y={centerY}
                  dominantBaseline="central"
                  className="cursor-pointer font-body text-strong hover:underline"
                  fontSize={13}
                  fill="currentColor"
                  tabIndex={0}
                  role="link"
                  aria-label={detail}
                  onClick={() => goToArtist(bar.memberId)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      goToArtist(bar.memberId);
                    }
                  }}
                >
                  {truncate(bar.label, 18)}
                </text>

                {/* The bar. Unknown span (no dates) is drawn hollow + dashed with a '?', never
                    as a solid asserted span — honest degradation (R2). */}
                {bar.unknownSpan ? (
                  <>
                    <rect
                      x={bar.x}
                      y={bar.y}
                      width={bar.width}
                      height={bar.height}
                      rx={2}
                      fill="none"
                      stroke={bar.color}
                      strokeOpacity={0.6}
                      strokeDasharray="3 3"
                    />
                    <text
                      x={(bar.x + bar.x + bar.width) / 2}
                      y={centerY}
                      textAnchor="middle"
                      dominantBaseline="central"
                      className="font-mono"
                      fontSize={11}
                      fill="currentColor"
                    >
                      ?
                    </text>
                  </>
                ) : (
                  <rect
                    x={bar.x}
                    y={bar.y}
                    width={bar.width}
                    height={bar.height}
                    rx={2}
                    fill={bar.color}
                    fillOpacity={0.85}
                    onClick={() => goToArtist(bar.memberId)}
                    style={{ cursor: 'pointer' }}
                  />
                )}
              </g>
            );
          })}
        </svg>
      </div>

      <Legend families={familiesPresent} />
    </figure>
  );
}

function Legend({ families }: { families: readonly InstrumentFamily[] }) {
  const { t } = useTranslation();

  return (
    <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1">
      {families.map((family) => (
        <li key={family} className="flex items-center gap-1.5 font-mono text-[0.65rem] uppercase text-muted">
          <span
            aria-hidden="true"
            className="inline-block h-2.5 w-2.5 rounded-sm"
            style={{ backgroundColor: FAMILY_COLORS[family] }}
          />
          {t(`lineup.instrument.${family}`)}
        </li>
      ))}
    </ul>
  );
}

// "1986–", "1990–2000", or the unknown marker. Honest: a missing bound shows a '?', a
// missing span shows nothing (the '?' lives on the bar itself).
function formatYears(begin: number | null, end: number | null, unknownSpan: boolean): string {
  if (unknownSpan) {
    return '';
  }
  const from = begin === null ? '?' : String(begin);
  const to = end === null ? '' : String(end);
  return `${from}–${to}`;
}

function truncate(s: string, max: number): string {
  return s.length > max ? `${s.slice(0, max - 1)}…` : s;
}
