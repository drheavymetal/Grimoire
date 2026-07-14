import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSeedCandidates } from '../../core/hooks/useSeedCandidates';
import { useImportLastFm, useSeed } from '../../core/hooks/useColdStart';
import { ApiError } from '../../core/api/client';
import type { SeedCandidate } from '../../core/domain/types';
import { PageHeader } from '../PageHeader';

const REQUIRED_PICKS = 5;
// The API seeds from at most twenty bands (MaxSeedArtists) and opens one neighbour lane per pick.
const MAX_PICKS = 20;

// Cold start (D15): a new user has no taste vector, so The Rite cannot run. They seed it by
// choosing bands they already know, or by importing Last.fm (feature C1, currently blocked
// with no API key -> a dignified "not available yet", not a broken error).
export function ColdStart() {
  const { t } = useTranslation();
  // Picked bands are held whole, not as bare ids: once a pick refills the grid with its neighbours
  // the band itself may no longer be among the candidates, and it must still render as chosen.
  const [picked, setPicked] = useState<Map<string, SeedCandidate>>(new Map());
  const pickedIds = [...picked.keys()];
  const { data, isLoading, isError, isFetching } = useSeedCandidates(true, pickedIds);
  const seed = useSeed();

  function toggle(band: SeedCandidate) {
    setPicked((current) => {
      const next = new Map(current);
      if (next.has(band.id)) {
        next.delete(band.id);
      } else if (next.size < MAX_PICKS) {
        next.set(band.id, band);
      }
      return next;
    });
  }

  const enough = picked.size >= REQUIRED_PICKS;
  const full = picked.size >= MAX_PICKS;
  // The grid never repeats what is already pinned above it.
  const suggestions = (data ?? []).filter((band) => !picked.has(band.id));

  return (
    <section>
      <PageHeader
        eyebrow={t('coldStart.eyebrow')}
        title={t('coldStart.heading')}
        lead={<p className="font-body text-strong">{t('coldStart.intro')}</p>}
      />
      <p className="mt-3 font-mono text-xs text-muted">
        {t('coldStart.counter', { count: picked.size, required: REQUIRED_PICKS })}
      </p>

      {picked.size > 0 ? (
        <div className="mt-5">
          <h2 className="font-mono text-xs uppercase text-muted">{t('coldStart.pickedHeading')}</h2>
          <ul className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-3">
            {[...picked.values()].map((band) => (
              <SeedChip key={band.id} band={band} selected onToggle={() => toggle(band)} />
            ))}
          </ul>
        </div>
      ) : null}

      {isError ? <p className="mt-4 font-mono text-sm text-danger">{t('coldStart.loadError')}</p> : null}
      {isLoading ? <p className="mt-4 font-mono text-sm text-muted">{t('coldStart.loading')}</p> : null}

      {data !== undefined ? (
        <div className="mt-6">
          <h2 className="font-mono text-xs uppercase text-muted">
            {picked.size > 0 ? t('coldStart.moreLikeHeading') : t('coldStart.suggestionsHeading')}
          </h2>
          <p className="mt-1 font-mono text-[0.65rem] text-muted">
            {isFetching ? t('coldStart.retuning') : t('coldStart.gridHint')}
          </p>
          <ul
            className={`mt-3 grid grid-cols-2 gap-2 sm:grid-cols-3 ${isFetching ? 'opacity-60' : ''}`}
          >
            {suggestions.map((band) => (
              <SeedChip
                key={band.id}
                band={band}
                selected={false}
                disabled={full}
                onToggle={() => toggle(band)}
              />
            ))}
          </ul>
        </div>
      ) : null}

      {seed.isError ? <p className="mt-4 font-mono text-sm text-danger">{t('coldStart.seedError')}</p> : null}

      <button
        type="button"
        disabled={!enough || seed.isPending}
        onClick={() => seed.mutate(pickedIds)}
        className="mt-6 w-full border border-accent bg-accent px-4 py-3 font-display text-lg text-bg disabled:opacity-40"
      >
        {seed.isPending ? t('coldStart.seeding') : t('coldStart.seed')}
      </button>

      <LastFmImport />
    </section>
  );
}

function SeedChip({
  band,
  selected,
  disabled = false,
  onToggle,
}: {
  band: SeedCandidate;
  selected: boolean;
  disabled?: boolean;
  onToggle: () => void;
}) {
  return (
    <li>
      <button
        type="button"
        onClick={onToggle}
        disabled={disabled}
        aria-pressed={selected}
        className={`w-full border px-3 py-2 text-left disabled:opacity-40 ${
          selected ? 'border-accent bg-accent/10 text-strong' : 'border-line text-muted hover:border-accent'
        }`}
      >
        <span className="block truncate font-body text-strong">{band.name}</span>
        <span className="font-mono text-[0.65rem] text-muted">
          {band.country ?? '—'}
          {band.formedYear !== null ? ` · ${band.formedYear}` : ''}
        </span>
      </button>
    </li>
  );
}

// Last.fm import (feature C1). Blocked without an API key: the endpoint answers 503, which
// we present as a dignified "not available yet" state, never a broken error (blocker Q5).
function LastFmImport() {
  const { t } = useTranslation();
  const importLastFm = useImportLastFm();
  const [username, setUsername] = useState('');

  const unavailable = importLastFm.error instanceof ApiError && importLastFm.error.status === 503;
  const noMatch = importLastFm.error instanceof ApiError && importLastFm.error.status === 404;
  const otherError = importLastFm.isError && !unavailable && !noMatch;

  return (
    <div className="mt-10 border-t border-line pt-6">
      <h2 className="font-mono text-xs uppercase text-muted">{t('coldStart.lastFmHeading')}</h2>
      <p className="mt-2 max-w-prose font-body text-sm text-strong">{t('coldStart.lastFmIntro')}</p>

      <div className="mt-3 flex gap-2">
        <input
          type="text"
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          placeholder={t('coldStart.lastFmPlaceholder')}
          className="min-w-0 flex-1 border border-line bg-panel px-3 py-2 font-body text-strong outline-none focus:border-accent"
          autoComplete="off"
        />
        <button
          type="button"
          disabled={username.trim().length === 0 || importLastFm.isPending}
          onClick={() => importLastFm.mutate(username.trim())}
          className="shrink-0 border border-line px-4 py-2 font-mono text-xs uppercase text-muted hover:border-accent hover:text-accent disabled:opacity-40"
        >
          {importLastFm.isPending ? t('coldStart.lastFmWorking') : t('coldStart.lastFmImport')}
        </button>
      </div>

      {unavailable ? (
        <p className="mt-3 font-mono text-xs text-muted">{t('coldStart.lastFmUnavailable')}</p>
      ) : null}
      {noMatch ? <p className="mt-3 font-mono text-xs text-muted">{t('coldStart.lastFmNoMatch')}</p> : null}
      {otherError ? <p className="mt-3 font-mono text-xs text-danger">{t('coldStart.lastFmError')}</p> : null}
    </div>
  );
}
