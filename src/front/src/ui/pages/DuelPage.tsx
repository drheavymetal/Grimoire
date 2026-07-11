import { RiteGate } from '../rite/RiteGate';
import { DuelConsole } from '../rite/DuelConsole';

// audit-ok: composition wrapper; gating and data live in RiteGate (core hook useTaste) and in
// DuelConsole (serves/resolves the duel via core hooks). No direct core import here by design.
// The blind duel (feature C2), behind the shared Rite gate (sign in -> cold start -> the duel).
export function DuelPage() {
  return (
    <RiteGate>
      <DuelConsole />
    </RiteGate>
  );
}
