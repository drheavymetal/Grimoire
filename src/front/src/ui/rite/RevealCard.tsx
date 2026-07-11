import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { RiteReveal } from '../../core/domain/types';
import { RevealName } from './RevealName';

// The reveal card shared by the summon (RiteConsole) and the duel winner (DuelConsole): the band
// name develops in (RevealName), then its origin, tags, the C4 explanation (distance, shared tags,
// shared members), and a link to the full ficha. `marker` is the small label above the name
// ("Summoned", "Preferred") so the same card serves both surfaces.
export function RevealCard({ reveal, marker }: { reveal: RiteReveal; marker: string }) {
  const { t } = useTranslation();
  const { artist, why } = reveal;

  return (
    // A surface of impact (Q2 hybrid, DESIGN §2): the photocopied flyer grain in light mode. Dark
    // mode stays clean (the cassette). The .flyer class paints grain only in light.
    <div className="flyer border border-accent p-6">
      <p className="font-mono text-xs uppercase text-accent">{marker}</p>
      <div className="mt-2">
        <RevealName name={artist.name} rank={artist.rank} />
      </div>

      <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 font-mono text-xs text-muted">
        <dt className="uppercase">{t('artist.origin')}</dt>
        <dd className="text-strong">
          {artist.country ?? '—'}
          {artist.city ? ` · ${artist.city}` : ''}
        </dd>
        <dt className="uppercase">{t('artist.formed')}</dt>
        <dd className="text-strong">{artist.formedYear ?? '—'}</dd>
      </dl>

      {artist.tags.length > 0 ? (
        <ul className="mt-3 flex flex-wrap gap-2">
          {artist.tags.map((tag) => (
            <li key={tag} className="border border-line px-2 py-1 font-mono text-xs text-strong">
              {tag}
            </li>
          ))}
        </ul>
      ) : null}

      {/* C4 explainability: without "why you were served this", a strange recommender looks broken. */}
      <div className="mt-5 border-t border-line pt-4">
        <h3 className="font-mono text-xs uppercase text-muted">{t('rite.why')}</h3>
        <p className="mt-2 font-mono text-xs text-muted">
          {t('rite.whyDistance', { distance: why.distance.toFixed(3) })}
        </p>
        {why.sharedTags.length > 0 ? (
          <p className="mt-1 font-mono text-xs text-muted">
            {t('rite.whyTags', { tags: why.sharedTags.join(', ') })}
          </p>
        ) : null}
        {why.sharedMembers.length > 0 ? (
          <p className="mt-1 font-mono text-xs text-muted">
            {t('rite.whyMembers', { members: why.sharedMembers.join(', ') })}
          </p>
        ) : null}
        {why.sharedTags.length === 0 && why.sharedMembers.length === 0 ? (
          <p className="mt-1 font-mono text-xs text-muted">{t('rite.whyNothingShared')}</p>
        ) : null}
      </div>

      <Link
        to="/artist/$artistId"
        params={{ artistId: artist.id }}
        className="mt-5 inline-block font-mono text-xs uppercase text-accent no-underline hover:text-strong"
      >
        {t('rite.openFiche')} →
      </Link>
    </div>
  );
}
