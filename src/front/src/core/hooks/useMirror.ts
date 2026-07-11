import { useQuery } from '@tanstack/react-query';
import { useGrimoireClient } from '../api/context';
import type { AntiRec, DarkTwin, Gaps, Reflection, Trajectory } from '../domain/types';

// The mirror (C20): what fraction of the bands you rejected blind match your favourite genre.
export function useReflection(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<Reflection>({
    queryKey: ['mirror', 'reflection'],
    queryFn: ({ signal }) => client.reflection(signal),
    enabled,
  });
}

// Your taste trajectory over time (C16), projected onto the Atlas plane.
export function useTrajectory(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<Trajectory>({
    queryKey: ['mirror', 'trajectory'],
    queryFn: ({ signal }) => client.trajectory(signal),
    enabled,
  });
}

// The band the engine predicts will repel you, and why (B25).
export function useAntiRec(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<AntiRec>({
    queryKey: ['mirror', 'antiRec'],
    queryFn: ({ signal }) => client.antiRec(signal),
    enabled,
  });
}

// The nearest-taste, most-disjoint user — anonymous (B18).
export function useDarkTwin(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<DarkTwin>({
    queryKey: ['mirror', 'darkTwin'],
    queryFn: ({ signal }) => client.darkTwin(signal),
    enabled,
  });
}

// Decades, countries and subgenres you have never summoned — the dark Atlas (B23).
export function useGaps(enabled: boolean) {
  const client = useGrimoireClient();

  return useQuery<Gaps>({
    queryKey: ['mirror', 'gaps'],
    queryFn: ({ signal }) => client.gaps(signal),
    enabled,
  });
}
