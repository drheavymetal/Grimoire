import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../core/api/client';
import { useGrimoireClient } from '../../core/api/context';
import { useArtistSearch } from '../../core/hooks/useArtistSearch';
import { useDebouncedValue } from '../../core/hooks/useDebouncedValue';
import {
  useAddAnchor,
  useAnchors,
  useProfile,
  useRebuildTaste,
  useRemoveAnchor,
  useReseed,
  useUpdateHandle,
} from '../../core/hooks/useProfile';
import { useLogoutAll, useSessions } from '../../core/hooks/useSessions';
import type {
  ArtistSummary,
  BandCard,
  CountryCount,
  DecadeCount,
  GenreCount,
  Profile,
  Rank,
  RankBreakdownEntry,
  ReseedMode,
  ReseedResult,
  Session,
} from '../../core/domain/types';
import { applyTheme, readTheme, type Theme } from '../../platform/theme.web';
import { authStore } from '../../platform/authStore.web';
import { downloadAuthenticated } from '../../platform/download.web';
import { persistLanguage } from '../../i18n';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { useSeedGrid } from '../../core/hooks/useSeedGrid';
import { LastFmImport, SeedGrid } from '../rite/SeedPicker';
import { PageHeader } from '../PageHeader';
import { SectionHead } from '../SectionHead';
import { RankedName } from '../RankedName';

// The canonical rank order, rarest last, so the distribution always reads Known → Nameless
// regardless of the order the backend returns the breakdown in.
const RANK_ORDER: Rank[] = ['Known', 'Obscure', 'Hidden', 'Forgotten', 'Nameless'];

// The user profile (2026-07-15): the signed-in listener's own page. Taste is HYBRID — the Rite
// keeps learning by itself (EMA), and this page adds an editable anchor set plus a "rebuild my
// taste from these anchors" action. The page LINKS to the grimoire, the mirror and the atlas; it
// never duplicates them. Every section has a designed empty state — a new account has nothing yet.
export function ProfilePage() {
  const { t } = useTranslation();
  const { status, isAuthenticated } = useAuth();

  if (status === 'unknown') {
    return <p className="font-mono text-sm text-muted">{t('rite.checking')}</p>;
  }

  if (!isAuthenticated) {
    return <AuthPanel />;
  }

  return <ProfileBody />;
}

function ProfileBody() {
  const { t } = useTranslation();
  const profile = useProfile(true);

  return (
    <section className="space-y-12">
      <PageHeader eyebrow={t('profile.eyebrow')} title={t('profile.heading')} />

      {profile.isLoading ? (
        <p className="font-mono text-sm text-muted">{t('profile.loading')}</p>
      ) : profile.isError ? (
        <p className="font-mono text-sm text-danger">{t('profile.error')}</p>
      ) : profile.data !== undefined ? (
        <>
          <IdentityHeader profile={profile.data} />
          <TasteManagement anchorCount={profile.data.anchorCount} />
          <Discoveries profile={profile.data} />
        </>
      ) : null}

      <Settings />
    </section>
  );
}

// Identity: the Depth Score as the headline badge, the summoned count, and the rank distribution.
function IdentityHeader({ profile }: { profile: Profile }) {
  const { t } = useTranslation();

  return (
    <section>
      <div className="flex flex-wrap items-end gap-x-8 gap-y-4 border border-line p-6">
        <div>
          <p className="font-mono text-[0.7rem] uppercase tracking-[0.28em] text-accent">
            {t('profile.depthScore')}
          </p>
          <p className="font-display text-6xl leading-none text-strong">{profile.depthScore}</p>
        </div>
        <div>
          <p className="font-mono text-[0.7rem] uppercase tracking-[0.28em] text-muted">
            {t('profile.summoned')}
          </p>
          <p className="font-display text-4xl leading-none text-strong">{profile.summonedCount}</p>
        </div>
      </div>

      <RankDistribution breakdown={profile.rankBreakdown} total={profile.summonedCount} />
    </section>
  );
}

