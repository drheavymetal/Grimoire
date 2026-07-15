import { useEffect, useState } from 'react';
import { Link, Outlet } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { applyTheme, readTheme, type Theme } from '../platform/theme.web';
import { persistLanguage } from '../i18n';
import { BrandLockup } from './Logo';
import { Sidebar } from './Sidebar';

// The shell (DESIGN, D14/D27). The routes no longer sit in a bar over the void — they run down a
// LEFT SIDEBAR, the spine of the grimoire (see Sidebar). On desktop the spine is a sticky rail and
// the reading column sits to its right; below md the rail folds away behind a hamburger and opens as
// a drawer over a scrim. Theme and language live in the sidebar's user area, but their state is held
// here so the rail and the drawer (both mounted at once) never disagree.
export function Layout() {
  const { t, i18n } = useTranslation();
  const [theme, setTheme] = useState<Theme>(() => readTheme());
  const [drawerOpen, setDrawerOpen] = useState(false);

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

  // Escape closes the drawer (accessibility floor, DESIGN §7). Only listens while it is open.
  useEffect(() => {
    if (!drawerOpen) {
      return;
    }

    function onKey(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setDrawerOpen(false);
      }
    }

    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('keydown', onKey);
    };
  }, [drawerOpen]);

  const sidebarProps = {
    theme,
    onToggleTheme: toggleTheme,
    language: i18n.language,
    onToggleLanguage: toggleLanguage,
  };

  return (
    <div className="flex min-h-full">
      {/* Desktop rail — the spine, sticky and full height so the main column scrolls beside it. */}
      <aside className="sticky top-0 hidden h-screen w-60 shrink-0 border-r border-line md:block">
        <Sidebar {...sidebarProps} />
      </aside>

      {/* Mobile drawer — the same spine, folded away behind a scrim until summoned. Kept mounted so
          it can slide; `inert` and pointer-events keep it out of reach when closed. Reduced motion
          drops the slide (motion-reduce:transition-none). */}
      <div
        className={`fixed inset-0 z-40 md:hidden ${drawerOpen ? '' : 'pointer-events-none'}`}
        aria-hidden={!drawerOpen}
      >
        <button
          type="button"
          aria-label={t('nav.closeMenu')}
          tabIndex={drawerOpen ? 0 : -1}
          onClick={() => setDrawerOpen(false)}
          className={`absolute inset-0 h-full w-full cursor-default bg-black/60 transition-opacity duration-200 motion-reduce:transition-none ${
            drawerOpen ? 'opacity-100' : 'opacity-0'
          }`}
        />
        <aside
          inert={!drawerOpen}
          className={`absolute left-0 top-0 h-full w-72 max-w-[85%] border-r border-line shadow-xl transition-transform duration-200 motion-reduce:transition-none ${
            drawerOpen ? 'translate-x-0' : '-translate-x-full'
          }`}
        >
          <Sidebar {...sidebarProps} onNavigate={() => setDrawerOpen(false)} />
        </aside>
      </div>

      {/* The reading column, offset from the rail on desktop, full width on mobile. */}
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Mobile identity bar: the hamburger and the brand, only below md. */}
        <div className="sticky top-0 z-20 flex items-center gap-3 border-b border-line bg-bg/85 px-4 py-3 backdrop-blur-sm md:hidden">
          <button
            type="button"
            aria-label={t('nav.menu')}
            aria-expanded={drawerOpen}
            onClick={() => setDrawerOpen(true)}
            className="cursor-pointer text-muted hover:text-strong"
          >
            <svg width="20" height="20" viewBox="0 0 20 20" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.6">
              <line x1="3" y1="6" x2="17" y2="6" />
              <line x1="3" y1="10" x2="17" y2="10" />
              <line x1="3" y1="14" x2="17" y2="14" />
            </svg>
          </button>
          <Link to="/" aria-label={t('app.title')} className="no-underline">
            <BrandLockup />
          </Link>
        </div>

        <main className="mx-auto w-full max-w-3xl flex-1 px-5 py-10">
          <Outlet />
        </main>

        <footer className="mt-8 border-t border-line">
          <div className="px-5 py-8 text-center font-mono text-[0.68rem] uppercase tracking-[0.14em] text-faint">
            {t('app.title')} — {t('app.tagline')}
          </div>
        </footer>
      </div>
    </div>
  );
}
