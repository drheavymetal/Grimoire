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
import { SectionHead } from '../SectionHead';
import { DurationAxis } from '../recordings/DurationAxis';

// Movement V — the Explore hub: the catalogue turned over and looked at from odd angles. The wall of
// covers (C6), comparing two bands (B24), the one-album bands (C24), the hyperprolific (C25), and
// the split network (C9). Every section reads real data through a core/ hook and degrades to a
// designed empty state.

export function ExplorePage() {
  const { t } = useTranslation();

  return (
    <section>
      <PageHeader
        eyebrow={t('explore.eyebrow')}
        title={t('explore.heading')}
        lead={<p className="font-mono text-xs text-muted">{t('explore.intro')}</p>}
      />

      <CoverWallSection />
      <CompareSection />
      <DurationAxis />
      <RareInstrumentsSection />
      <OneAlbumSection />
      <HyperprolificSection />
      <SplitsSection />
    </section>
  );
}

// C15 — rare instruments: the folk/orchestral colour outside the standard rock kit, and who plays it.
function RareInstrumentsSection() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useRareInstruments();
  const instruments = data ?? [];

  return (
    <div className="mt-12">
      <SectionHead title={t('explore.rareTitle')} hint={t('explore.rareHint')} />

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
    </div>
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

// C6 — the wall of covers.
function CoverWallSection() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCoverWall(48);
  const items = data ?? [];

  return (
    <div className="mt-10">
      <SectionHead title={t('explore.wallTitle')} hint={t('explore.wallHint')} />

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
    </div>
  );
}

// B24 — compare two bands.
function CompareSection() {
  const { t } = useTranslation();
  const [a, setA] = useState<ArtistSummary | null>(null);
  const [b, setB] = useState<ArtistSummary | null>(null);
  const { data, isFetching, isError } = useCompare(a?.id ?? '', b?.id ?? '');

  const ready = a !== null && b !== null && a.id !== b.id;

  return (
    <div className="mt-12">
      <SectionHead title={t('explore.compareTitle')} hint={t('explore.compareHint')} />

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
    </div>
  );
}

// C24 — the one-album bands.
function OneAlbumSection() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useOneAlbumBands();
  const bands = data ?? [];

  return (
    <div className="mt-12">
      <SectionHead title={t('explore.oneAlbumTitle')} hint={t('explore.oneAlbumHint')} />

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
    </div>
  );
}

// C25 — the hyperprolific.
function HyperprolificSection() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useHyperprolific();
  const bands = data ?? [];

  return (
    <div className="mt-12">
      <SectionHead title={t('explore.prolificTitle')} hint={t('explore.prolificHint')} />

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
    </div>
  );
}

// C9 — the split network.
function SplitsSection() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useSplits();

  return (
    <div className="mt-12">
      <SectionHead title={t('explore.splitsTitle')} hint={t('explore.splitsHint')} />

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
    </div>
  );
}
