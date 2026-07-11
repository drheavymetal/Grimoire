import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type {
  Diaspora,
  Graph,
  MemberBands,
  MissingLink,
  PathResult,
  RabbitHole,
} from '../domain/types';

// The lineage hooks (movement IV). Each wraps one endpoint of the lineage API through the
// injected client, so nothing here touches the network or the DOM directly — the same hook
// runs under React Native (D12). Queries are disabled until their ids are present, so a picker
// that has not chosen yet fires nothing.

// B16 — the ego graph of an artist.
export function useBloodline(id: string, hops: number) {
  const client = useGrimoireClient();

  return useQuery<Graph>({
    queryKey: ['lineage', 'bloodline', id, hops],
    queryFn: ({ signal }) => client.bloodline(id, hops, signal),
    enabled: id.length > 0,
  });
}

// B19 — the shortest path between two bands. Disabled until both ends are chosen and distinct.
export function useSixDegrees(from: string, to: string) {
  const client = useGrimoireClient();

  return useQuery<PathResult>({
    queryKey: ['lineage', 'six-degrees', from, to],
    queryFn: ({ signal }) => client.sixDegrees(from, to, signal),
    enabled: from.length > 0 && to.length > 0 && from !== to,
  });
}

// B11 — where a band's departed members went next.
export function useDiaspora(id: string) {
  const client = useGrimoireClient();

  return useQuery<Diaspora>({
    queryKey: ['lineage', 'diaspora', id],
    queryFn: ({ signal }) => client.diaspora(id, signal),
    enabled: id.length > 0,
  });
}

// B3 — every band a musician played in.
export function useMemberBands(id: string, enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<MemberBands>({
    queryKey: ['lineage', 'member-bands', id],
    queryFn: ({ signal }) => client.memberBands(id, signal),
    enabled: enabled && id.length > 0,
  });
}

// C5 — the bands between two others in embedding space. Disabled until both ends are distinct.
export function useMissingLink(from: string, to: string) {
  const client = useGrimoireClient();

  return useQuery<MissingLink>({
    queryKey: ['lineage', 'missing-link', from, to],
    queryFn: ({ signal }) => client.missingLink(from, to, signal),
    enabled: from.length > 0 && to.length > 0 && from !== to,
  });
}

// C8 — a guided walk through the lineage. Only fetched when explicitly started, so it is opt-in.
export function useRabbitHole(id: string, length: number, enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<RabbitHole>({
    queryKey: ['lineage', 'rabbit-hole', id, length],
    queryFn: ({ signal }) => client.rabbitHole(id, length, signal),
    enabled: enabled && id.length > 0,
  });
}

// C17 — the signed-in user's grimoire as a graph.
export function useGrimoireGraph(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<Graph>({
    queryKey: ['lineage', 'grimoire-graph'],
    queryFn: ({ signal }) => client.grimoireGraph(signal),
    enabled,
  });
}
