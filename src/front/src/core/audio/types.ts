// The audio adapter contract (DECISIONS D12, invariant 6). The Rite streams a blind
// preview through this port; the web build backs it with HTMLAudioElement and a native
// build would back it with expo-av. core/ defines only the shape — no DOM, no globals —
// so the same contract ports without change.

export type AudioStatus = 'idle' | 'loading' | 'playing' | 'paused' | 'ended' | 'error';

export interface AudioState {
  status: AudioStatus;
  positionSec: number;
  durationSec: number;
}

export interface AudioAdapter {
  // Point the player at a URL (the proxied /api/rite/{token}/audio capability URL).
  load(url: string): void;
  play(): Promise<void>;
  pause(): void;
  // Release the underlying resource; safe to call more than once.
  dispose(): void;
  // Subscribe to state changes; returns an unsubscribe function.
  subscribe(listener: (state: AudioState) => void): () => void;
  getState(): AudioState;
}
