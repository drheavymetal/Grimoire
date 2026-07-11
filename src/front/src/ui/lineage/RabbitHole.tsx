import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useRabbitHole } from '../../core/hooks/useLineage';

// C8 — Rabbit Hole: an opt-in guided walk through the lineage, each step chosen by the previous
// band's connections. It is not fetched until the user starts it (no surprise work), and it stops
// honestly at a dead end rather than padding the chain.

const HOLE_LENGTH = 10;

export function RabbitHole({ artistId }: { artistId: string }) {
  const { t } = useTranslation();
  const [started, setStarted] = useState(false);
  const { data, isLoading, isError, refetch, isFetching } = useRabbitHole(artistId, HOLE_LENGTH, started);

  return (
    <section className="mt-10">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="font-display text-2xl text-strong">{t('lineage.rabbitHoleTitle')}</h2>
        <button
          type="button"
          onClick={() => {
            if (started) {
              void refetch();
            } else {
              setStarted(true);
            }
          }}
          disabled={isFetching}
          className="font-mono text-xs uppercase text-accent hover:underline disabled:text-muted"
        >
          {started ? t('lineage.rabbitHoleAgain') : t('lineage.rabbitHoleStart')}
        </button>
      </div>
      <p className="mt-1 font-mono text-xs text-muted">{t('lineage.rabbitHoleHint')}</p>

      {started && isLoading ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('lineage.loading')}</p>
      ) : null}

      {started && isError ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('lineage.error')}</p>
      ) : null}

      {data !== undefined && data.steps.length > 0 ? (
        <ol className="mt-4 space-y-1">
          {data.steps.map((step, i) => (
            <li key={step.id} className="flex items-baseline gap-3 font-body">
              <span className="w-6 shrink-0 text-right font-mono text-xs text-muted">{i + 1}</span>
              <Link
                to="/artist/$artistId"
                params={{ artistId: step.id }}
                className="text-strong no-underline hover:text-accent"
              >
                {step.name}
              </Link>
            </li>
          ))}
        </ol>
      ) : null}

      {data !== undefined && data.steps.length <= 1 ? (
        <p className="mt-3 font-body text-sm text-muted">{t('lineage.rabbitHoleDeadEnd')}</p>
      ) : null}
    </section>
  );
}
