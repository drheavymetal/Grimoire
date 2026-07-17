import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useCoverWall } from '../../core/hooks/useCoverWall';
import { useCompare, useHyperprolific, useOneAlbumBands, useSplits } from '../../core/hooks/useCatalogue';
import { useRareInstruments } from '../../core/hooks/useRareInstruments';
import type { ArtistSummary, RareInstrument } from '../../core/domain/types';
import { ArtistPicker } from '../lineage/ArtistPicker';
import { GraphCanvas } from '../graph/GraphCanvas';
import { GraphErrorBoundary } from '../GraphErrorBoundary';
import { Cover } from '../Cover';
import { PageHeader } from '../PageHeader';
import { CollapsibleSection } from '../CollapsibleSection';
import { DurationAxis } from '../recordings/DurationAxis';
import { readExploreSections, writeExploreSections } from '../../platform/exploreSections.web';
import type { ExploreSectionId } from '../../core/domain/exploreSections';

// Movement V — the Explore hub: the catalogue turned over and looked at from odd angles. Seven
// sections — the wall of covers (C6), comparing two bands (B24), the duration axis (C7), the rare
// instruments (C15), the one-album bands (C24), the hyperprolific (C25), and the split network (C9).
// Every section reads real data through a core/ hook and degrades to a designed empty state.
//
// Every section also folds, and folding is not cosmetic. Mounting the page used to fire six queries
// and forty-eight cover images at once for a page nobody reads end to end — a reader called it
// "inmenso". Now each section owns its query but passes `open` down as `enabled`, so a folded
// section costs zero requests, and the fold state persists per reader. The section components stay
// mounted while folded on purpose: that is what keeps a picked band or a chosen pole alive across a
// fold, which unmounting the whole section would throw away.

// How many covers the wall opens with. The wall is the only section open by default, so this number
// IS the page's load: measured 2026-07-17, 48 of the 56 requests at mount were covers, one per
// release group. Folding every query away only moved the total from 54 to 49 while this stayed at
// 48. Twelve fills the grid two rows deep and costs a quarter of that.
const WALL_COVERS = 12;

export function ExplorePage() {
  const { t } = useTranslation();
  // Read once on mount, from the reader's last visit. Corrupt or absent state falls back to the
  // default (wall open, the rest folded) rather than breaking the page.
  const [sections, setSections] = useState(readExploreSections);

  function toggle(id: ExploreSectionId) {
    setSections((current) => {
      const next = { ...current, [id]: !current[id] };
      writeExploreSections(next);
      return next;
    });
  }

  return (
    <section>
      <PageHeader
        eyebrow={t('explore.eyebrow')}
        title={t('explore.heading')}
        lead={<p className="font-mono text-xs text-muted">{t('explore.intro')}</p>}
      />

      <CoverWallSection open={sections.wall} onToggle={() => toggle('wall')} />
      <CompareSection open={sections.compare} onToggle={() => toggle('compare')} />
      <DurationAxis open={sections.duration} onToggle={() => toggle('duration')} />
      <RareInstrumentsSection open={sections.rare} onToggle={() => toggle('rare')} />
      <OneAlbumSection open={sections.oneAlbum} onToggle={() => toggle('oneAlbum')} />
      <HyperprolificSection open={sections.prolific} onToggle={() => toggle('prolific')} />
      <SplitsSection open={sections.splits} onToggle={() => toggle('splits')} />
    </section>
  );
}

// Every section takes the same pair: whether it is unfolded, and how to flip that.
type SectionProps = { open: boolean; onToggle: () => void };

// C15 — rare instruments: the folk/orchestral colour outside the standard rock kit, and who plays it.
function RareInstrumentsSection({ open, onToggle }: SectionProps) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useRareInstruments(open);
  const instruments = data ?? [];

  return (
    <CollapsibleSection
      title={t('explore.rareTitle')}
      hint={t('explore.rareHint')}
      open={open}
      onToggle={onToggle}
    >
      {isLoading ? <p className="mt-3 font-mono text-sm text-muted">{t('explore.loading')}</p> : null}
      {isError ? <p className="mt-3 font-mono text-sm text-danger">{t('explore.error')}</p> : null}
      {!isLoading && !isError && instruments.length === 0 ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('explore.rareEmpty')}</p>
      ) : null}

      {instruments.length > 0 ? (
        <div className="mt-3 space-y-4">
          {instruments.map((instrument) => (
            <RareInstrumentCard key={instrument.instrument} instrument={instrument} />
          ))}
        </div>
      ) : null}
    </CollapsibleSection>
  );
}

