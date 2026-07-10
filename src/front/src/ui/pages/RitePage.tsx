import { useTranslation } from 'react-i18next';
import { useTaste } from '../../core/hooks/useTaste';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { ColdStart } from '../rite/ColdStart';
import { RiteConsole } from '../rite/RiteConsole';

// The Rite entry point. Three gates, in order (task 1/2/3):
//   anonymous            -> sign in / register
//   signed in, no taste  -> cold start (choose bands / import Last.fm)
//   signed in, has taste -> the rite console
export function RitePage() {
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

  return <RiteConsole />;
}