function RankDistribution({
  breakdown,
  total,
}: {
  breakdown: RankBreakdownEntry[];
  total: number;
}) {
  const { t } = useTranslation();
  const byRank = new Map<Rank | 'null', number>();
  for (const entry of breakdown) {
    byRank.set(entry.rank ?? 'null', entry.count);
  }
  const nullCount = byRank.get('null') ?? 0;

  if (total === 0) {
    return (
      <p className="mt-4 font-mono text-xs text-muted">{t('profile.rankEmpty')}</p>
    );
  }

  return (
    <dl className="mt-5 flex flex-wrap gap-x-6 gap-y-2">
      {RANK_ORDER.map((rank) => (
        <div key={rank} className="flex items-baseline gap-2">
          <dt className="font-mono text-[0.7rem] uppercase tracking-[0.14em] text-muted">
            {t(`rank.${rank}`)}
          </dt>
          <dd className="font-mono text-sm text-strong">{byRank.get(rank) ?? 0}</dd>
        </div>
      ))}
      {nullCount > 0 ? (
        <div className="flex items-baseline gap-2">
          <dt className="font-mono text-[0.7rem] uppercase tracking-[0.14em] text-muted">
            {t('profile.rankUnknown')}
          </dt>
          <dd className="font-mono text-sm text-strong">{nullCount}</dd>
        </div>
      ) : null}
    </dl>
  );
}

// The hybrid anchor set: the pinned bands (removable), an "add band" typeahead, and the
// rebuild-taste action. The copy is honest about the two halves of the taste.
function TasteManagement({ anchorCount }: { anchorCount: number }) {
  const { t } = useTranslation();
  const anchors = useAnchors(true);
  const removeAnchor = useRemoveAnchor();

  return (
    <section>
      <SectionHead title={t('profile.tasteTitle')} hint={t('profile.tasteHint')} />

      {anchors.isLoading ? (
        <p className="mt-3 font-mono text-sm text-muted">{t('profile.anchorsLoading')}</p>
      ) : anchors.isError ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('profile.anchorsError')}</p>
      ) : (
        <>
          {(anchors.data ?? []).length === 0 ? (
            <p className="mt-3 max-w-prose font-body text-sm text-muted">{t('profile.anchorsEmpty')}</p>
          ) : (
            <ul className="mt-4 flex flex-wrap gap-2">
              {(anchors.data ?? []).map((band) => (
                <li
                  key={band.id}
                  className="flex items-center gap-2 border border-line px-3 py-1.5"
                >
                  <Link
                    to="/artist/$artistId"
                    params={{ artistId: band.id }}
                    className="no-underline"
                  >
                    <RankedName name={band.name} rank={band.rank} className="font-body text-strong" />
                  </Link>
                  <button
                    type="button"
                    onClick={() => removeAnchor.mutate(band.id)}
                    disabled={removeAnchor.isPending}
                    aria-label={t('profile.anchorRemove', { name: band.name })}
                    className="cursor-pointer font-mono text-sm text-muted hover:text-danger disabled:opacity-50"
                  >
                    ✕
                  </button>
                </li>
              ))}
            </ul>
          )}

          <AnchorSearch existing={anchors.data ?? []} />
          <RebuildTaste anchorCount={anchorCount} />
          <ReselectBands />
        </>
      )}
    </section>
  );
}