function RareInstrumentCard({ instrument }: { instrument: RareInstrument }) {
  const { t } = useTranslation();

  return (
    <article className="border border-line p-3">
      <header className="flex items-baseline justify-between gap-3 border-b border-line pb-1.5">
        <h3 className="font-display text-lg text-accent">{instrument.instrument}</h3>
        <span className="shrink-0 font-mono text-xs text-muted">
          {t('explore.rarePlayers', { count: instrument.playerCount })}
        </span>
      </header>
      <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1">
        {instrument.players.map((player) => (
          <li key={`${player.artistId}-${player.bandId}`} className="font-body text-sm text-strong">
            <Link
              to="/artist/$artistId"
              params={{ artistId: player.artistId }}
              className="no-underline hover:text-accent"
            >
              {player.name}
            </Link>
            <Link
              to="/artist/$artistId"
              params={{ artistId: player.bandId }}
              className="ml-1.5 font-mono text-xs text-muted no-underline hover:text-accent"
            >
              {player.bandName}
            </Link>
          </li>
        ))}
      </ul>
    </article>
  );
}

// C6 — the wall of covers. The one section Explore opens by default, so it is also the page's whole
// load cost: it takes the hook's twelve-cover default rather than asking for the old forty-eight.
// Twelve fills the grid two rows deep and gives the page something to look at without spending
// forty-eight image requests before the reader has asked for anything.
function CoverWallSection({ open, onToggle }: SectionProps) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCoverWall(WALL_COVERS, open);
  const items = data ?? [];

  return (
    <CollapsibleSection
      title={t('explore.wallTitle')}
      hint={t('explore.wallHint')}
      open={open}
      onToggle={onToggle}
    >
      {isLoading ? <p className="mt-3 font-mono text-sm text-muted">{t('explore.loading')}</p> : null}
      {isError ? <p className="mt-3 font-mono text-sm text-danger">{t('explore.error')}</p> : null}
      {!isLoading && !isError && items.length === 0 ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('explore.wallEmpty')}</p>
      ) : null}

      {items.length > 0 ? (
        <div className="mt-3 grid grid-cols-3 gap-2 sm:grid-cols-4 md:grid-cols-6">
          {items.map((item) => (
            <Link
              key={item.releaseId}
              to="/artist/$artistId"
              params={{ artistId: item.artistId }}
              className="group no-underline"
              title={`${item.artistName} — ${item.title}`}
            >
              <Cover mbid={item.mbid} title={item.title} className="w-full" />
              <p className="mt-1 truncate font-mono text-[0.6rem] uppercase text-muted group-hover:text-accent">
                {item.artistName}
              </p>
            </Link>
          ))}
        </div>
      ) : null}
    </CollapsibleSection>
  );
}

