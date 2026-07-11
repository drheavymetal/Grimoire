import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useBloodline } from '../../core/hooks/useLineage';
import { GraphCanvas } from '../graph/GraphCanvas';
import { GraphErrorBoundary } from '../GraphErrorBoundary';

// B16 — Bloodline: the ego graph of one artist (shared members + declared influence), N hops out.
// The hero of the lineage view. A hop control widens the neighbourhood; the graph is the shared
// GraphCanvas so clicking a node opens its page.

const MIN_HOPS = 1;
const MAX_HOPS = 3;

export function Bloodline({ artistId }: { artistId: string }) {
  const { t } = useTranslation();
  const [hops, setHops] = useState(2);
  const { data, isLoading, isError } = useBloodline(artistId, hops);

  return (
    <section className="mt-10">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="font-display text-2xl text-strong">{t('lineage.bloodlineTitle')}</h2>
        <div className="flex items-center gap-2 font-mono text-xs uppercase text-muted">
          <span>{t('lineage.hops')}</span>
          {Array.from({ length: MAX_HOPS - MIN_HOPS + 1 }, (_, i) => MIN_HOPS + i).map((h) => (
            <button
              key={h}
              type="button"
              onClick={() => setHops(h)}
              aria-pressed={hops === h}
              className={hops === h ? 'text-accent underline' : 'hover:text-accent'}
            >
              {h}
            </button>
          ))}
        </div>
      </div>
      <p className="mt-1 font-mono text-xs text-muted">{t('lineage.bloodlineHint')}</p>

      {isLoading ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('lineage.loading')}</p>
      ) : isError ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('lineage.error')}</p>
      ) : data !== undefined ? (
        <GraphErrorBoundary>
          <GraphCanvas graph={data} />
        </GraphErrorBoundary>
      ) : null}
    </section>
  );
}
