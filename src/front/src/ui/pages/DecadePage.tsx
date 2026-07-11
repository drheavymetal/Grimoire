import { RiteGate } from '../rite/RiteGate';
import { DecadeConsole } from '../rite/DecadeConsole';

// audit-ok: composition wrapper; gating and data live in RiteGate (core hook useTaste) and in
// DecadeConsole (serves/scores via core hooks). No direct core import here by design.
// Guess the decade (feature C27), behind the shared Rite gate (sign in -> cold start -> the game).
export function DecadePage() {
  return (
    <RiteGate>
      <DecadeConsole />
    </RiteGate>
  );
}
