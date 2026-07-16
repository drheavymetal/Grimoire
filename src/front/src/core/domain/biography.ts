// Which biography to show a reader, out of the ones a band actually has. Pure and portable: the
// active language is handed in rather than read from i18next, so this stays DOM-free (Invariant 6)
// and a test can exercise every fallback without a browser.
//
// Grimoire never translates. The team has no self-hosted translator and every hosted one is a paid
// service, which Invariant 1 forbids outright (and inventing a translation would break Invariant 5
// besides). So the rule is: show the original, and always say what language it is in. A Spanish
// reader gets eswiki when it exists and English when it does not — labelled, never faked.

import type { Biography } from './types';

// The biography chosen for this reader, and whether it is the language they asked for.
export interface ChosenBiography {
  biography: Biography;
  // True when `biography.language` is not the reader's active language — the caller must say so
  // out loud. Reading a paragraph in a language you did not choose, with no warning, reads as a bug
  // in the app rather than a gap in Wikipedia.
  isFallback: boolean;
}

// The reader's language as a bare code: i18next hands out regional tags ('es-ES', 'en-GB') but
// Wikipedia names its editions with the bare subtag, and there is no es-ES wiki to match against.
export function baseLanguage(language: string): string {
  return language.split('-')[0]!.toLowerCase();
}

// Preference order: the reader's own language, then English, then whatever exists.
//
// English is the second choice rather than the widest-coverage one on purpose — it is the only
// language the whole team reads, and it is where coverage actually is (only English feeds the
// embedding, so it is the edition the catalogue has most of). "Whatever exists" is last and, today,
// unreachable: only 'en' and 'es' are configured. It is here because the storage is language-
// agnostic by design, so the day 'no' or 'sv' is switched on, this already does the right thing
// instead of silently showing nothing.
export function chooseBiography(
  biographies: readonly Biography[],
  activeLanguage: string,
): ChosenBiography | null {
  const usable = biographies.filter((b) => b.abstract.trim().length > 0);

  if (usable.length === 0) {
    return null;
  }

  const active = baseLanguage(activeLanguage);

  const preferred =
    usable.find((b) => baseLanguage(b.language) === active) ??
    usable.find((b) => baseLanguage(b.language) === 'en') ??
    usable[0]!;

  return {
    biography: preferred,
    isFallback: baseLanguage(preferred.language) !== active,
  };
}
