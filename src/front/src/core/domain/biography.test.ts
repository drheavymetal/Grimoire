import { describe, expect, it } from 'vitest';
import { baseLanguage, chooseBiography } from './biography';
import type { Biography } from './types';

const en: Biography = {
  language: 'en',
  abstract: 'Darkthrone are a Norwegian black metal band, formed in 1986.',
  url: 'https://en.wikipedia.org/wiki/Darkthrone',
};

const es: Biography = {
  language: 'es',
  abstract: 'Darkthrone es una banda noruega de black metal, formada en 1986.',
  url: 'https://es.wikipedia.org/wiki/Darkthrone',
};

const no: Biography = {
  language: 'no',
  abstract: 'Darkthrone er et norsk black metal-band, dannet i 1986.',
  url: 'https://no.wikipedia.org/wiki/Darkthrone',
};

describe('baseLanguage', () => {
  it('strips the region i18next tacks on: there is no es-ES wikipedia to match', () => {
    expect(baseLanguage('es-ES')).toBe('es');
    expect(baseLanguage('en-GB')).toBe('en');
  });

  it('lowercases, so a config or header casing never misses a match', () => {
    expect(baseLanguage('ES')).toBe('es');
  });

  it('leaves a bare code alone', () => {
    expect(baseLanguage('es')).toBe('es');
  });
});

describe('chooseBiography', () => {
  it('gives a Spanish reader the Spanish article when eswiki has one', () => {
    const chosen = chooseBiography([en, es], 'es');

    expect(chosen?.biography.language).toBe('es');
    expect(chosen?.isFallback).toBe(false);
  });

  it('gives an English reader English even though a Spanish article exists', () => {
    // The rule is the READER's language first, not a fixed es-over-en priority: an English reader
    // seeing Spanish because eswiki happened to sort first would be the same bug in a mirror.
    const chosen = chooseBiography([en, es], 'en');

    expect(chosen?.biography.language).toBe('en');
    expect(chosen?.isFallback).toBe(false);
  });

  it('falls back to English for a Spanish reader when eswiki has nothing — and says so', () => {
    // The common case for the underground, and the whole reason isFallback exists: the page must
    // announce the language rather than quietly serve English to someone reading in Spanish.
    const chosen = chooseBiography([en], 'es');

    expect(chosen?.biography.language).toBe('en');
    expect(chosen?.isFallback).toBe(true);
  });

  it('falls back to any language when neither the reader nor English is available', () => {
    // Unreachable today (only en/es are configured) but the storage is language-agnostic on
    // purpose: switching 'no' on must show the Norwegian text, labelled, not an empty section.
    const chosen = chooseBiography([no], 'es');

    expect(chosen?.biography.language).toBe('no');
    expect(chosen?.isFallback).toBe(true);
  });

  it('prefers English over another language when the reader language is missing', () => {
    const chosen = chooseBiography([no, en], 'es');

    expect(chosen?.biography.language).toBe('en');
    expect(chosen?.isFallback).toBe(true);
  });

  it('matches a regional active language against the bare edition code', () => {
    const chosen = chooseBiography([en, es], 'es-419');

    expect(chosen?.biography.language).toBe('es');
    expect(chosen?.isFallback).toBe(false);
  });

  it('returns null when the band has no biography at all', () => {
    expect(chooseBiography([], 'es')).toBeNull();
  });

  it('ignores blank text rather than rendering an empty section', () => {
    const blank: Biography = { language: 'es', abstract: '   ', url: 'https://es.wikipedia.org/wiki/X' };

    const chosen = chooseBiography([blank, en], 'es');

    expect(chosen?.biography.language).toBe('en');
    expect(chosen?.isFallback).toBe(true);
  });

  it('returns null when every biography is blank', () => {
    const blank: Biography = { language: 'en', abstract: '', url: null };

    expect(chooseBiography([blank], 'en')).toBeNull();
  });

  it('keeps the article url of the language it chose, for CC BY-SA attribution', () => {
    // Attribution has to credit the text actually shown; crediting enwiki for eswiki's words would
    // be a licence violation, not a cosmetic slip.
    const chosen = chooseBiography([en, es], 'es');

    expect(chosen?.biography.url).toBe('https://es.wikipedia.org/wiki/Darkthrone');
  });
});
