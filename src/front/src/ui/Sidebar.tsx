import { Link, type LinkProps } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { Theme } from '../platform/theme.web';
import { useProfile } from '../core/hooks/useProfile';
import { useAuth } from './auth/AuthProvider';
import { BrandLockup } from './Logo';

// The spine of the grimoire (DESIGN §3/§5, D14/D27). Where the old top bar spread the routes in a
// row, the sidebar stacks them down the left edge like the spine of a book or a cassette J-card:
// the brand at the top (the mark is the way home), the routes grouped by what the app DOES, each
// group under a Courier small-caps eyebrow, and the listener's own identity anchored at the bottom.
//
// The one signature is the active-route marker: a VERTICAL sulphur bar on the left edge of the
// active item — an echo of the mark's sulphur "I", the time axis — replacing the old underline. No
// second accent, no new radius. This same column renders in both the desktop rail and the mobile
// drawer; `onNavigate` lets the drawer close itself when a route is chosen.
interface SidebarProps {
  theme: Theme;
  onToggleTheme(): void;
  language: string;
  onToggleLanguage(): void;
  onNavigate?: () => void;
}

export function Sidebar({ theme, onToggleTheme, language, onToggleLanguage, onNavigate }: SidebarProps) {
  const { t } = useTranslation();
  const { isAuthenticated, logout } = useAuth();
  const profileQuery = useProfile(isAuthenticated);

  return (
    <div className="flex h-full flex-col bg-bg">
      {/* Brand sits at the top and OUTSIDE the nav landmark: the mark is the way home, not a route. */}
      <div className="shrink-0 border-b border-line px-4 py-4">
        <Link to="/" aria-label={t('app.title')} onClick={onNavigate} className="no-underline">
          <BrandLockup />
        </Link>
      </div>

      {/* The routes, grouped by what the app does. The grouping is structural — it encodes the shape
          of the product, not just a list. The nav scrolls on its own so the user area stays pinned. */}
      <nav className="flex-1 space-y-5 overflow-y-auto py-5">
        <NavGroup label={t('nav.groupRite')}>
          <NavItem to="/rite" label={t('nav.rite')} onNavigate={onNavigate} />
          <NavItem to="/duel" label={t('nav.duel')} onNavigate={onNavigate} />
          <NavItem to="/weekly" label={t('nav.weekly')} onNavigate={onNavigate} />
        </NavGroup>

        <NavGroup label={t('nav.groupExplore')}>
          {/* Home / Search is the one item that must match EXACTLY — every path starts with "/". */}
          <NavItem to="/" label={t('nav.search')} exact onNavigate={onNavigate} />
          <NavItem to="/scenes" label={t('nav.scenes')} onNavigate={onNavigate} />
          <NavItem to="/labels" label={t('nav.labels')} onNavigate={onNavigate} />
          <NavItem to="/lineage" label={t('nav.lineage')} onNavigate={onNavigate} />
          <NavItem to="/atlas" label={t('nav.atlas')} onNavigate={onNavigate} />
          <NavItem to="/explore" label={t('nav.explore')} onNavigate={onNavigate} />
          <NavItem to="/decade" label={t('nav.decade')} onNavigate={onNavigate} />
          <NavItem to="/memoriam" label={t('nav.memoriam')} onNavigate={onNavigate} />
        </NavGroup>

        <NavGroup label={t('nav.groupYours')}>
          <NavItem to="/grimoire" label={t('nav.grimoire')} onNavigate={onNavigate} />
          <NavItem to="/mirror" label={t('nav.mirror')} onNavigate={onNavigate} />
          <NavItem to="/friends" label={t('nav.friends')} onNavigate={onNavigate} />
        </NavGroup>
      </nav>

      {/* The user area: the profile's proper home. Anchored at the bottom of the spine. */}
      <div className="shrink-0 border-t border-line p-3">
        {isAuthenticated ? (
          <UserIdentity
            handle={profileQuery.data?.handle ?? null}
            depthScore={profileQuery.data?.depthScore}
            onNavigate={onNavigate}
          />
        ) : (
          <Link
            to="/rite"
            onClick={onNavigate}
            className="block rounded-sm px-2 py-2 font-mono text-xs uppercase tracking-[0.18em] text-muted no-underline hover:bg-panel hover:text-strong"
          >
            {t('nav.signIn')} →
          </Link>
        )}

        <div className="mt-2 flex items-center gap-2 px-2">
          <button
            type="button"
            onClick={onToggleLanguage}
            className="cursor-pointer font-mono text-xs uppercase tracking-[0.18em] text-muted hover:text-strong"
          >
            {language === 'es' ? 'EN' : 'ES'}
          </button>
          <button
            type="button"
            onClick={onToggleTheme}
            aria-label={t('nav.toggleTheme')}
            className="cursor-pointer font-mono text-sm text-muted hover:text-accent"
          >
            {theme === 'dark' ? '☾' : '☀'}
          </button>
          {isAuthenticated ? (
            <button
              type="button"
              onClick={() => {
                onNavigate?.();
                logout();
              }}
              className="ml-auto cursor-pointer font-mono text-xs uppercase tracking-[0.18em] text-muted hover:text-strong"
            >
              {t('nav.logout')}
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

// The signed-in listener's identity block: their handle (or a muted prompt to set one), and their
// Depth Score as the one identity stat — the number struck in sulphur. The whole block is the door
// to the full profile.
function UserIdentity({
  handle,
  depthScore,
  onNavigate,
}: {
  handle: string | null;
  depthScore: number | undefined;
  onNavigate?: () => void;
}) {
  const { t } = useTranslation();

  return (
    <Link
      to="/profile"
      onClick={onNavigate}
      className="block rounded-sm px-2 py-2 no-underline hover:bg-panel"
    >
      {handle !== null ? (
        <span className="block truncate font-mono text-xs uppercase tracking-[0.14em] text-strong">
          @{handle}
        </span>
      ) : (
        <span className="block truncate font-mono text-xs uppercase tracking-[0.14em] text-muted">
          {t('nav.setHandle')}
        </span>
      )}
      <span className="mt-1 block font-mono text-[0.62rem] uppercase tracking-[0.2em] text-faint">
        {t('profile.depthScore')} · <span className="text-accent">{depthScore ?? 0}</span>
      </span>
    </Link>
  );
}

// A group of routes under a Courier small-caps eyebrow (matching the page-header kicker, DESIGN §3).
function NavGroup({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="px-4 pb-1.5 font-mono text-[0.62rem] uppercase tracking-[0.28em] text-faint">
        {label}
      </p>
      <ul className="space-y-0.5">{children}</ul>
    </div>
  );
}

// One route: Courier small-caps, muted until hovered. When it is the active route it turns strong
// and grows a VERTICAL sulphur bar on its left edge — the spine's tick, the app's one accent. `exact`
// is for the home route, which every path would otherwise match as a prefix.
function NavItem({
  to,
  label,
  exact = false,
  onNavigate,
}: {
  to: LinkProps['to'];
  label: string;
  exact?: boolean;
  onNavigate?: () => void;
}) {
  const base =
    'relative block py-1.5 pl-4 pr-3 font-mono text-xs uppercase tracking-[0.18em] no-underline';

  return (
    <li>
      <Link
        to={to}
        onClick={onNavigate}
        activeOptions={exact ? { exact: true } : undefined}
        className={`${base} text-muted hover:text-strong`}
        activeProps={{
          'aria-current': 'page',
          className: `${base} text-strong before:absolute before:top-1 before:bottom-1 before:left-0 before:w-[3px] before:bg-accent before:content-['']`,
        }}
      >
        {label}
      </Link>
    </li>
  );
}
