import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../core/api/client';
import { useWeekly } from '../../core/hooks/useWeekly';
import { useAuth } from '../auth/AuthProvider';
import { AuthPanel } from '../auth/AuthPanel';
import { PushSubscribe } from '../push/PushSubscribe';
import { WeeklyItem } from '../weekly/WeeklyItem';

// The Weekly Rite (feature B17): the seven blind bands of the current ISO week, plus Web Push
// subscription so they arrive without opening the app. Auth-gated; a user with no taste is sent to
// cold start (the backend answers 409). Wired to the useWeekly core hook — not an aesthetic shell.
export function WeeklyPage() {
  const { t } = useTranslation();
  const { status, isAuthenticated } = useAuth();
  const weekly = useWeekly(isAuthenticated);

  if (status === 'unknown') {
    return <p className="font-mono text-sm text-muted">{t('rite.checking')}</p>;
  }

  if (!isAuthenticated) {
    return <AuthPanel />;
  }

  // 409 → the user has no taste yet: point them at cold start rather than showing a broken week.
  const needsTaste = weekly.isError && weekly.error instanceof ApiError && weekly.error.status === 409;

  return (
    <section>
      <h1 className="font-display text-4xl text-strong">{t('weekly.heading')}</h1>
      <p className="mt-2 max-w-prose font-mono text-xs text-muted">{t('weekly.intro')}</p>

      <div className="mt-6">
        <PushSubscribe />
      </div>

      {needsTaste ? (
        <div className="mt-6 border border-line p-6">
          <p className="font-display text-xl text-strong">{t('weekly.noTasteTitle')}</p>
          <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('weekly.noTasteBody')}</p>
          <Link
            to="/rite"
            className="mt-4 inline-block border border-accent px-4 py-2 font-mono text-xs uppercase text-accent no-underline hover:bg-accent hover:text-bg"
          >
            {t('weekly.toColdStart')}
          </Link>
        </div>
      ) : null}

      {weekly.isLoading ? (
        <p className="mt-6 font-mono text-sm text-muted">{t('weekly.loading')}</p>
      ) : null}

      {weekly.isError && !needsTaste ? (
        <p className="mt-6 font-mono text-sm text-danger">{t('weekly.error')}</p>
      ) : null}

      {weekly.data !== undefined ? (
        <div className="mt-8">
          <p className="font-mono text-xs uppercase text-muted">
            {t('weekly.weekLabel', { week: weekly.data.weekKey })}
          </p>
          {weekly.data.items.length === 0 ? (
            <div className="mt-4 border border-line p-6">
              <p className="font-display text-xl text-strong">{t('weekly.emptyTitle')}</p>
              <p className="mt-2 max-w-prose font-body text-sm text-muted">{t('weekly.emptyBody')}</p>
            </div>
          ) : (
            <ul className="mt-4 grid gap-4 sm:grid-cols-2">
              {weekly.data.items.map((item, index) => (
                <WeeklyItem key={item.token} item={item} index={index} />
              ))}
            </ul>
          )}
        </div>
      ) : null}
    </section>
  );
}