// "Reselect your bands": re-runs the sign-up cold-start picker from the profile, reusing the exact
// same grid (SeedPicker). An expanding in-page panel, consistent with the handle/session editors.
// Two clearly-labelled outcomes once at least one band is chosen — "Start fresh" (mode "fresh":
// replaces the taste, like a new account) and "Add these" (mode "add": unions into the anchors and
// rebuilds from all). The Last.fm import is a fresh cold start, labelled as such. The 400 "no usable
// band" case surfaces as friendly copy; success collapses the panel and the invalidated queries
// refresh the anchors and depth score above.
function ReselectBands() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const [done, setDone] = useState<{ mode: ReseedMode | 'lastfm'; used: number; depth: number } | null>(
    null,
  );
  // The reseed backend (POST /api/profile/reseed) has no cap, so re-selecting is UNLIMITED — unlike
  // sign-up, which the /api/rite/seed cap holds to MAX_PICKS.
  const grid = useSeedGrid(open, Number.POSITIVE_INFINITY);
  const reseed = useReseed();

  const chosen = grid.picked.size;
  const noUsable = reseed.isError && reseed.error instanceof ApiError && reseed.error.status === 400;

  function toggleOpen() {
    setDone(null);
    reseed.reset();
    setOpen((value) => !value);
  }

  function run(mode: ReseedMode) {
    setDone(null);
    reseed.mutate(
      { artistIds: [...grid.picked], mode },
      {
        onSuccess: (result: ReseedResult) => {
          setDone({ mode, used: result.anchorsUsed, depth: result.depthScore });
          grid.reset();
          setOpen(false);
        },
      },
    );
  }

  // The Last.fm mutation already invalidates the rite taste; refresh the profile + anchors too so the
  // depth score and anchor set on this page update, then collapse and confirm. Import = fresh.
  function onLastFmImported() {
    void queryClient.invalidateQueries({ queryKey: ['profile'] });
    void queryClient.invalidateQueries({ queryKey: ['profile', 'anchors'] });
    setDone({ mode: 'lastfm', used: 0, depth: 0 });
    setOpen(false);
  }

  return (
    <div className="mt-6 border-t border-line pt-5">
      <div className="flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <p className="font-mono text-[0.7rem] uppercase tracking-[0.28em] text-accent">
            {t('profile.reselect.title')}
          </p>
          <p className="mt-1 max-w-prose font-mono text-xs text-muted">{t('profile.reselect.hint')}</p>
        </div>
        <button
          type="button"
          onClick={toggleOpen}
          className="shrink-0 border border-line px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-strong"
        >
          {open ? t('profile.reselect.close') : t('profile.reselect.open')}
        </button>
      </div>

      {done !== null ? (
        <p className="mt-3 font-mono text-sm text-strong">
          {done.mode === 'lastfm'
            ? t('profile.reselect.doneLastFm')
            : done.mode === 'fresh'
              ? t('profile.reselect.doneFresh', { used: done.used, depth: done.depth })
              : t('profile.reselect.doneAdd', { used: done.used, depth: done.depth })}
        </p>
      ) : null}

      {open ? (
        <div className="mt-5">
          <p className="font-mono text-xs text-muted">
            {t('profile.reselect.chosen', { count: chosen })}
          </p>

          <SeedGrid
            grid={grid.grid}
            picked={grid.picked}
            full={grid.full}
            expanding={grid.expanding}
            isLoading={grid.isLoading}
            isError={grid.isError}
            onToggle={grid.toggle}
            onPickFromSearch={grid.pickFromSearch}
          />

          {chosen > 0 ? (
            <div className="mt-6 grid gap-4 sm:grid-cols-2">
              <div className="border border-line p-4">
                <button
                  type="button"
                  disabled={reseed.isPending}
                  onClick={() => run('fresh')}
                  className="w-full border border-accent px-4 py-2.5 font-mono text-xs uppercase tracking-[0.18em] text-accent hover:bg-accent hover:text-bg disabled:cursor-not-allowed disabled:opacity-50"
                >
                  {reseed.isPending ? t('profile.reselect.working') : t('profile.reselect.startFresh')}
                </button>
                <p className="mt-2 font-mono text-xs text-muted">{t('profile.reselect.startFreshNote')}</p>
              </div>
              <div className="border border-line p-4">
                <button
                  type="button"
                  disabled={reseed.isPending}
                  onClick={() => run('add')}
                  className="w-full border border-line px-4 py-2.5 font-mono text-xs uppercase tracking-[0.18em] text-strong hover:border-accent hover:text-accent disabled:cursor-not-allowed disabled:opacity-50"
                >
                  {reseed.isPending ? t('profile.reselect.working') : t('profile.reselect.add')}
                </button>
                <p className="mt-2 font-mono text-xs text-muted">{t('profile.reselect.addNote')}</p>
              </div>
            </div>
          ) : null}

          {noUsable ? (
            <p className="mt-3 font-mono text-sm text-danger">{t('profile.reselect.noUsable')}</p>
          ) : reseed.isError ? (
            <p className="mt-3 font-mono text-sm text-danger">{t('profile.reselect.error')}</p>
          ) : null}

          <LastFmImport onImported={onLastFmImported} freshNote />
        </div>
      ) : null}
    </div>
  );
}

