import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useMissingLink, useSixDegrees } from '../../core/hooks/useLineage';
import type { ArtistSummary } from '../../core/domain/types';
import { ArtistPicker } from '../lineage/ArtistPicker';
import { PageHeader } from '../PageHeader';
import { SectionHead } from '../SectionHead';

// The lineage tools hub: the two features that take two bands as input — Six Degrees of Metal
// (B19) and the missing link (C5). Each picks two bands and shows a real result from the graph or
// the embedding space; both degrade to a designed empty state (no path, no embedding).

export function LineagePage() {
  const { t } = useTranslation();

  return (
    <section>
      <PageHeader
        eyebrow={t('lineage.eyebrow')}
        title={t('lineage.pageTitle')}
        lead={<p className="font-body text-sm text-muted">{t('lineage.pageIntro')}</p>}
      />

      <SixDegrees />
      <MissingLinkTool />
    </section>
  );
}

// B19 — Six Degrees of Metal.
function SixDegrees() {
  const { t } = useTranslation();
  const [from, setFrom] = useState<ArtistSummary | null>(null);
  const [to, setTo] = useState<ArtistSummary | null>(null);
  const { data, isFetching, isError } = useSixDegrees(from?.id ?? '', to?.id ?? '');

  const ready = from !== null && to !== null && from.id !== to.id;

  return (
    <div className="mt-10">
      <SectionHead title={t('lineage.sixDegreesTitle')} hint={t('lineage.sixDegreesHint')} />

      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <ArtistPicker label={t('lineage.fromBand')} selected={from} onSelect={setFrom} />
        <ArtistPicker label={t('lineage.toBand')} selected={to} onSelect={setTo} />
      </div>

      {ready && isFetching ? (
        <p className="mt-4 font-mono text-sm text-muted">{t('lineage.tracing')}</p>
      ) : null}
      {ready && isError ? <p className="mt-4 font-mono text-sm text-danger">{t('lineage.error')}</p> : null}

      {ready && data !== undefined && !isFetching ? (
        data.nodes.length === 0 ? (
          <div className="mt-4 border border-line border-dashed p-6 text-center">
            <p className="font-body text-sm text-muted">{t('lineage.noPath')}</p>
          </div>
        ) : (
          <div className="mt-4">
            <p className="font-mono text-xs uppercase text-accent">
              {t('lineage.degrees', { count: data.degrees })}
            </p>
            <ol className="mt-3 flex flex-wrap items-center gap-2">
              {data.nodes.map((node, i) => (
                <li key={node.id} className="flex items-center gap-2">
                  {i > 0 ? <span className="text-muted">·</span> : null}
                  <Link
                    to="/artist/$artistId"
                    params={{ artistId: node.id }}
                    className={
                      node.kind === 'Group'
                        ? 'font-display text-lg text-strong no-underline hover:text-accent'
                        : 'font-mono text-xs text-muted no-underline hover:text-accent'
                    }
                  >
                    {node.name}
                  </Link>
                </li>
              ))}
            </ol>
          </div>
        )
      ) : null}
    </div>
  );
}

// C5 — the missing link.
function MissingLinkTool() {
  const { t } = useTranslation();
  const [from, setFrom] = useState<ArtistSummary | null>(null);
  const [to, setTo] = useState<ArtistSummary | null>(null);
  const { data, isFetching, isError, error } = useMissingLink(from?.id ?? '', to?.id ?? '');

  const ready = from !== null && to !== null && from.id !== to.id;
  const noEmbedding = isError && error instanceof Error && 'status' in error && (error as { status: number }).status === 422;

  return (
    <div className="mt-12">
      <SectionHead title={t('lineage.missingLinkTitle')} hint={t('lineage.missingLinkHint')} />

      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <ArtistPicker label={t('lineage.fromBand')} selected={from} onSelect={setFrom} />
        <ArtistPicker label={t('lineage.toBand')} selected={to} onSelect={setTo} />
      </div>

      {ready && isFetching ? (
        <p className="mt-4 font-mono text-sm text-muted">{t('lineage.interpolating')}</p>
      ) : null}
      {ready && isError ? (
        <p className="mt-4 font-mono text-sm text-danger">
          {noEmbedding ? t('lineage.noEmbedding') : t('lineage.error')}
        </p>
      ) : null}

      {ready && data !== undefined && !isFetching ? (
        <div className="mt-4">
          <p className="font-mono text-xs uppercase text-muted">
            {t('lineage.betweenLabel', { from: data.from.name, to: data.to.name })}
          </p>
          <ul className="mt-3 divide-y divide-line border-y border-line">
            {data.between.map((node) => (
              <li key={node.id}>
                <Link
                  to="/artist/$artistId"
                  params={{ artistId: node.id }}
                  className="flex items-baseline justify-between gap-4 py-2.5 no-underline"
                >
                  <span className="font-display text-lg text-strong">{node.name}</span>
                  <span className="shrink-0 font-mono text-xs text-muted">
                    {t('lineage.distance', { distance: node.distance.toFixed(3) })}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}
