import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useResolve } from '../../core/hooks/useRite';
import type { RiteAction, RiteReveal, WeeklyItem as WeeklyItemData } from '../../core/domain/types';
import { RitePlayer } from '../rite/RitePlayer';
import { RevealName } from '../rite/RevealName';

type Phase = 'listening' | 'revealed' | 'blindResolved' | 'alreadyDone';

// One of the Weekly Rite's seven (feature B17). Plays blind through the shared audio proxy and
// resolves with Summon/Banish/Again — the same mechanic as the daily rite. Only a summon reveals
// the band; banish and again stay blind (C3/C20). Items already judged this week show as done.
export function WeeklyItem({ item, index }: { item: WeeklyItemData; index: number }) {
  const { t } = useTranslation();
  const resolve = useResolve();

  const [phase, setPhase] = useState<Phase>(item.resolved ? 'alreadyDone' : 'listening');
  const [reveal, setReveal] = useState<RiteReveal | null>(null);
  const [lastAction, setLastAction] = useState<RiteAction | null>(null);

  function act(action: RiteAction) {
    resolve.mutate(
      { token: item.token, action },
      {
        onSuccess: (result) => {
          setLastAction(action);
          if (action === 'summon' && result.reveal !== null) {
            setReveal(result.reveal);
            setPhase('revealed');
          } else {
            setReveal(null);
            setPhase('blindResolved');
          }
        },
      },
    );
  }

  return (
    <li className="border border-line p-4">
      <div className="flex items-baseline justify-between">
        <span className="font-mono text-xs uppercase text-muted">
          {t('weekly.itemLabel', { n: index + 1 })}
        </span>
        <span className="font-mono text-[0.65rem] uppercase text-muted">
          {t('weekly.risk', { pct: Math.round(item.riskPercentile * 100) })}
        </span>
      </div>

      {phase === 'alreadyDone' ? (
        <p className="mt-3 font-mono text-xs text-muted">
          {t('weekly.alreadyResolved', { state: t(`weekly.state.${item.state}`) })}
        </p>
      ) : (
        <div className="mt-3 space-y-3">
          <RitePlayer key={item.token} audioUrl={item.audioUrl} autoPlay={false} />

          {phase === 'listening' ? (
            <div className="grid grid-cols-3 gap-2">
              <button
                type="button"
                onClick={() => act('summon')}
                disabled={resolve.isPending}
                className="border border-accent px-2 py-2 font-display text-base text-accent hover:bg-accent hover:text-bg disabled:opacity-40"
              >
                {t('rite.summon')}
              </button>
              <button
                type="button"
                onClick={() => act('again')}
                disabled={resolve.isPending}
                className="border border-line px-2 py-2 font-display text-base text-muted hover:border-strong hover:text-strong disabled:opacity-40"
              >
                {t('rite.again')}
              </button>
              <button
                type="button"
                onClick={() => act('banish')}
                disabled={resolve.isPending}
                className="border border-danger px-2 py-2 font-display text-base text-danger hover:bg-danger hover:text-bg disabled:opacity-40"
              >
                {t('rite.banish')}
              </button>
            </div>
          ) : null}

          {phase === 'revealed' && reveal !== null ? (
            <div className="flyer border border-accent p-4">
              <p className="font-mono text-xs uppercase text-accent">{t('rite.summoned')}</p>
              <div className="mt-1">
                <RevealName name={reveal.artist.name} rank={reveal.artist.rank} />
              </div>
              <p className="mt-2 font-mono text-xs text-muted">
                {reveal.artist.country ?? '—'} · {reveal.artist.formedYear ?? '—'}
              </p>
            </div>
          ) : null}

          {phase === 'blindResolved' && lastAction !== null ? (
            <p className="font-mono text-xs text-muted">
              {lastAction === 'banish' ? t('rite.banishedTitle') : t('rite.againTitle')}
            </p>
          ) : null}
        </div>
      )}
    </li>
  );
}
