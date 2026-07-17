import { createRootRoute, createRoute, createRouter } from '@tanstack/react-router';
import type { RiteScope, ThemeKind } from '../core/domain/types';
import { Layout } from './Layout';
import { SearchPage } from './pages/SearchPage';
import { ArtistPage } from './pages/ArtistPage';
import { RitePage } from './pages/RitePage';
import { BrowsePage } from './pages/BrowsePage';
import { DuelPage } from './pages/DuelPage';
import { DecadePage } from './pages/DecadePage';
import { GrimoirePage } from './pages/GrimoirePage';
import { LineagePage } from './pages/LineagePage';
import { AtlasPage } from './pages/AtlasPage';
import { ScenesPage } from './pages/ScenesPage';
import { LabelsPage } from './pages/LabelsPage';
import { LabelPage } from './pages/LabelPage';
import { ExplorePage } from './pages/ExplorePage';
import { GiftPage } from './pages/GiftPage';
import { WeeklyPage } from './pages/WeeklyPage';
import { MirrorPage } from './pages/MirrorPage';
import { MemoriamPage } from './pages/MemoriamPage';
import { ProfilePage } from './pages/ProfilePage';
import { FriendsPage } from './pages/FriendsPage';
import { GamesPage } from './pages/GamesPage';
import { NotificationsPage } from './pages/NotificationsPage';

const rootRoute = createRootRoute({
  component: Layout,
});

const searchRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: SearchPage,
});

const artistRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/artist/$artistId',
  component: ArtistRouteComponent,
});

const riteRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/rite',
  // A ficha chip can scope a blind rite by passing a tag or theme needle in the URL. The rite stays
  // blind — the scope only narrows the pool (see RiteConsole). Unknown values are dropped.
  validateSearch: (search: Record<string, unknown>): RiteScope => {
    const themeKind =
      search.themeKind === 'lyrical' || search.themeKind === 'mined'
        ? (search.themeKind as ThemeKind)
        : undefined;
    return {
      genreNeedle: typeof search.genreNeedle === 'string' ? search.genreNeedle : undefined,
      themeNeedle: typeof search.themeNeedle === 'string' ? search.themeNeedle : undefined,
      themeKind,
    };
  },
  component: RiteRouteComponent,
});

const duelRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/duel',
  component: DuelPage,
});

const decadeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/decade',
  component: DecadePage,
});

const grimoireRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/grimoire',
  component: GrimoirePage,
});

const lineageRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/lineage',
  component: LineagePage,
});

const atlasRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/atlas',
  component: AtlasPage,
});

const scenesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/scenes',
  component: ScenesPage,
});

const labelsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/labels',
  component: LabelsPage,
});

const labelRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/label/$labelId',
  component: LabelRouteComponent,
});

const exploreRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/explore',
  component: ExplorePage,
});

const giftRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/gift/$token',
  component: GiftRouteComponent,
});

const weeklyRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/weekly',
  component: WeeklyPage,
});

const mirrorRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/mirror',
  component: MirrorPage,
});

const memoriamRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/memoriam',
  component: MemoriamPage,
});

const profileRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/profile',
  component: ProfilePage,
});

const friendsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/friends',
  component: FriendsPage,
});

const gamesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/games',
  component: GamesPage,
});

const notificationsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/notifications',
  component: NotificationsPage,
});

// The NAMED "see all" door out of a ficha chip: every band under a tag, or under a theme.
const browseTagRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/browse/tag/$needle',
  component: BrowseTagRouteComponent,
});

const browseThemeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/browse/theme/$key',
  validateSearch: (search: Record<string, unknown>): { kind: ThemeKind } => ({
    kind: search.kind === 'mined' ? 'mined' : 'lyrical',
  }),
  component: BrowseThemeRouteComponent,
});

function ArtistRouteComponent() {
  const { artistId } = artistRoute.useParams();
  return <ArtistPage artistId={artistId} />;
}

function LabelRouteComponent() {
  const { labelId } = labelRoute.useParams();
  return <LabelPage labelId={labelId} />;
}

function GiftRouteComponent() {
  const { token } = giftRoute.useParams();
  return <GiftPage token={token} />;
}

function RiteRouteComponent() {
  const scope = riteRoute.useSearch();
  return <RitePage scope={scope} />;
}

function BrowseTagRouteComponent() {
  const { needle } = browseTagRoute.useParams();
  return <BrowsePage mode={{ kind: 'tag', needle }} />;
}

function BrowseThemeRouteComponent() {
  const { key } = browseThemeRoute.useParams();
  const { kind } = browseThemeRoute.useSearch();
  return <BrowsePage mode={{ kind: 'theme', themeKey: key, themeKind: kind }} />;
}

const routeTree = rootRoute.addChildren([
  searchRoute,
  artistRoute,
  riteRoute,
  duelRoute,
  decadeRoute,
  grimoireRoute,
  lineageRoute,
  atlasRoute,
  scenesRoute,
  labelsRoute,
  labelRoute,
  exploreRoute,
  giftRoute,
  weeklyRoute,
  mirrorRoute,
  memoriamRoute,
  profileRoute,
  friendsRoute,
  gamesRoute,
  notificationsRoute,
  browseTagRoute,
  browseThemeRoute,
]);

export const router = createRouter({ routeTree });

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
