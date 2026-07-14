import { redactionCutForRank, redactionFontFamily } from '../core/domain/redaction';
import type { Rank } from '../core/domain/types';

// A band name rendered in the Redaction cut its rank earns (Q1 / DESIGN §3, D14/D38): the
// typography IS the datum. Known reads clean (cut 10), Nameless is heavily eroded but still legible
// (cut 70), and an unknown rank falls back to the clean base — unknown is not rare (D35). The
// corrosion is only ever for band NAMES, never the app mark (D27). Pure rank→cut mapping lives in
// core; this only paints, so the same signature works on the ficha, search and reveal.
export function RankedName({
  name,
  rank,
  className,
}: {
  name: string;
  rank: Rank | null;
  className?: string;
}) {
  const cut = redactionCutForRank(rank);

  return (
    <span className={className} style={{ fontFamily: redactionFontFamily(cut) }}>
      {name}
    </span>
  );
}
