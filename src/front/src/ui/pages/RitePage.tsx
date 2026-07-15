import { RiteGate } from '../rite/RiteGate';
import { RiteConsole } from '../rite/RiteConsole';
import type { RiteScope } from '../../core/domain/types';

// audit-ok: this is a composition wrapper; the gating and data wiring live in RiteGate (which reads
// the taste through the core hook useTaste) and in RiteConsole (which serves/resolves via core). The
// page imports no core hook directly on purpose — the gate is shared across the three Rite surfaces.
// The Rite entry point: the shared three-gate guard (anonymous -> sign in, no taste -> cold start,
// has taste -> the console) wrapping the rite console.
export function RitePage({ scope }: { scope?: RiteScope }) {
  return (
    <RiteGate>
      <RiteConsole scope={scope} />
    </RiteGate>
  );
}
