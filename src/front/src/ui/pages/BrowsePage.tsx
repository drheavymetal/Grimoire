import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useBrowseByTag, useBrowseByTheme } from '../../core/hooks/useBrowse';
import type { BandCard, ThemeKind } from '../../core/domain/types';
import { PageHeader } from '../PageHeader';
import { RankedName } from '../RankedName';

// The NAMED "see all" door (2026-07-15): every band under a tag or a theme, paged. The rite is
// blind; this is its deliberate opposite — names, ranks and origins shown, like Scenes. Coverage is
// thin in the underground (R2), so a tag/theme with nothing behind it renders a designed empty state.

type BrowseMode =
  | { kind: 'tag'; needle: string }
  | { kind: 'theme'; themeKey: string; themeKind: ThemeKind };

export function BrowsePage({ mode }: { mode: BrowseMode }) {
  return mode.kind === 'tag' ? (
    <BrowseTag needle={mode.needle} />
  ) : (
    <BrowseTheme themeKey={mode.themeKey} themeKind={mode.themeKind} />
  );
}

function BrowseTag({ needle }: { needle: string }) {
  const { t } = useTranslation();
  const query = useBrowseByTag(needle);
  return (
    <BrowseView eyebrow={t('browse.eyebrowTag')} title={needle} query={query} />
  );
}

function BrowseTheme({ themeKey, themeKind }: { themeKey: string; themeKind: ThemeKind }) {
  const { t } = useTranslation();
  const query = useBrowseByTheme(themeKey, themeKind);
  // A mined theme is one of the closed C21 vocabulary, so it is translatable; a real Metal Archives
  // lyrical theme is free text (e.g. "Death, Gore") and is shown exactly as recorded.
  const title = themeKind === 'mined' ? t(`theme.${themeKey}`, { defaultValue: themeKey }) : themeKey;
  return <BrowseView eyebrow={t('browse.eyebrowTheme')} title={title} query={query} />;
}

// The shared render for either door: a header, the count, a grid of cards, and "load more".
function BrowseView({
  eyebrow,
  title,
  query,
}: {
  eyebrow: string;
  title: string;
  query: ReturnType<typeof useBrowseByTag>;
}) {
  const { t } = useTranslation();
  const { data, isLoading, isError, hasNextPage, isFetchingNextPage, fetchNextPage } = query;

  const bands: BandCard[] = data?.pages.flatMap((page) => page.bands) ?? [];
  const total = data?.pages[0]?.total ?? 0;

  return (
    <section>
      <PageHeader
        eyebrow={eyebrow}
        title={title}
        lead={<p className="font-mono text-xs text-muted">{t('browse.intro')}</p>}
      />

      {isLoading ? (
        <p className="mt-6 font-mono text-sm text-muted">{t('browse.loading')}</p>
      ) : null}
      {isError ? <p className="mt-6 font-mono text-sm text-danger">{t('browse.error')}</p> : null}

      {!isLoading && !isError && bands.length === 0 ? (
        <div className="mt-6 border border-line border-dashed p-8 text-center">
          <p className="font-body text-sm text-muted">{t('browse.empty')}</p>
        </div>
      ) : null}

      {bands.length > 0 ? (
        <>
          <p className="mt-6 font-mono text-xs uppercase text-muted">
            {t('browse.count', { count: total })}
          </p>
          <ul className="mt-3 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {bands.map((band) => (
              <BandCardTile key={band.id} band={band} />
            ))}
          </ul>

          {hasNextPage ? (
            <div className="mt-6 text-center">
              <button
                type="button"
                onClick={() => void fetchNextPage()}
                disabled={isFetchingNextPage}
                className="border border-accent px-6 py-2 font-mono text-xs uppercase text-accent hover:bg-accent hover:text-bg disabled:opacity-40"
              >
                {isFetchingNextPage ? t('browse.loading') : t('browse.loadMore')}
              </button>
            </div>
          ) : null}
        </>
      ) : null}
    </section>
  );
}

function BandCardTile({ band }: { band: BandCard }) {
  const { t } = useTranslation();
  const origin = band.country ?? t('search.countryUnknown');
  const rankLabel = band.rank !== null ? t(`rank.${band.rank}`) : t('artist.rankUnknown');

  return (
    <li>
      <Link
        to="/artist/$artistId"
        params={{ artistId: band.id }}
        className="flex h-full flex-col justify-between border border-line p-4 no-underline hover:border-accent"
      >
        <RankedName name={band.name} rank={band.rank} className="font-display text-xl text-strong" />
        <p className="mt-3 flex items-baseline justify-between gap-3 font-mono text-xs">
          <span className="text-muted">{origin}</span>
          <span className={band.rank !== null ? 'text-accent' : 'text-muted'}>{rankLabel}</span>
        </p>
      </Link>
    </li>
  );
}
