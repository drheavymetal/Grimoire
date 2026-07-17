// Which of the Explore hub's seven sections are unfolded. Pure and portable (invariant 6, D12): the
// shape and its defaults live here, the platform/ adapter owns the key and the storage call, and ui/
// renders it. A section that is folded fires no query at all, so this state is not decoration — it
// decides what the page costs on mount.
//
// The parser is deliberately forgiving. This state is persisted client-side, which means it is user
// -writable and survives across deploys: a hand-edited value, a half-written record, or a key we
// renamed last month must all degrade to the default rather than take the page down with them.

export const EXPLORE_SECTION_IDS = [
  'wall',
  'compare',
  'duration',
  'rare',
  'oneAlbum',
  'prolific',
  'splits',
] as const;

export type ExploreSectionId = (typeof EXPLORE_SECTION_IDS)[number];

export type ExploreSectionState = Record<ExploreSectionId, boolean>;

// The wall of covers opens, the other six stay folded. Opening everything would keep the page the
// same wall of scroll a reader complained about; folding everything would make it read as broken.
// One section open says "this unfolds" without paying for the other six.
export const DEFAULT_EXPLORE_SECTIONS: ExploreSectionState = {
  wall: true,
  compare: false,
  duration: false,
  rare: false,
  oneAlbum: false,
  prolific: false,
  splits: false,
};

// Reads a persisted record back into state. Anything unparseable, of the wrong shape, or carrying a
// non-boolean falls back to the default for that section — never throws.
export function parseExploreSections(stored: string | null): ExploreSectionState {
  const state: ExploreSectionState = { ...DEFAULT_EXPLORE_SECTIONS };

  if (stored === null || stored.length === 0) {
    return state;
  }

  let parsed: unknown;

  try {
    parsed = JSON.parse(stored);
  } catch {
    return state;
  }

  // Arrays and null are typeof 'object' too; neither is a record of sections.
  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return state;
  }

  const record = parsed as Record<string, unknown>;

  for (const id of EXPLORE_SECTION_IDS) {
    const value = record[id];

    if (typeof value === 'boolean') {
      state[id] = value;
    }
  }

  return state;
}

// Serialises only the known ids, so a stale key from an older shape does not ride along forever.
export function serialiseExploreSections(state: ExploreSectionState): string {
  const record: Record<string, boolean> = {};

  for (const id of EXPLORE_SECTION_IDS) {
    record[id] = state[id];
  }

  return JSON.stringify(record);
}