// B24 — compare two bands. This one was already lazy by nature — useCompare stays disabled until two
// distinct bands are picked — so folding it costs it nothing. The picked bands live here rather than
// inside the fold, so collapsing the section and opening it again does not lose the pair.
function CompareSection({ open, onToggle }: SectionProps) {
  const { t } = useTranslation();
  const [a, setA] = useState<ArtistSummary | null>(null);
  const [b, setB] = useState<ArtistSummary | null>(null);
  const { data, isFetching, isError } = useCompare(a?.id ?? '', b?.id ?? '');

  const ready = a !== null && b !== null && a.id !== b.id;

  return (
    <CollapsibleSection
      title={t('explore.compareTitle')}
      hint={t('explore.compareHint')}
      open={open}
      onToggle={onToggle}
    >
      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <ArtistPicker label={t('explore.bandA')} selected={a} onSelect={setA} />
        <ArtistPicker label={t('explore.bandB')} selected={b} onSelect={setB} />
      </div>

      {ready && isFetching ? <p className="mt-4 font-mono text-sm text-muted">{t('explore.comparing')}</p> : null}
      {ready && isError ? <p className="mt-4 font-mono text-sm text-danger">{t('explore.error')}</p> : null}

      {ready && data !== undefined && !isFetching ? (
        <div className="mt-4 border border-line p-4">
          <dl className="grid gap-3 sm:grid-cols-2">
            <div>
              <dt className="font-mono text-xs uppercase text-muted">{t('explore.tagOverlap')}</dt>
              <dd className="mt-1 font-body text-strong">
                {(data.tagSimilarity * 100).toFixed(0)}%
                {data.sharedTags.length > 0 ? (
                  <span className="ml-2 font-mono text-xs text-muted">{data.sharedTags.join(', ')}</span>
                ) : (
                  <span className="ml-2 font-mono text-xs text-muted">{t('explore.noSharedTags')}</span>
                )}
              </dd>
            </div>
            <div>
              <dt className="font-mono text-xs uppercase text-muted">{t('explore.soundDistance')}</dt>
              <dd className="mt-1 font-body text-strong">
                {data.vectorDistance !== null
                  ? data.vectorDistance.toFixed(3)
                  : t('explore.noDistance')}
              </dd>
            </div>
            <div className="sm:col-span-2">
              <dt className="font-mono text-xs uppercase text-muted">{t('explore.sharedMembers')}</dt>
              <dd className="mt-1">
                {data.sharedMembers.length > 0 ? (
                  <ul className="flex flex-wrap gap-x-3 gap-y-1">
                    {data.sharedMembers.map((m) => (
                      <li key={m.id}>
                        <Link
                          to="/artist/$artistId"
                          params={{ artistId: m.id }}
                          className="font-body text-strong no-underline hover:text-accent"
                        >
                          {m.name}
                        </Link>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <span className="font-mono text-xs text-muted">{t('explore.noSharedMembers')}</span>
                )}
              </dd>
            </div>
          </dl>
        </div>
      ) : null}
    </CollapsibleSection>
  );
}

// C24 — the one-album bands.
function OneAlbumSection({ open, onToggle }: SectionProps) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useOneAlbumBands(open);
  const bands = data ?? [];

  return (
    <CollapsibleSection
      title={t('explore.oneAlbumTitle')}
      hint={t('explore.oneAlbumHint')}
      open={open}
      onToggle={onToggle}
    >
      {isLoading ? <p className="mt-3 font-mono text-sm text-muted">{t('explore.loading')}</p> : null}
      {isError ? <p className="mt-3 font-mono text-sm text-danger">{t('explore.error')}</p> : null}
      {!isLoading && !isError && bands.length === 0 ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('explore.oneAlbumEmpty')}</p>
      ) : null}

      {bands.length > 0 ? (
        <ul className="mt-3 divide-y divide-line border-y border-line">
          {bands.map((band) => (
            <li key={band.id}>
              <Link
                to="/artist/$artistId"
                params={{ artistId: band.id }}
                className="flex items-baseline justify-between gap-4 py-2.5 no-underline"
              >
                <span className="font-display text-lg text-strong">{band.name}</span>
                <span className="shrink-0 font-mono text-xs text-muted">
                  {band.albumTitle}
                  {band.albumDate ? ` · ${band.albumDate.slice(0, 4)}` : ''}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      ) : null}
    </CollapsibleSection>
  );
}

// C25 — the hyperprolific.
function HyperprolificSection({ open, onToggle }: SectionProps) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useHyperprolific(open);
  const bands = data ?? [];

  return (
    <CollapsibleSection
      title={t('explore.prolificTitle')}
      hint={t('explore.prolificHint')}
      open={open}
      onToggle={onToggle}
    >
      {isLoading ? <p className="mt-3 font-mono text-sm text-muted">{t('explore.loading')}</p> : null}
      {isError ? <p className="mt-3 font-mono text-sm text-danger">{t('explore.error')}</p> : null}
      {!isLoading && !isError && bands.length === 0 ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('explore.prolificEmpty')}</p>
      ) : null}

      {bands.length > 0 ? (
        <ul className="mt-3 divide-y divide-line border-y border-line">
          {bands.map((band) => (
            <li key={band.id}>
              <Link
                to="/artist/$artistId"
                params={{ artistId: band.id }}
                className="flex items-baseline justify-between gap-4 py-2.5 no-underline"
              >
                <span className="font-display text-lg text-strong">{band.name}</span>
                <span className="shrink-0 font-mono text-xs text-muted">
                  {t('explore.prolificMeta', { releases: band.releaseCount, ratio: band.ratio.toFixed(1) })}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      ) : null}
    </CollapsibleSection>
  );
}

// C9 — the split network.
function SplitsSection({ open, onToggle }: SectionProps) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useSplits(open);

  return (
    <CollapsibleSection
      title={t('explore.splitsTitle')}
      hint={t('explore.splitsHint')}
      open={open}
      onToggle={onToggle}
    >
      {isLoading ? <p className="mt-3 font-mono text-sm text-muted">{t('explore.loading')}</p> : null}
      {isError ? <p className="mt-3 font-mono text-sm text-danger">{t('explore.error')}</p> : null}

      {!isLoading && !isError && data !== undefined ? (
        data.nodes.length === 0 ? (
          <div className="mt-3 border border-line border-dashed p-6 text-center">
            <p className="font-body text-sm text-muted">{t('explore.splitsEmpty')}</p>
          </div>
        ) : (
          <GraphErrorBoundary>
            <GraphCanvas graph={data} height={360} />
          </GraphErrorBoundary>
        )
      ) : null}
    </CollapsibleSection>
  );
}
