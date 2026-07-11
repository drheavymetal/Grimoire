import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useArtistSearch } from '../../core/hooks/useArtistSearch';
import { useDebouncedValue } from '../../core/hooks/useDebouncedValue';
import type { ArtistSummary } from '../../core/domain/types';

// A small search-and-pick control used by the two-ended lineage tools (Six Degrees B19, the
// missing link C5). It reuses the trigram search hook; once a band is chosen it collapses to a
// chip with a "change" affordance. Pure UI over an injected core/ hook — no invented data.

interface Props {
  label: string;
  selected: ArtistSummary | null;
  onSelect: (artist: ArtistSummary | null) => void;
}

export function ArtistPicker({ label, selected, onSelect }: Props) {
  const { t } = useTranslation();
  const [term, setTerm] = useState('');
  const debounced = useDebouncedValue(term, 300);
  const { data, isFetching } = useArtistSearch(debounced);

  const showResults = debounced.trim().length >= 2 && selected === null;
  const results = data ?? [];

  if (selected !== null) {
    return (
      <div>
        <p className="font-mono text-xs uppercase text-muted">{label}</p>
        <div className="mt-1 flex items-baseline justify-between gap-3 border border-accent px-3 py-2">
          <span className="font-display text-lg text-strong">{selected.name}</span>
          <button
            type="button"
            onClick={() => {
              onSelect(null);
              setTerm('');
            }}
            className="shrink-0 font-mono text-xs uppercase text-muted hover:text-accent"
          >
            {t('lineage.change')}
          </button>
        </div>
      </div>
    );
  }

  return (
    <div>
      <label className="font-mono text-xs uppercase text-muted">{label}</label>
      <input
        type="search"
        value={term}
        onChange={(event) => setTerm(event.target.value)}
        placeholder={t('lineage.pickPlaceholder')}
        className="mt-1 w-full border border-line bg-panel px-3 py-2 font-body text-strong outline-none focus:border-accent"
        autoComplete="off"
      />
      {showResults ? (
        <ul className="mt-1 max-h-48 divide-y divide-line overflow-y-auto border border-line">
          {isFetching && results.length === 0 ? (
            <li className="px-3 py-2 font-mono text-xs text-muted">{t('search.searching')}</li>
          ) : null}
          {!isFetching && results.length === 0 ? (
            <li className="px-3 py-2 font-mono text-xs text-muted">{t('search.empty')}</li>
          ) : null}
          {results.map((artist) => (
            <li key={artist.id}>
              <button
                type="button"
                onClick={() => onSelect(artist)}
                className="flex w-full items-baseline justify-between gap-3 px-3 py-2 text-left hover:bg-panel"
              >
                <span className="font-body text-strong">{artist.name}</span>
                <span className="shrink-0 font-mono text-xs text-muted">
                  {artist.country ?? '—'}
                  {artist.formedYear !== null ? ` · ${artist.formedYear}` : ''}
                </span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
