import { useTranslation } from 'react-i18next';
import { useSeed } from '../../core/hooks/useColdStart';
import { useSeedGrid } from '../../core/hooks/useSeedGrid';
import { LastFmImport, SeedGrid } from './SeedPicker';
import { PageHeader } from '../PageHeader';

const REQUIRED_PICKS = 5;

// Cold start (D15): a new user has no taste vector, so The Rite cannot run. They seed it by
// choosing bands they already know, or by importing Last.fm (feature C1, currently blocked
// with no API key -> a dignified "not available yet", not a broken error).
//
// The picker grid itself (which GROWS and never reshuffles) lives in SeedPicker.tsx so the profile's
// "reselect your bands" panel can reuse the exact same experience. See core/domain/seedGrid.ts.
export function ColdStart() {
  const { t } = useTranslation();
  const grid = useSeedGrid(true);
  const seed = useSeed();

  const enough = grid.picked.size >= REQUIRED_PICKS;

  return (
    <section>
      <PageHeader
        eyebrow={t('coldStart.eyebrow')}
        title={t('coldStart.heading')}
        lead={<p className="font-body text-strong">{t('coldStart.intro')}</p>}
      />
      <p className="mt-3 font-mono text-xs text-muted">
        {t('coldStart.counter', { count: grid.picked.size, required: REQUIRED_PICKS })}
      </p>

      <SeedGrid
        grid={grid.grid}
        picked={grid.picked}
        full={grid.full}
        expanding={grid.expanding}
        isLoading={grid.isLoading}
        isError={grid.isError}
        onToggle={grid.toggle}
      />

      {seed.isError ? <p className="mt-4 font-mono text-sm text-danger">{t('coldStart.seedError')}</p> : null}

      <button
        type="button"
        disabled={!enough || seed.isPending}
        onClick={() => seed.mutate([...grid.picked])}
        className="mt-6 w-full border border-accent bg-accent px-4 py-3 font-display text-lg text-bg disabled:opacity-40"
      >
        {seed.isPending ? t('coldStart.seeding') : t('coldStart.seed')}
      </button>

      <LastFmImport />
    </section>
  );
}
