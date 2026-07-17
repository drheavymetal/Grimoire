import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useScenes } from '../../core/hooks/useScenes';
import type { Scene } from '../../core/domain/types';
import { PageHeader } from '../PageHeader';

// B20/C11 — Scenes. Not a country map (D17): the unit is the local scene, a city and a decade and a
// sound family taken together. Ranked by lift — how far the place departs from the catalogue's own
// average — because ranking by headcount only ever surfaced the megacity wearing the vaguest tag.
// Real data off city/formed_year/tags; a thin catalogue yields few scenes and renders a designed
// empty state, never a fake grid.

export function ScenesPage() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useScenes();

  const scenes = data ?? [];

  return (
    <section>
      <PageHeader
        eyebrow={t('scenes.eyebrow')}
        title={t('scenes.heading')}
        lead={<p className="font-mono text-xs text-muted">{t('scenes.intro')}</p>}
      />

      {isLoading ? <p className="mt-6 font-mono text-sm text-muted">{t('scenes.loading')}</p> : null}
      {isError ? <p className="mt-6 font-mono text-sm text-danger">{t('scenes.error')}</p> : null}

      {!isLoading && !isError && scenes.length === 0 ? (
        <div className="mt-6 border border-line border-dashed p-8 text-center">
          <p className="font-body text-sm text-muted">{t('scenes.empty')}</p>
        </div>
      ) : null}

      {scenes.length > 0 ? (
        <div className="mt-6 grid gap-5 sm:grid-cols-2">
          {scenes.map((scene) => (
            <SceneCard key={`${scene.city}-${scene.decade}-${scene.family}`} scene={scene} />
          ))}
        </div>
      ) : null}
    </section>
  );
}

function SceneCard({ scene }: { scene: Scene }) {
  const { t, i18n } = useTranslation();

  // The lift is why the scene is on the page at all, so it is rendered as prose, not a bare ratio.
  const lift = new Intl.NumberFormat(i18n.language, {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(scene.lift);

  return (
    <article className="border border-line p-4">
      <header className="flex items-baseline justify-between gap-3 border-b border-line pb-2">
        <div>
          <h2 className="font-display text-xl text-strong">{scene.city}</h2>
          <p className="font-mono text-xs uppercase text-accent">
            {t('scenes.decadeLabel', { decade: scene.decade })} · {scene.family}
          </p>
        </div>
        <span className="shrink-0 font-mono text-xs text-muted">
          {t('scenes.bandCount', { count: scene.size })}
        </span>
      </header>

      <p className="mt-2 font-mono text-xs text-muted">{t('scenes.lift', { lift })}</p>
      <ul className="mt-3 flex flex-wrap gap-x-3 gap-y-1.5">
        {scene.bands.map((band) => (
          <li key={band.id}>
            <Link
              to="/artist/$artistId"
              params={{ artistId: band.id }}
              className="font-body text-strong no-underline hover:text-accent"
            >
              {band.name}
            </Link>
          </li>
        ))}
      </ul>
    </article>
  );
}
