import { useState } from 'react';
import { Link, Outlet } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { applyTheme, readTheme, type Theme } from '../platform/theme.web';
import { persistLanguage } from '../i18n';
import { useAuth } from './auth/AuthProvider';

export function Layout() {
  const { t, i18n } = useTranslation();
  const { isAuthenticated, logout } = useAuth();
  const [theme, setTheme] = useState<Theme>(() => readTheme());

  function toggleTheme() {
    const next: Theme = theme === 'dark' ? 'light' : 'dark';
    applyTheme(next);
    setTheme(next);
  }

  function toggleLanguage() {
    const next = i18n.language === 'es' ? 'en' : 'es';
    void i18n.changeLanguage(next);
    persistLanguage(next);
  }

  return (
    <div className="min-h-full">
      <header className="border-b border-line">
        <div className="mx-auto flex max-w-3xl items-baseline justify-between px-5 py-4">
          <Link to="/" className="font-display text-2xl tracking-wide text-strong no-underline">
            {t('app.title')}
          </Link>
          <nav className="flex flex-wrap items-center justify-end gap-x-4 gap-y-1">
            <Link to="/rite" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
              {t('nav.rite')}
            </Link>
            <Link to="/weekly" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
              {t('nav.weekly')}
            </Link>
            <Link to="/scenes" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
              {t('nav.scenes')}
            </Link>
            <Link to="/labels" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
              {t('nav.labels')}
            </Link>
            <Link to="/lineage" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
              {t('nav.lineage')}
            </Link>
            <Link to="/explore" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
              {t('nav.explore')}
            </Link>
            <Link to="/atlas" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
              {t('nav.atlas')}
            </Link>
            <Link
              to="/grimoire"
              className="font-mono text-xs uppercase text-muted no-underline hover:text-accent"
            >
              {t('nav.grimoire')}
            </Link>
            <Link to="/mirror" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
              {t('nav.mirror')}
            </Link>
            <Link to="/memoriam" className="font-mono text-xs uppercase text-muted no-underline hover:text-accent">
              {t('nav.memoriam')}
            </Link>
            {isAuthenticated ? (
              <button
                type="button"
                onClick={logout}
                className="font-mono text-xs uppercase text-muted hover:text-accent"
              >
                {t('nav.logout')}
              </button>
            ) : null}
            <button
              type="button"
              onClick={toggleLanguage}
              className="font-mono text-xs uppercase text-muted hover:text-accent"
            >
              {i18n.language === 'es' ? 'EN' : 'ES'}
            </button>
            <button
              type="button"
              onClick={toggleTheme}
              aria-label={t('nav.toggleTheme')}
              className="font-mono text-xs uppercase text-muted hover:text-accent"
            >
              {theme === 'dark' ? '☾' : '☀'}
            </button>
          </nav>
        </div>
      </header>
      <main className="mx-auto max-w-3xl px-5 py-8">
        <Outlet />
      </main>
    </div>
  );
}
