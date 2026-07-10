import { useState } from 'react';
import { Link, Outlet } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { applyTheme, readTheme, type Theme } from '../platform/theme.web';
import { persistLanguage } from '../i18n';

export function Layout() {
  const { t, i18n } = useTranslation();
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
          <div className="flex items-center gap-3">
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
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-3xl px-5 py-8">
        <Outlet />
      </main>
    </div>
  );
}
