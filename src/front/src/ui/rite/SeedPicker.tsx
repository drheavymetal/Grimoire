import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useImportLastFm } from '../../core/hooks/useColdStart';
import { useArtistSearch } from '../../core/hooks/useArtistSearch';
import { useDebouncedValue } from '../../core/hooks/useDebouncedValue';
import { ApiError } from '../../core/api/client';
import type { ArtistSummary, SeedCandidate, TasteStatus } from '../../core/domain/types';

// Renders the picker grid itself: the search box, the hint line, the loading/error states, and the
// chip grid. The caller owns the surrounding chrome (heading, counter, action buttons).
export function SeedGrid({
  grid,
  picked,
  full,
  expanding,
  isLoading,
  isError,
  onToggle,
  onPickFromSearch,
}: {
  grid: SeedCandidate[];
  picked: Set<string>;
  full: boolean;
  expanding: string | null;
  isLoading: boolean;
  isError: boolean;
  onToggle: (band: SeedCandidate, index: number) => void;
  onPickFromSearch: (summary: ArtistSummary) => void;
}) {
  const { t } = useTranslation();
  const gridIds = new Set(grid.map((band) => band.id));

  return (
    <>
      <SeedSearch full={full} picked={picked} gridIds={gridIds} onPick={onPickFromSearch} />

      <p className="mt-1 font-mono text-[0.65rem] text-muted">
        {expanding !== null ? t('coldStart.unfolding') : t('coldStart.gridHint')}
      </p>

      {isError ? <p className="mt-4 font-mono text-sm text-danger">{t('coldStart.loadError')}</p> : null}
      {isLoading ? <p className="mt-4 font-mono text-sm text-muted">{t('coldStart.loading')}</p> : null}

      {grid.length > 0 ? (
        <ul className="mt-5 grid grid-cols-2 gap-2 sm:grid-cols-3">
          {grid.map((band, index) => (
            <SeedChip
              key={band.id}
              band={band}
              selected={picked.has(band.id)}
              disabled={full && !picked.has(band.id)}
              onToggle={() => onToggle(band, index)}
            />
          ))}
        </ul>
      ) : null}
    </>
  );
}

// The band search inside the picker: a debounced typeahead so the user can find a band the grid
// never surfaced, add it, and have its kin unfold from it exactly like a grid pick. It is NOT blind
// — this is a deliberate known-band chooser, so results show name and origin. Bands already picked
// or already in the grid are shown as "already added" (disabled), never offered twice. When the pick
// cap is reached the results cannot add and say so; with an infinite cap (the profile reseed) `full`
// is never true, so that hint simply never shows.
function SeedSearch({
  full,
  picked,
  gridIds,
  onPick,
}: {
  full: boolean;
  picked: Set<string>;
  gridIds: Set<string>;
  onPick: (summary: ArtistSummary) => void;
}) {
  const { t } = useTranslation();
  const [term, setTerm] = useState('');
  const debounced = useDebouncedValue(term, 300);
  const search = useArtistSearch(debounced);

  const showResults = debounced.trim().length >= 2;
  const results = search.data ?? [];

  function pick(summary: ArtistSummary) {
    if (full || picked.has(summary.id) || gridIds.has(summary.id)) {
      return;
    }
    onPick(summary);
    setTerm('');
  }

  return (
    <div className="mb-5">
      <label className="block">
        <span className="font-mono text-xs uppercase text-muted">{t('coldStart.searchLabel')}</span>
        <input
          type="search"
          value={term}
          onChange={(event) => setTerm(event.target.value)}
          placeholder={t('coldStart.searchPlaceholder')}
          autoComplete="off"
          className="mt-1 w-full border border-line bg-panel px-4 py-3 font-body text-strong outline-none focus:border-accent"
        />
      </label>

      {full ? <p className="mt-2 font-mono text-xs text-muted">{t('coldStart.searchFull')}</p> : null}

      {showResults && search.isFetching ? (
        <p className="mt-2 font-mono text-xs text-muted">{t('coldStart.searchSearching')}</p>
      ) : null}

      {showResults && !search.isFetching && results.length === 0 ? (
        <p className="mt-2 font-mono text-xs text-muted">{t('coldStart.searchEmpty')}</p>
      ) : null}

      {results.length > 0 ? (
        <ul className="mt-2 divide-y divide-line border-y border-line">
          {results.map((artist) => {
            const already = picked.has(artist.id) || gridIds.has(artist.id);
            return (
              <li key={artist.id}>
                <button
                  type="button"
                  onClick={() => pick(artist)}
                  disabled={already || full}
                  className="flex w-full items-baseline justify-between gap-4 py-2.5 text-left disabled:opacity-50"
                >
                  <span className="min-w-0 truncate font-body text-strong">{artist.name}</span>
                  <span className="shrink-0 font-mono text-xs text-muted">
                    {already
                      ? t('coldStart.searchAdded')
                      : (artist.country ?? '—')}
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      ) : null}
    </div>
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

// Last.fm import (feature C1), shared by cold start and the profile reseed panel. Blocked without an
// API key: the endpoint answers 503, which we present as a dignified "not available yet" state,
// never a broken error (blocker Q5). `onImported` lets the profile refresh + collapse on success;
// `freshNote` adds the line making clear that a Last.fm import always starts fresh.
export function LastFmImport({
  onImported,
  freshNote = false,
}: {
  onImported?: (result: TasteStatus) => void;
  freshNote?: boolean;
}) {
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
      {freshNote ? (
        <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('profile.reselect.lastFmFresh')}</p>
      ) : null}

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
          onClick={() =>
            importLastFm.mutate(
              username.trim(),
              onImported !== undefined ? { onSuccess: onImported } : undefined,
            )
          }
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
