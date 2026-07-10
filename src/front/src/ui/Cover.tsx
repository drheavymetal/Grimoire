import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useGrimoireClient } from '../core/api/context';

type Status = 'loading' | 'loaded' | 'missing';

// A release cover, proxied and disk-cached by the API (feature B6). Coverage is worst
// exactly on the dark bands the engine leads to (R2), so the missing state is designed
// copy — a blank sleeve that says what is absent — not a broken image icon.
export function Cover({ mbid, title }: { mbid: string; title: string }) {
  const { t } = useTranslation();
  const client = useGrimoireClient();
  const [status, setStatus] = useState<Status>('loading');

  return (
    <div className="relative aspect-square w-14 shrink-0 overflow-hidden border border-line bg-panel">
      {status !== 'missing' ? (
        <img
          src={client.coverUrl(mbid)}
          alt={t('cover.alt', { title })}
          loading="lazy"
          onLoad={() => setStatus('loaded')}
          onError={() => setStatus('missing')}
          className={`h-full w-full object-cover transition-opacity duration-300 ${
            status === 'loaded' ? 'opacity-100' : 'opacity-0'
          }`}
        />
      ) : (
        <div className="flex h-full w-full items-center justify-center p-1 text-center">
          <span className="font-mono text-[0.55rem] uppercase leading-tight tracking-wide text-muted">
            {t('cover.none')}
          </span>
        </div>
      )}
    </div>
  );
}
