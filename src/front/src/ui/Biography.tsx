import { useTranslation } from 'react-i18next';
import { chooseBiography } from '../core/domain/biography';
import type { Biography as BiographyData } from '../core/domain/types';

// The band's Wikipedia biography, in the reader's language when Wikipedia has one there and in
// whatever language it does exist in otherwise — always labelled with the language it is in.
//
// Grimoire does not translate (no self-hosted translator, and every hosted one is a paid service
// Invariant 1 forbids). So the honest move is to show the original and name its language: a
// paragraph in a language you did not ask for, unannounced, reads as a broken app rather than as
// Wikipedia's gap. Choosing is pure and tested in core/domain/biography.ts; this file only renders.

// Names the language in the reader's own language ("Norwegian" / "noruego") via Intl, which is
// ECMA-402 rather than DOM, so it survives the React Native port (Invariant 6). Falls back to the
// bare code when the runtime has no name for it — a label saying "no" beats a label saying nothing.
function languageName(code: string, activeLanguage: string): string {
  try {
    const names = new Intl.DisplayNames([activeLanguage], { type: 'language' });
    return names.of(code) ?? code;
  } catch {
    return code;
  }
}

export function Biography({ biographies }: { biographies: BiographyData[] }) {
  const { t, i18n } = useTranslation();
  const chosen = chooseBiography(biographies, i18n.language);

  if (chosen === null) {
    return (
      <section className="mt-8">
        <h2 className="font-mono text-xs uppercase text-muted">{t('artist.bio')}</h2>
        <p className="mt-2 font-mono text-xs text-muted">{t('artist.noBio')}</p>
      </section>
    );
  }

  const { biography, isFallback } = chosen;
  const name = languageName(biography.language, i18n.language);

  return (
    <section className="mt-8">
      <h2 className="font-mono text-xs uppercase text-muted">{t('artist.bio')}</h2>

      {/* lang= is not decoration: it tells a screen reader to switch pronunciation, and lets the
          browser hyphenate a Norwegian paragraph as Norwegian. */}
      <p lang={biography.language} className="mt-2 max-w-prose font-body leading-relaxed text-strong">
        {biography.abstract}
      </p>

      {/* Said plainly, and only when the language is not the one the reader chose — a Spanish reader
          being handed English is the common case for the underground and must never be silent. */}
      {isFallback ? (
        <p className="mt-2 font-mono text-xs text-muted">{t('artist.abstractFallback', { language: name })}</p>
      ) : null}

      {/* CC BY-SA attribution, pointing at the article the text above actually came from — crediting
          enwiki for eswiki's words would be a licence violation, not a cosmetic slip. */}
      <p className="mt-2 font-mono text-xs text-muted">
        {biography.url !== null ? (
          <a
            href={biography.url}
            target="_blank"
            rel="noreferrer noopener"
            className="text-muted underline hover:text-accent"
          >
            {t('artist.abstractSource', { language: name })}
          </a>
        ) : (
          t('artist.abstractSource', { language: name })
        )}{' '}
        (CC BY-SA)
      </p>
    </section>
  );
}
