import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useGrimoireClient } from '../../core/api/context';
import { ApiError } from '../../core/api/client';
import { useNotifyWeekly } from '../../core/hooks/useWeekly';
import {
  existingPushSubscription,
  isPushSupported,
  pushPermission,
  subscribePush,
  unsubscribePush,
} from '../../platform/push.web';

type PushState =
  | 'checking'
  | 'unsupported'
  | 'unconfigured'
  | 'subscribed'
  | 'unsubscribed'
  | 'denied'
  | 'working';

// Web Push subscription control (feature B17). Detects support, reflects the current subscription,
// and lets the user subscribe/unsubscribe. The browser plumbing lives in platform/push.web.ts; this
// component only orchestrates it and talks to the API. A "send me a test push" button triggers the
// weekly notification (the manual/test trigger), reporting the honest per-subscription tally.
export function PushSubscribe() {
  const { t } = useTranslation();
  const client = useGrimoireClient();
  const notify = useNotifyWeekly();

  const [state, setState] = useState<PushState>('checking');
  const [notifyMsg, setNotifyMsg] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function detect() {
      if (!isPushSupported()) {
        if (!cancelled) {
          setState('unsupported');
        }
        return;
      }

      if (pushPermission() === 'denied') {
        if (!cancelled) {
          setState('denied');
        }
        return;
      }

      try {
        const existing = await existingPushSubscription();
        if (!cancelled) {
          setState(existing ? 'subscribed' : 'unsubscribed');
        }
      } catch {
        if (!cancelled) {
          setState('unsubscribed');
        }
      }
    }

    void detect();

    return () => {
      cancelled = true;
    };
  }, []);

  async function subscribe() {
    setState('working');
    setNotifyMsg(null);
    try {
      const key = await client.vapidPublicKey();
      const subscription = await subscribePush(key);
      if (subscription === null) {
        setState('denied');
        return;
      }
      await client.subscribePush(subscription);
      setState('subscribed');
    } catch (error) {
      if (error instanceof ApiError && error.status === 503) {
        setState('unconfigured');
        return;
      }
      setState('unsubscribed');
    }
  }

  async function unsubscribe() {
    setState('working');
    setNotifyMsg(null);
    try {
      const removed = await unsubscribePush();
      if (removed !== null) {
        await client.unsubscribePush(removed);
      }
    } finally {
      setState('unsubscribed');
    }
  }

  function sendTest() {
    setNotifyMsg(null);
    notify.mutate(undefined, {
      onSuccess: (result) => {
        setNotifyMsg(t('weekly.notifySent', { sent: result.sent, pruned: result.pruned, failed: result.failed }));
      },
      onError: (error) => {
        setNotifyMsg(
          error instanceof ApiError && error.status === 503
            ? t('weekly.notifyUnconfigured')
            : t('weekly.notifyError'),
        );
      },
    });
  }

  return (
    <div className="border border-line bg-panel p-5">
      <h2 className="font-display text-xl text-strong">{t('weekly.pushTitle')}</h2>
      <p className="mt-1 max-w-prose font-body text-sm text-muted">{t('weekly.pushHint')}</p>

      <div className="mt-4">
        {state === 'checking' ? (
          <p className="font-mono text-xs text-muted">{t('weekly.pushChecking')}</p>
        ) : null}

        {state === 'unsupported' ? (
          <p className="font-mono text-xs text-muted">{t('weekly.pushUnsupported')}</p>
        ) : null}

        {state === 'unconfigured' ? (
          <p className="font-mono text-xs text-muted">{t('weekly.pushUnconfigured')}</p>
        ) : null}

        {state === 'denied' ? (
          <p className="font-mono text-xs text-danger">{t('weekly.pushDenied')}</p>
        ) : null}

        {state === 'unsubscribed' ? (
          <button
            type="button"
            onClick={subscribe}
            className="border border-accent px-4 py-2 font-mono text-xs uppercase text-accent hover:bg-accent hover:text-bg"
          >
            {t('weekly.pushSubscribe')}
          </button>
        ) : null}

        {state === 'working' ? (
          <p className="font-mono text-xs text-muted">{t('weekly.pushWorking')}</p>
        ) : null}

        {state === 'subscribed' ? (
          <div className="flex flex-wrap items-center gap-3">
            <span className="font-mono text-xs uppercase text-accent">{t('weekly.pushSubscribed')}</span>
            <button
              type="button"
              onClick={unsubscribe}
              className="border border-line px-3 py-1.5 font-mono text-xs uppercase text-muted hover:border-strong hover:text-strong"
            >
              {t('weekly.pushUnsubscribe')}
            </button>
            <button
              type="button"
              onClick={sendTest}
              disabled={notify.isPending}
              className="border border-line px-3 py-1.5 font-mono text-xs uppercase text-muted hover:border-strong hover:text-strong disabled:opacity-40"
            >
              {t('weekly.pushTest')}
            </button>
          </div>
        ) : null}

        {notifyMsg !== null ? <p className="mt-3 font-mono text-xs text-muted">{notifyMsg}</p> : null}
      </div>
    </div>
  );
}
