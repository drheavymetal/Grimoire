import { useMemo } from 'react';
import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useAtlas } from '../../core/hooks/useAtlas';
import { starsNearTaste } from '../../core/domain/atlas';
import { useAuth } from '../auth/AuthProvider';
import { AtlasCanvas } from '../atlas/AtlasCanvas';

// How many stars around the taste are painted alive (sulphur). Mirrors AtlasCanvas ALIVE_COUNT.
const ALIVE_COUNT = 28;

// The Atlas page (C18/B22): the whole catalogue as a star field, with the user's taste marked when
// known. Wired to the useAtlas core hook (real xy from the backend), so it is not an aesthetic
// shell. The near-taste selection is a pure core function, tested without a canvas.
export function AtlasPage() {
  const { t } = useTranslation();
  const { isAuthenticated } = useAuth();
  const { data, isLoading, isError } = useAtlas(isAuthenticated);

  const aliveIds = useMemo(
    () => starsNearTaste(data?.stars ?? [], data?.taste ?? null, ALIVE_COUNT),
    [data?.stars, data?.taste],
  );

  return (
    <section>
      <h1 className="font-display text-4xl text-strong">{t('atlas.heading')}</h1>
      <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('atlas.intro')}</p>

      {isLoading ? <p className="mt-6 font-mono text-sm text-muted">{t('atlas.loading')}</p> : null}
      {isError ? <p className="mt-6 font-mono text-sm text-danger">{t('atlas.error')}</p> : null}

      {data !== undefined && !isError ? (
        <>
          <AtlasCanvas atlas={data} aliveIds={aliveIds} />

          {data.taste === null && data.stars.length > 0 ? (
            <p className="mt-3 max-w-prose font-body text-sm text-muted">
              {isAuthenticated ? (
                <>
                  {t('atlas.noTasteSignedIn')}{' '}
                  <Link to="/rite" className="text-accent no-underline hover:text-strong">
                    {t('atlas.toRite')}
                  </Link>
                </>
              ) : (
                <>
                  {t('atlas.noTasteAnon')}{' '}
                  <Link to="/rite" className="text-accent no-underline hover:text-strong">
                    {t('atlas.toRite')}
                  </Link>
                </>
              )}
            </p>
          ) : null}
        </>
      ) : null}
    </section>
  );
}
