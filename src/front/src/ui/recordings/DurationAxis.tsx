import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useDurationAxis } from '../../core/hooks/useRecordings';
import { formatAverageLength } from '../../core/domain/recordings';
import { RankedName } from '../RankedName';
import { SectionHead } from '../SectionHead';

// C7 — the duration axis: bands ranked by mean track length, the pole no genre tag captures. One end
// is the funeral-doom crawl, the other the grindcore burst. A toggle picks the end. The average is
// over each band's timed recordings only (nulls are absences, not zeros — computed honestly on the
// server). Reads real data through a core/ hook and degrades to a designed empty state. It is an
// axis of curiosity, not a claim of genre — the hint says so.
export function DurationAxis() {
  const { t } = useTranslation();
  const [pole, setPole] = useState<'long' | 'short'>('long');
  const { data, isLoading, isError } = useDurationAxis(pole, 20);
  const bands = data ?? [];

  return (
    <div className="mt-12">
      <SectionHead title={t('explore.durationTitle')} hint={t('explore.durationHint')} />

      <div className="mt-3 flex items-center gap-3 font-mono text-xs uppercase">
        <button
          type="button"
          onClick={() => setPole('long')}
          aria-pressed={pole === 'long'}
          className={pole === 'long' ? 'text-accent underline' : 'text-muted hover:text-accent'}
        >
          {t('explore.durationLong')}
        </button>
        <button
          type="button"
          onClick={() => setPole('short')}
          aria-pressed={pole === 'short'}
          className={pole === 'short' ? 'text-accent underline' : 'text-muted hover:text-accent'}
        >
          {t('explore.durationShort')}
        </button>
      </div>

      {isLoading ? <p className="mt-3 font-mono text-sm text-muted">{t('explore.loading')}</p> : null}
      {isError ? <p className="mt-3 font-mono text-sm text-danger">{t('explore.error')}</p> : null}
      {!isLoading && !isError && bands.length === 0 ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('explore.durationEmpty')}</p>
      ) : null}

      {bands.length > 0 ? (
        <ol className="mt-3 divide-y divide-line border-y border-line">
          {bands.map((band) => (
            <li key={band.id}>
              <Link
                to="/artist/$artistId"
                params={{ artistId: band.id }}
                className="flex items-baseline justify-between gap-4 py-2.5 no-underline"
              >
                <RankedName name={band.name} rank={band.rank} className="font-display text-lg text-strong" />
                <span className="shrink-0 font-mono text-xs text-muted tabular-nums">
                  {formatAverageLength(band.averageMs)}
                  <span className="ml-2 text-[0.65rem] uppercase">
                    {t('explore.durationMeta', { count: band.timedTrackCount })}
                  </span>
                </span>
              </Link>
            </li>
          ))}
        </ol>
      ) : null}
    </div>
  );
}
