import { createRootRoute, createRoute, createRouter } from '@tanstack/react-router';
import { Layout } from './Layout';
import { SearchPage } from './pages/SearchPage';
import { ArtistPage } from './pages/ArtistPage';
import { RitePage } from './pages/RitePage';
import { GrimoirePage } from './pages/GrimoirePage';
import { LineagePage } from './pages/LineagePage';
import { AtlasPage } from './pages/AtlasPage';
import { ScenesPage } from './pages/ScenesPage';
import { LabelsPage } from './pages/LabelsPage';
import { LabelPage } from './pages/LabelPage';
import { ExplorePage } from './pages/ExplorePage';
import { GiftPage } from './pages/GiftPage';

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
  component: RitePage,
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

const routeTree = rootRoute.addChildren([
  searchRoute,
  artistRoute,
  riteRoute,
  grimoireRoute,
  lineageRoute,
  atlasRoute,
  scenesRoute,
  labelsRoute,
  labelRoute,
  exploreRoute,
  giftRoute,
]);

export const router = createRouter({ routeTree });

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
