import { createRootRoute, createRoute, createRouter } from '@tanstack/react-router';
import { Layout } from './Layout';
import { SearchPage } from './pages/SearchPage';
import { ArtistPage } from './pages/ArtistPage';

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

function ArtistRouteComponent() {
  const { artistId } = artistRoute.useParams();
  return <ArtistPage artistId={artistId} />;
}

const routeTree = rootRoute.addChildren([searchRoute, artistRoute]);

export const router = createRouter({ routeTree });

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
