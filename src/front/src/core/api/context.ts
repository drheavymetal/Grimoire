import { createContext, useContext } from 'react';
import type { GrimoireClient } from './client';

// The concrete client (with its base URL) is injected by the platform layer, so core
// hooks depend on the interface, not on any environment lookup.
const GrimoireClientContext = createContext<GrimoireClient | null>(null);

export const GrimoireClientProvider = GrimoireClientContext.Provider;

export function useGrimoireClient(): GrimoireClient {
  const client = useContext(GrimoireClientContext);

  if (client === null) {
    throw new Error('useGrimoireClient must be used within a GrimoireClientProvider.');
  }

  return client;
}
