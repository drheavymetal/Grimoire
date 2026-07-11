import type { AudioAdapter, AudioState } from '../core/audio/types';

// Web audio adapter (DECISIONS D12, invariant 6): the ONLY place HTMLAudioElement is
// touched. core/ never sees it — the UI drives The Rite through the AudioAdapter port,
// and a native build swaps this file for an expo-av implementation of the same contract.
// Blind listening is one band at a time: only a single element may sound across the whole
// app. Whenever any player starts, the previously sounding one is paused. This keeps the
// Weekly Rite's seven (and any two duel tracks) from overlapping into noise.
let currentPlaying: HTMLAudioElement | null = null;

export function createWebAudio(): AudioAdapter {
  const element = new Audio();
  element.preload = 'auto';

  let state: AudioState = { status: 'idle', positionSec: 0, durationSec: 0 };
  const listeners = new Set<(s: AudioState) => void>();

  function emit(next: Partial<AudioState>): void {
    state = { ...state, ...next };
    for (const listener of listeners) {
      listener(state);
    }
  }

  element.addEventListener('loadstart', () => emit({ status: 'loading' }));
  element.addEventListener('durationchange', () => {
    emit({ durationSec: Number.isFinite(element.duration) ? element.duration : 0 });
  });
  element.addEventListener('playing', () => emit({ status: 'playing' }));
  element.addEventListener('pause', () => {
    if (state.status !== 'ended') {
      emit({ status: 'paused' });
    }
  });
  element.addEventListener('timeupdate', () => emit({ positionSec: element.currentTime }));
  element.addEventListener('ended', () => {
    if (currentPlaying === element) {
      currentPlaying = null;
    }
    emit({ status: 'ended', positionSec: element.duration || 0 });
  });
  element.addEventListener('error', () => emit({ status: 'error' }));

  return {
    load(url) {
      element.src = url;
      element.currentTime = 0;
      emit({ status: 'loading', positionSec: 0, durationSec: 0 });
    },
    async play() {
      if (currentPlaying !== null && currentPlaying !== element) {
        currentPlaying.pause();
      }
      currentPlaying = element;
      await element.play();
    },
    pause() {
      if (currentPlaying === element) {
        currentPlaying = null;
      }
      element.pause();
    },
    dispose() {
      if (currentPlaying === element) {
        currentPlaying = null;
      }
      element.pause();
      element.removeAttribute('src');
      element.load();
      listeners.clear();
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    getState() {
      return state;
    },
  };
}
