import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../core/api/client';
import { useAuth } from './AuthProvider';
import { Mark } from '../Logo';

// Minimal sign-in / register gate for The Rite (task 1). The Rite endpoints require a JWT,
// so an anonymous visitor sees this first. Copy is directed, not a bare form.
export function AuthPanel() {
  const { t } = useTranslation();
  const { login, register } = useAuth();
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [errorKey, setErrorKey] = useState<string | null>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setErrorKey(null);

    try {
      if (mode === 'login') {
        await login(email, password);
      } else {
        await register(email, password);
      }
    } catch (error) {
      setErrorKey(messageKeyFor(error, mode));
      setBusy(false);
    }
  }

  return (
    <section className="mx-auto max-w-sm">
      {/* The threshold to the rite: the mark, then its own eyebrow. The heading stays 'The Rite'
          (the gate the visitor is trying to enter) — the e2e suite finds it by that name. */}
      <Mark size={40} className="text-strong" />
      <p className="mt-4 font-mono text-[0.7rem] uppercase tracking-[0.28em] text-accent">{t('auth.eyebrow')}</p>
      <h1 className="mt-2 font-display text-4xl leading-[0.95] text-strong sm:text-5xl">{t('rite.heading')}</h1>
      <p className="mt-2 font-mono text-xs text-muted">
        {mode === 'login' ? t('auth.loginHint') : t('auth.registerHint')}
      </p>

      <form onSubmit={submit} className="mt-6 space-y-4">
        <label className="block">
          <span className="font-mono text-xs uppercase text-muted">{t('auth.email')}</span>
          <input
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="email"
            required
            className="mt-1 w-full border border-line bg-panel px-4 py-3 font-body text-strong outline-none focus:border-accent"
          />
        </label>

        <label className="block">
          <span className="font-mono text-xs uppercase text-muted">{t('auth.password')}</span>
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
            required
            minLength={8}
            className="mt-1 w-full border border-line bg-panel px-4 py-3 font-body text-strong outline-none focus:border-accent"
          />
        </label>

        {mode === 'register' ? (
          <p className="font-mono text-xs text-muted">{t('auth.passwordRule')}</p>
        ) : null}

        {errorKey !== null ? (
          <p className="font-mono text-sm text-danger">{t(errorKey)}</p>
        ) : null}

        <button
          type="submit"
          disabled={busy}
          className="w-full border border-accent bg-accent px-4 py-3 font-display text-lg text-bg disabled:opacity-50"
        >
          {busy
            ? t('auth.working')
            : mode === 'login'
              ? t('auth.signIn')
              : t('auth.createAccount')}
        </button>
      </form>

      <button
        type="button"
        onClick={() => {
          setMode(mode === 'login' ? 'register' : 'login');
          setErrorKey(null);
        }}
        className="mt-4 font-mono text-xs uppercase text-muted hover:text-accent"
      >
        {mode === 'login' ? t('auth.toRegister') : t('auth.toLogin')}
      </button>
    </section>
  );
}

function messageKeyFor(error: unknown, mode: 'login' | 'register'): string {
  if (error instanceof ApiError) {
    if (error.status === 401) {
      return 'auth.errorInvalid';
    }
    if (error.status === 409) {
      return 'auth.errorExists';
    }
    if (error.status === 400) {
      return mode === 'register' ? 'auth.errorWeak' : 'auth.errorInvalid';
    }
  }

  return 'auth.errorGeneric';
}
