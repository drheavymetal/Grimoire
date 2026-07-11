// Pure, portable helpers for per-release credits (feature B9). No DOM, no platform coupling
// (invariant 6, D12): the ui/ layer renders what these return. Tested without a browser.

import type { PerformerCredit } from './types';

// The performers of a release split into official members and guests/session players. The
// member-vs-guest distinction is the load-bearing one (D9): confusing them ruins the Gantt, so
// the UI renders the two groups apart, under their own headings. Order within each group is
// preserved from the backend (members-then-name), so this only partitions.
export interface SplitPerformers {
  members: PerformerCredit[];
  guests: PerformerCredit[];
}

export function splitPerformers(performers: readonly PerformerCredit[]): SplitPerformers {
  const members: PerformerCredit[] = [];
  const guests: PerformerCredit[] = [];

  for (const performer of performers) {
    if (performer.isGuest) {
      guests.push(performer);
    } else {
      members.push(performer);
    }
  }

  return { members, guests };
}

// Whether a release has any credit at all to show. A release with neither performers nor
// production is one the ETL never reached — the UI shows a designed "no credits" state, never a
// blank (R2: the ficha degrades with dignity).
export function hasCredits(credits: {
  performers: readonly PerformerCredit[];
  production: readonly unknown[];
}): boolean {
  return credits.performers.length > 0 || credits.production.length > 0;
}
