import { describe, expect, it } from 'vitest';
import {
  DEFAULT_EXPLORE_SECTIONS,
  EXPLORE_SECTION_IDS,
  parseExploreSections,
  serialiseExploreSections,
} from './exploreSections';

describe('DEFAULT_EXPLORE_SECTIONS', () => {
  it('opens the wall of covers and folds the other six', () => {
    expect(DEFAULT_EXPLORE_SECTIONS.wall).toBe(true);

    const open = EXPLORE_SECTION_IDS.filter((id) => DEFAULT_EXPLORE_SECTIONS[id]);
    expect(open).toEqual(['wall']);
  });

  it('covers every section the hub renders', () => {
    expect(EXPLORE_SECTION_IDS).toHaveLength(7);
    expect(Object.keys(DEFAULT_EXPLORE_SECTIONS).sort()).toEqual([...EXPLORE_SECTION_IDS].sort());
  });
});

describe('parseExploreSections', () => {
  it('falls back to the default when nothing was ever stored', () => {
    expect(parseExploreSections(null)).toEqual(DEFAULT_EXPLORE_SECTIONS);
  });

  it('falls back to the default on an empty string', () => {
    // authStore clears its keys by writing '', so an empty value is a real thing to meet.
    expect(parseExploreSections('')).toEqual(DEFAULT_EXPLORE_SECTIONS);
  });

  it('falls back to the default on corrupt JSON instead of throwing', () => {
    expect(() => parseExploreSections('{"wall":')).not.toThrow();
    expect(parseExploreSections('{"wall":')).toEqual(DEFAULT_EXPLORE_SECTIONS);
    expect(parseExploreSections('not json at all')).toEqual(DEFAULT_EXPLORE_SECTIONS);
  });

  it('falls back to the default on JSON that is not a record', () => {
    expect(parseExploreSections('null')).toEqual(DEFAULT_EXPLORE_SECTIONS);
    expect(parseExploreSections('42')).toEqual(DEFAULT_EXPLORE_SECTIONS);
    expect(parseExploreSections('"wall"')).toEqual(DEFAULT_EXPLORE_SECTIONS);
    expect(parseExploreSections('["wall"]')).toEqual(DEFAULT_EXPLORE_SECTIONS);
  });

  it('restores what the reader left open', () => {
    const stored = JSON.stringify({ wall: false, splits: true });
    const state = parseExploreSections(stored);

    expect(state.wall).toBe(false);
    expect(state.splits).toBe(true);
    // Sections absent from the record keep their default.
    expect(state.rare).toBe(false);
  });

  it('ignores non-boolean values and keys it does not know', () => {
    const stored = JSON.stringify({ wall: 'yes', rare: 1, splits: true, ghost: true });
    const state = parseExploreSections(stored);

    expect(state.wall).toBe(DEFAULT_EXPLORE_SECTIONS.wall);
    expect(state.rare).toBe(DEFAULT_EXPLORE_SECTIONS.rare);
    expect(state.splits).toBe(true);
    expect(state).not.toHaveProperty('ghost');
  });
});

describe('serialiseExploreSections', () => {
  it('round-trips through the parser', () => {
    const state = { ...DEFAULT_EXPLORE_SECTIONS, wall: false, prolific: true };
    expect(parseExploreSections(serialiseExploreSections(state))).toEqual(state);
  });

  it('writes only the known ids', () => {
    const state = { ...DEFAULT_EXPLORE_SECTIONS, ghost: true } as never;
    const written = JSON.parse(serialiseExploreSections(state)) as Record<string, boolean>;

    expect(Object.keys(written).sort()).toEqual([...EXPLORE_SECTION_IDS].sort());
  });
});
