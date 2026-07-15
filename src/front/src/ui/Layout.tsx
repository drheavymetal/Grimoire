import { useState } from 'react';
import { Link, Outlet, type LinkProps } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { applyTheme, readTheme, type Theme } from '../platform/theme.web';
import { persistLanguage } from '../i18n';
import { useAuth } from './auth/AuthProvider';
import { BrandLockup } from './Logo';

// The shell (DESIGN, D14/D27). A sticky bar over the void: the brand lockup on the left (mark +
// wordmark, the sulphur I the time axis), the routes as Courier small-caps with a sulphur underline
// marking where you are, and the utilities — sign out, language, artifact toggle — on the right.
// Nothing generic: the nav labels are the vernacular of a J-card, uppercase and tracked.
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
    <div className="flex min-h-full flex-col">
      <header className="sticky top-0 z-20 border-b border-line bg-bg/85 backdrop-blur-sm">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center gap-x-6 gap-y-2 px-5 py-3">
          {/* Brand sits OUTSIDE the nav landmark: the mark is the way home, not a route. */}
          <Link to="/" aria-label={t('app.title')} className="no-underline">
            <BrandLockup />
          </Link>

          <nav className="flex flex-1 flex-wrap items-center justify-end gap-x-4 gap-y-1">
            <NavLink to="/rite" label={t('nav.rite')} />
            <NavLink to="/duel" label={t('nav.duel')} />
            <NavLink to="/decade" label={t('nav.decade')} />
            <NavLink to="/weekly" label={t('nav.weekly')} />
            <NavLink to="/scenes" label={t('nav.scenes')} />
            <NavLink to="/labels" label={t('nav.labels')} />
            <NavLink to="/lineage" label={t('nav.lineage')} />
            <NavLink to="/explore" label={t('nav.explore')} />
            <NavLink to="/atlas" label={t('nav.atlas')} />
            <NavLink to="/grimoire" label={t('nav.grimoire')} />
            <NavLink to="/mirror" label={t('nav.mirror')} />
            <NavLink to="/memoriam" label={t('nav.memoriam')} />

            <span aria-hidden="true" className="mx-1 h-3 w-px bg-line" />

            {isAuthenticated ? <NavLink to="/profile" label={t('nav.profile')} /> : null}
            {isAuthenticated ? (
              <button
                type="button"
                onClick={logout}
                className="cursor-pointer font-mono text-xs uppercase tracking-[0.18em] text-muted hover:text-strong"
              >
                {t('nav.logout')}
              </button>
            ) : null}
            <button
              type="button"
              onClick={toggleLanguage}
              className="cursor-pointer font-mono text-xs uppercase tracking-[0.18em] text-muted hover:text-strong"
            >
              {i18n.language === 'es' ? 'EN' : 'ES'}
            </button>
            <button
              type="button"
              onClick={toggleTheme}
              aria-label={t('nav.toggleTheme')}
              className="cursor-pointer font-mono text-sm text-muted hover:text-accent"
            >
              {theme === 'dark' ? '☾' : '☀'}
            </button>
          </nav>
        </div>
      </header>

      <main className="mx-auto w-full max-w-3xl flex-1 px-5 py-10">
        <Outlet />
      </main>

      <footer className="mt-8 border-t border-line">
        <div className="mx-auto max-w-5xl px-5 py-8 text-center font-mono text-[0.68rem] uppercase tracking-[0.14em] text-faint">
          {t('app.title')} — {t('app.tagline')}
        </div>
      </footer>
    </div>
  );
}

// One nav entry: Courier small-caps, muted until hovered, with a sulphur underline when it is the
// active route (DESIGN — sulphur marks where you are). The underline is the only accent in the bar.
function NavLink({ to, label }: { to: LinkProps['to']; label: string }) {
  return (
    <Link
      to={to}
      className="relative py-0.5 font-mono text-xs uppercase tracking-[0.18em] text-muted no-underline hover:text-strong"
      activeProps={{
        className:
          'relative py-0.5 font-mono text-xs uppercase tracking-[0.18em] text-strong no-underline after:absolute after:-bottom-1 after:left-0 after:right-0 after:h-px after:bg-accent',
      }}
    >
      {label}
    </Link>
  );
}
