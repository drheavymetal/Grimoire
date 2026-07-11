import { useTranslation } from 'react-i18next';
import { useArtistThemes } from '../../core/hooks/useRecordings';

// C21 — song-title mining: the lyrical themes a band's titles evoke, as badges with counts. It is
// an APPROXIMATION from the titles (a closed bilingual vocabulary + a counter), not a curated
// lyrical fact — the hint says so plainly (D17). Reads real data through a core/ hook; a band whose
// titles matched no theme word degrades to a designed empty state, never a fabricated theme.
export function ArtistThemes({ artistId }: { artistId: string }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useArtistThemes(artistId);

  if (isLoading || isError || data === undefined) {
    // Themes are a garnish on the ficha, not load-bearing: while loading or on error, show nothing
    // rather than a spinner or a scare. The rest of the page stands on its own.
    return null;
  }

  return (
    <section className="mt-8">
      <h2 className="font-mono text-xs uppercase text-muted">{t('themes.title')}</h2>
      <p className="mt-1 max-w-prose font-mono text-[0.65rem] text-muted">
        {t('themes.hint', { count: data.titleCount })}
      </p>

      {data.themes.length > 0 ? (
        <ul className="mt-2 flex flex-wrap gap-2">
          {data.themes.map((theme) => (
            <li
              key={theme.theme}
              className="flex items-baseline gap-1.5 border border-line px-2 py-1 font-mono text-xs text-strong"
            >
              <span>{t(`theme.${theme.theme}`)}</span>
              <span className="text-muted tabular-nums">{theme.count}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-2 font-mono text-xs text-muted">{t('themes.empty')}</p>
      )}
    </section>
  );
}
