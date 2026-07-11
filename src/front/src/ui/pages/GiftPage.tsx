import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { usePeekGift, useRevealGift } from '../../core/hooks/useGift';
import { RitePlayer } from '../rite/RitePlayer';
import { RankedName } from '../RankedName';
import { PageHeader } from '../PageHeader';

// C22 — receiving a gift. The band arrives face down and signed: the recipient hears it blind, not
// knowing whether it is a gift or a trap, and turns it over only if they like it. Reuses the exact
// blind-audio player and anti-leak proxy of the Rite. An invalid or tampered token is a designed
// "not a real gift" state.

export function GiftPage({ token }: { token: string }) {
  const { t } = useTranslation();
  const peek = usePeekGift(token);
  const reveal = useRevealGift();

  if (peek.isLoading) {
    return <p className="font-mono text-sm text-muted">{t('gift.loading')}</p>;
  }

  if (peek.isError || peek.data === undefined) {
    return (
      <div className="border border-line border-dashed p-8 text-center">
        <p className="font-display text-xl text-strong">{t('gift.invalidTitle')}</p>
        <p className="mt-2 font-body text-sm text-muted">{t('gift.invalidBody')}</p>
      </div>
    );
  }

  const revealed = reveal.data;

  return (
    <section>
      <PageHeader
        eyebrow={t('gift.eyebrow')}
        title={t('gift.heading')}
        lead={<p className="font-body text-sm text-muted">{t('gift.intro')}</p>}
      />

      {peek.data.note ? (
        <blockquote className="mt-6 border-l-2 border-accent bg-panel px-4 py-3 font-body italic text-strong">
          “{peek.data.note}”
        </blockquote>
      ) : null}

      <div className="mt-6">
        <RitePlayer audioUrl={peek.data.audioUrl} />
      </div>

      {revealed === undefined ? (
        <div className="mt-6">
          <button
            type="button"
            onClick={() => reveal.mutate(token)}
            disabled={reveal.isPending}
            className="w-full border border-accent px-4 py-3 font-mono text-sm uppercase text-accent hover:bg-accent hover:text-bg disabled:opacity-50"
          >
            {reveal.isPending ? t('gift.revealing') : t('gift.reveal')}
          </button>
          <p className="mt-2 text-center font-mono text-xs text-muted">{t('gift.revealHint')}</p>
          {reveal.isError ? (
            <p className="mt-2 text-center font-mono text-xs text-danger">{t('gift.revealError')}</p>
          ) : null}
        </div>
      ) : (
        <div className="mt-6 border border-line p-6 text-center">
          <p className="font-mono text-xs uppercase text-muted">{t('gift.revealedLabel')}</p>
          <h2 className="mt-2">
            <RankedName name={revealed.name} rank={revealed.rank} className="text-4xl text-strong" />
          </h2>
          <p className="mt-2 font-mono text-xs text-muted">
            {revealed.country ?? t('search.countryUnknown')}
            {revealed.formedYear !== null ? ` · ${revealed.formedYear}` : ''}
          </p>
          <Link
            to="/artist/$artistId"
            params={{ artistId: revealed.id }}
            className="mt-4 inline-block border border-line px-4 py-2 font-mono text-xs uppercase text-strong no-underline hover:border-accent hover:text-accent"
          >
            {t('gift.openFiche')}
          </Link>
        </div>
      )}
    </section>
  );
}
