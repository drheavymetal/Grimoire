import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useCoverWall } from '../../core/hooks/useCoverWall';
import { useCompare, useHyperprolific, useOneAlbumBands, useSplits } from '../../core/hooks/useCatalogue';
import type { ArtistSummary } from '../../core/domain/types';
import { ArtistPicker } from '../lineage/ArtistPicker';
import { GraphCanvas } from '../graph/GraphCanvas';
import { Cover } from '../Cover';

// Movement V — the Explore hub: the catalogue turned over and looked at from odd angles. The wall of
// covers (C6), comparing two bands (B24), the one-album bands (C24), the hyperprolific (C25), and
// the split network (C9). Every section reads real data through a core/ hook and degrades to a
// designed empty state.

export function ExplorePage() {
  const { t } = useTranslation();

  return (
    <section>
      <div className="flyer -mx-5 -mt-8 border-b border-line px-5 pb-6 pt-8">
        <h1 className="font-display text-4xl text-strong">{t('explore.heading')}</h1>
        <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('explore.intro')}</p>
      </div>

      <CoverWallSection />
      <CompareSection />
      <OneAlbumSection />
      <HyperprolificSection />
      <SplitsSection />
    </section>
  );
}

// C6 — the wall of covers.
function CoverWallSection() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useCoverWall(48);
  const items = data ?? [];

  return (
    <div className="mt-10">
      <h2 className="font-display text-2xl text-strong">{t('explore.wallTitle')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('explore.wallHint')}</p>

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
      <h2 className="font-display text-2xl text-strong">{t('explore.compareTitle')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('explore.compareHint')}</p>

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
      <h2 className="font-display text-2xl text-strong">{t('explore.oneAlbumTitle')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('explore.oneAlbumHint')}</p>

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
      <h2 className="font-display text-2xl text-strong">{t('explore.prolificTitle')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('explore.prolificHint')}</p>

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
      <h2 className="font-display text-2xl text-strong">{t('explore.splitsTitle')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('explore.splitsHint')}</p>

      {isLoading ? <p className="mt-3 font-mono text-sm text-muted">{t('explore.loading')}</p> : null}
      {isError ? <p className="mt-3 font-mono text-sm text-danger">{t('explore.error')}</p> : null}

      {!isLoading && !isError && data !== undefined ? (
        data.nodes.length === 0 ? (
          <div className="mt-3 border border-line border-dashed p-6 text-center">
            <p className="font-body text-sm text-muted">{t('explore.splitsEmpty')}</p>
          </div>
        ) : (
          <GraphCanvas graph={data} height={360} />
        )
      ) : null}
    </div>
  );
}