// The "add band" typeahead — reuses the artist search that powers the "/" route. Bands already
// pinned are dropped from the suggestions so an anchor cannot be added twice.
function AnchorSearch({ existing }: { existing: BandCard[] }) {
  const { t } = useTranslation();
  const [term, setTerm] = useState('');
  const debounced = useDebouncedValue(term, 300);
  const search = useArtistSearch(debounced);
  const addAnchor = useAddAnchor();

  const existingIds = new Set(existing.map((band) => band.id));
  const suggestions = (search.data ?? []).filter((artist) => !existingIds.has(artist.id));
  const showResults = debounced.trim().length >= 2;

  function pick(artist: ArtistSummary) {
    addAnchor.mutate(artist.id, {
      onSuccess: () => {
        setTerm('');
      },
    });
  }

  return (
    <div className="mt-5">
      <label className="block">
        <span className="font-mono text-xs uppercase text-muted">{t('profile.addLabel')}</span>
        <input
          type="search"
          value={term}
          onChange={(event) => setTerm(event.target.value)}
          placeholder={t('profile.addPlaceholder')}
          autoComplete="off"
          className="mt-1 w-full border border-line bg-panel px-4 py-3 font-body text-strong outline-none focus:border-accent"
        />
      </label>

      {addAnchor.isError ? (
        <p className="mt-2 font-mono text-xs text-danger">{t('profile.addError')}</p>
      ) : null}

      {showResults && search.isFetching ? (
        <p className="mt-2 font-mono text-xs text-muted">{t('profile.addSearching')}</p>
      ) : null}

      {showResults && !search.isFetching && suggestions.length === 0 ? (
        <p className="mt-2 font-mono text-xs text-muted">{t('profile.addEmpty')}</p>
      ) : null}

      {suggestions.length > 0 ? (
        <ul className="mt-2 divide-y divide-line border-y border-line">
          {suggestions.map((artist) => (
            <li key={artist.id}>
              <button
                type="button"
                onClick={() => pick(artist)}
                disabled={addAnchor.isPending}
                className="flex w-full items-baseline justify-between gap-4 py-2.5 text-left disabled:opacity-50"
              >
                <RankedName name={artist.name} rank={artist.rank} className="font-body text-strong" />
                <span className="shrink-0 font-mono text-xs text-muted">
                  {artist.country ?? t('search.countryUnknown')}
                </span>
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

// The "rebuild my taste from these anchors" action. It re-seeds the taste vector with the anchors'
// mean; the Rite keeps learning on its own afterwards. Surfaces the result, or the 400 no-anchor case.
function RebuildTaste({ anchorCount }: { anchorCount: number }) {
  const { t } = useTranslation();
  const rebuild = useRebuildTaste();

  const noAnchors =
    rebuild.isError && rebuild.error instanceof ApiError && rebuild.error.status === 400;

  return (
    <div className="mt-6 border-t border-line pt-5">
      <button
        type="button"
        onClick={() => rebuild.mutate()}
        disabled={rebuild.isPending || anchorCount === 0}
        className="border border-accent px-5 py-2.5 font-mono text-xs uppercase tracking-[0.18em] text-accent hover:bg-accent hover:text-bg disabled:cursor-not-allowed disabled:opacity-50"
      >
        {rebuild.isPending ? t('profile.rebuilding') : t('profile.rebuild')}
      </button>
      <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('profile.rebuildNote')}</p>

      {noAnchors ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('profile.rebuildNoAnchors')}</p>
      ) : rebuild.isError ? (
        <p className="mt-3 font-mono text-sm text-danger">{t('profile.rebuildError')}</p>
      ) : rebuild.data !== undefined ? (
        <p className="mt-3 font-mono text-sm text-strong">
          {rebuild.data.tasteSet
            ? t('profile.rebuildDone', {
                used: rebuild.data.anchorsUsed,
                depth: rebuild.data.depthScore,
              })
            : t('profile.rebuildNoAnchors')}
        </p>
      ) : null}
    </div>
  );
}

// Discoveries + stats: the rarest find, and the grimoire's shape by decade, country and genre,
// with doors out to the grimoire, the mirror and the atlas.
function Discoveries({ profile }: { profile: Profile }) {
  const { t } = useTranslation();

  return (
    <section>
      <SectionHead title={t('profile.discoveriesTitle')} hint={t('profile.discoveriesHint')} />

      {profile.deepestCut !== null ? (
        <div className="mt-4 border border-accent p-5">
          <p className="font-mono text-[0.7rem] uppercase tracking-[0.28em] text-accent">
            {t('profile.deepestCut')}
          </p>
          <Link
            to="/artist/$artistId"
            params={{ artistId: profile.deepestCut.id }}
            className="no-underline"
          >
            <RankedName
              name={profile.deepestCut.name}
              rank={profile.deepestCut.rank}
              className="mt-2 block font-display text-3xl text-strong"
            />
          </Link>
          <p className="mt-2 font-mono text-xs text-muted">
            {profile.deepestCut.rank !== null ? t(`rank.${profile.deepestCut.rank}`) : t('profile.rankUnknown')}
            {' · '}
            {profile.deepestCut.country ?? t('search.countryUnknown')}
          </p>
        </div>
      ) : (
        <p className="mt-4 max-w-prose font-body text-sm text-muted">{t('profile.deepestCutEmpty')}</p>
      )}

      <div className="mt-8 grid gap-6 sm:grid-cols-3">
        <StatColumn
          title={t('profile.byDecade')}
          rows={profile.byDecade.map((d: DecadeCount) => ({ label: `${d.decade}s`, count: d.count }))}
          empty={t('profile.statsEmpty')}
        />
        <StatColumn
          title={t('profile.byCountry')}
          rows={profile.byCountry.map((c: CountryCount) => ({ label: c.country, count: c.count }))}
          empty={t('profile.statsEmpty')}
        />
        <StatColumn
          title={t('profile.byGenre')}
          rows={profile.byGenre.map((g: GenreCount) => ({ label: g.tag, count: g.count }))}
          empty={t('profile.statsEmpty')}
        />
      </div>

      <nav className="mt-8 flex flex-wrap gap-x-6 gap-y-2">
        <Link to="/grimoire" className="font-mono text-xs uppercase tracking-[0.14em] text-accent no-underline hover:text-strong">
          {t('profile.toGrimoire')}
        </Link>
        <Link to="/mirror" className="font-mono text-xs uppercase tracking-[0.14em] text-accent no-underline hover:text-strong">
          {t('profile.toMirror')}
        </Link>
        <Link to="/atlas" className="font-mono text-xs uppercase tracking-[0.14em] text-accent no-underline hover:text-strong">
          {t('profile.toAtlas')}
        </Link>
        <Link to="/friends" className="font-mono text-xs uppercase tracking-[0.14em] text-accent no-underline hover:text-strong">
          {t('profile.toFriends')}
        </Link>
      </nav>
    </section>
  );
}

function StatColumn({
  title,
  rows,
  empty,
}: {
  title: string;
  rows: { label: string; count: number }[];
  empty: string;
}) {
  const max = rows.reduce((acc, row) => Math.max(acc, row.count), 0);

  return (
    <div>
      <h3 className="font-mono text-xs uppercase text-muted">{title}</h3>
      {rows.length === 0 ? (
        <p className="mt-2 font-mono text-xs text-muted">{empty}</p>
      ) : (
        <ul className="mt-2 space-y-1.5">
          {rows.map((row) => (
            <li key={row.label} className="font-mono text-xs">
              <div className="flex items-baseline justify-between gap-3">
                <span className="min-w-0 truncate text-strong">{row.label}</span>
                <span className="shrink-0 text-muted">{row.count}</span>
              </div>
              <span
                aria-hidden="true"
                className="mt-1 block h-px bg-accent"
                style={{ width: `${max > 0 ? Math.max(6, (row.count / max) * 100) : 0}%` }}
              />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// Settings: theme + language (surfaced here too, not removed from the nav), the grimoire export,
// the honest D28 note about sessions, and sign out.
function Settings() {
  const { t, i18n } = useTranslation();
  const { logout } = useAuth();
  const client = useGrimoireClient();
  const [theme, setTheme] = useState<Theme>(() => readTheme());
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState(false);

  function toggleTheme() {
    const next: Theme = theme === 'dark' ? 'light' : 'dark';
    applyTheme(next);
    setTheme(next);
  }

  function toggleLanguage() {
    const next = i18n.language === 'es' ? 'en' : 'es';
    void i18n.changeLanguage(next);
    persistLanguage(next);
  }

  async function exportGrimoire() {
    setExporting(true);
    setExportError(false);
    try {
      await downloadAuthenticated(
        client.profileExportUrl(),
        authStore.getAccessToken(),
        'grimoire.json',
      );
    } catch {
      setExportError(true);
    } finally {
      setExporting(false);
    }
  }

  return (
    <section>
      <SectionHead title={t('profile.settingsTitle')} />

      <div className="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          onClick={toggleTheme}
          className="border border-line px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-strong"
        >
          {theme === 'dark' ? t('profile.themeToLight') : t('profile.themeToDark')}
        </button>
        <button
          type="button"
          onClick={toggleLanguage}
          className="border border-line px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-strong"
        >
          {i18n.language === 'es' ? t('profile.languageToEn') : t('profile.languageToEs')}
        </button>
        <button
          type="button"
          onClick={() => void exportGrimoire()}
          disabled={exporting}
          className="border border-line px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-strong disabled:opacity-50"
        >
          {exporting ? t('profile.exporting') : t('profile.export')}
        </button>
      </div>

      {exportError ? (
        <p className="mt-2 font-mono text-xs text-danger">{t('profile.exportError')}</p>
      ) : null}

      <HandleSettings />
      <SessionSettings />

      <button
        type="button"
        onClick={logout}
        className="mt-8 border border-danger px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-danger hover:bg-danger hover:text-bg"
      >
        {t('profile.logout')}
      </button>
    </section>
  );
}

// The public handle (the FRIENDS wave): the name friends add you by. Shows the current handle or
// "not set", with an inline editor. The 409 (taken) and 400 (bad format) cases surface as friendly
// copy; the format rule (3–30 chars, lower-case a–z 0–9 _) is stated up front.
function HandleSettings() {
  const { t } = useTranslation();
  const profile = useProfile(true);
  const update = useUpdateHandle();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState('');

  const current = profile.data?.handle ?? null;

  function begin() {
    setValue(current ?? '');
    update.reset();
    setEditing(true);
  }

  function submit(event: React.FormEvent) {
    event.preventDefault();
    update.mutate(value.trim(), {
      onSuccess: () => {
        setEditing(false);
      },
    });
  }

  const taken = update.isError && update.error instanceof ApiError && update.error.status === 409;
  const badFormat =
    update.isError && update.error instanceof ApiError && update.error.status === 400;

  return (
    <div className="mt-8 border-t border-line pt-5">
      <p className="font-mono text-[0.7rem] uppercase tracking-[0.28em] text-accent">
        {t('profile.handleTitle')}
      </p>
      <p className="mt-1 max-w-prose font-mono text-xs text-muted">{t('profile.handleHint')}</p>

      {!editing ? (
        <div className="mt-3 flex flex-wrap items-baseline gap-3">
          <span className="font-body text-lg text-strong">
            {current !== null ? `@${current}` : t('profile.handleNotSet')}
          </span>
          <button
            type="button"
            onClick={begin}
            className="font-mono text-xs uppercase tracking-[0.14em] text-accent hover:text-strong"
          >
            {current !== null ? t('profile.handleEdit') : t('profile.handleSet')}
          </button>
        </div>
      ) : (
        <form onSubmit={submit} className="mt-3 flex flex-wrap items-start gap-3">
          <div className="min-w-0 flex-1">
            <label className="flex items-center gap-1 border border-line bg-panel px-3 py-2 focus-within:border-accent">
              <span className="font-mono text-sm text-muted">@</span>
              <input
                type="text"
                value={value}
                onChange={(event) => setValue(event.target.value.toLowerCase())}
                placeholder={t('profile.handlePlaceholder')}
                autoComplete="off"
                autoFocus
                minLength={3}
                maxLength={30}
                className="min-w-0 flex-1 bg-transparent font-body text-strong outline-none"
              />
            </label>
            <p className="mt-1 font-mono text-[0.7rem] text-muted">{t('profile.handleRule')}</p>
            {taken ? (
              <p className="mt-1 font-mono text-xs text-danger">{t('profile.handleTaken')}</p>
            ) : badFormat ? (
              <p className="mt-1 font-mono text-xs text-danger">{t('profile.handleBadFormat')}</p>
            ) : update.isError ? (
              <p className="mt-1 font-mono text-xs text-danger">{t('profile.handleError')}</p>
            ) : null}
          </div>
          <div className="flex gap-2">
            <button
              type="submit"
              disabled={update.isPending || value.trim().length === 0}
              className="border border-accent px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-accent hover:bg-accent hover:text-bg disabled:opacity-50"
            >
              {update.isPending ? t('profile.handleSaving') : t('profile.handleSave')}
            </button>
            <button
              type="button"
              onClick={() => setEditing(false)}
              className="border border-line px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-strong"
            >
              {t('profile.handleCancel')}
            </button>
          </div>
        </form>
      )}
    </div>
  );
}

// Active sessions (D28): every device signed in, the current one flagged. "Log out this session"
// revokes the current refresh token and signs you out here; "log out everywhere" revokes them all.
function SessionSettings() {
  const { t } = useTranslation();
  const { logout } = useAuth();
  const sessions = useSessions(true);
  const logoutAll = useLogoutAll();

  function logoutEverywhere() {
    logoutAll.mutate(undefined, {
      // Revoking every session kills the current one too, so sign out locally right after.
      onSuccess: () => {
        logout();
      },
    });
  }

  return (
    <div className="mt-8 border-t border-line pt-5">
      <p className="font-mono text-[0.7rem] uppercase tracking-[0.28em] text-accent">
        {t('profile.sessionsTitle')}
      </p>
      <p className="mt-1 max-w-prose font-mono text-xs text-muted">{t('profile.sessionsHint')}</p>

      {sessions.isLoading ? (
        <p className="mt-3 font-mono text-xs text-muted">{t('profile.sessionsLoading')}</p>
      ) : sessions.isError ? (
        <p className="mt-3 font-mono text-xs text-danger">{t('profile.sessionsError')}</p>
      ) : (sessions.data ?? []).length === 0 ? (
        <p className="mt-3 font-mono text-xs text-muted">{t('profile.sessionsEmpty')}</p>
      ) : (
        <ul className="mt-4 divide-y divide-line border-y border-line">
          {(sessions.data ?? []).map((session) => (
            <SessionRow key={session.id} session={session} />
          ))}
        </ul>
      )}

      <div className="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          onClick={logout}
          className="border border-line px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-muted hover:text-strong"
        >
          {t('profile.sessionLogoutThis')}
        </button>
        <button
          type="button"
          onClick={logoutEverywhere}
          disabled={logoutAll.isPending}
          className="border border-danger px-4 py-2 font-mono text-xs uppercase tracking-[0.14em] text-danger hover:bg-danger hover:text-bg disabled:opacity-50"
        >
          {logoutAll.isPending ? t('profile.sessionLoggingOutAll') : t('profile.sessionLogoutAll')}
        </button>
      </div>

      {logoutAll.isError ? (
        <p className="mt-2 font-mono text-xs text-danger">{t('profile.sessionLogoutAllError')}</p>
      ) : null}
    </div>
  );
}

function SessionRow({ session }: { session: Session }) {
  const { t } = useTranslation();

  return (
    <li className="py-3">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <span className="min-w-0 break-all font-body text-sm text-strong">
          {session.userAgent ?? t('profile.sessionUnknownDevice')}
          {session.current ? (
            <span className="ml-2 font-mono text-[0.6rem] uppercase tracking-[0.16em] text-accent">
              {t('profile.sessionCurrent')}
            </span>
          ) : null}
        </span>
        <span className="shrink-0 font-mono text-[0.7rem] text-muted">
          {session.createdByIp ?? t('profile.sessionUnknownIp')}
        </span>
      </div>
      <p className="mt-1 font-mono text-[0.7rem] text-muted">
        {t('profile.sessionDates', {
          created: session.createdAt.slice(0, 10),
          expires: session.expiresAt.slice(0, 10),
        })}
      </p>
    </li>
  );
}
