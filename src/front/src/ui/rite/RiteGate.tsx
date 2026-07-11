import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useTaste } from '../../core/hooks/useTaste';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { ColdStart } from './ColdStart';

// The three-gate guard shared by every Rite surface (the console, the duel, the decade game):
//   anonymous            -> sign in / register
//   signed in, no taste  -> cold start (choose bands / import Last.fm)
//   signed in, has taste -> the surface itself (children)
// Factored out of RitePage so the duel and decade pages reuse the exact same gating (invariant 6:
// no DOM here beyond the shared UI components, and none of the discovery logic lives in core).
export function RiteGate({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const { status, isAuthenticated } = useAuth();
  const taste = useTaste(isAuthenticated);

  if (status === 'unknown') {
    return <p className="font-mono text-sm text-muted">{t('rite.checking')}</p>;
  }

  if (!isAuthenticated) {
    return <AuthPanel />;
  }

  if (taste.isLoading) {
    return <p className="font-mono text-sm text-muted">{t('rite.checking')}</p>;
  }

  if (taste.isError) {
    return <p className="font-mono text-sm text-danger">{t('rite.tasteError')}</p>;
  }

  if (taste.data !== undefined && !taste.data.hasTaste) {
    return <ColdStart />;
  }

  return <>{children}</>;
}
