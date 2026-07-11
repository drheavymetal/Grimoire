import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { createWebAudio } from '../../platform/audio.web';
import type { AudioState } from '../../core/audio/types';

// The blind preview player (feature B13). Audio runs through the platform AudioAdapter,
// never HTMLAudioElement in core (invariant 6, D12). The <audio> points ONLY at the
// proxied capability URL — the origin iTunes/Deezer URL never reaches the client. No band
// name, cover, country or genre is shown: this is the whole point of the rite.
// autoPlay defaults to true for the single daily rite (the serve was a user gesture). Screens
// that mount several players at once — the Weekly Rite's seven — pass autoPlay={false} so they
// don't all sound together; the listener presses play on the one they want, and the global audio
// coordination (audio.web.ts) still guarantees only one sounds at a time.
export function RitePlayer({ audioUrl, autoPlay = true }: { audioUrl: string; autoPlay?: boolean }) {
  const { t } = useTranslation();
  const audio = useMemo(() => createWebAudio(), []);
  const [state, setState] = useState<AudioState>(() => audio.getState());

  useEffect(() => {
    const unsubscribe = audio.subscribe(setState);
    audio.load(audioUrl);
    if (autoPlay) {
      // If the browser blocks autoplay, the player simply stays paused and the listener presses play.
      void audio.play().catch(() => undefined);
    }

    return () => {
      unsubscribe();
      audio.dispose();
    };
  }, [audio, audioUrl, autoPlay]);

  const isPlaying = state.status === 'playing';
  const progress = state.durationSec > 0 ? Math.min(1, state.positionSec / state.durationSec) : 0;

  function toggle() {
    if (isPlaying) {
      audio.pause();
    } else {
      void audio.play().catch(() => undefined);
    }
  }

  return (
    // The ritual: a signal that sounds in the dark, blind. Concentric rings, a sulphur core that
    // pulses only while it plays, and the time counting up — no name, no cover, nothing to judge by
    // but the ear. The pulse stops under prefers-reduced-motion (styles.css).
    <div className="flyer border border-line bg-panel px-6 py-8">
      <p className="text-center font-mono text-xs uppercase tracking-[0.3em] text-faint">
        {t('rite.blindTitle')}
      </p>

      <div className="relative mx-auto mt-6 grid h-56 w-56 place-items-center sm:h-64 sm:w-64">
        <span aria-hidden="true" className="pointer-events-none absolute inset-0 rounded-full border border-line" />
        <span aria-hidden="true" className="pointer-events-none absolute inset-[14%] rounded-full border border-line" />
        <span aria-hidden="true" className="pointer-events-none absolute inset-[28%] rounded-full border border-line" />
        {isPlaying ? (
          <>
            <span aria-hidden="true" className="signal-pulse pointer-events-none absolute inset-0 rounded-full border border-accent" />
            <span aria-hidden="true" className="signal-pulse signal-pulse-delay pointer-events-none absolute inset-0 rounded-full border border-accent" />
          </>
        ) : null}
        <button
          type="button"
          onClick={toggle}
          aria-label={isPlaying ? t('rite.pause') : t('rite.play')}
          className="relative z-10 grid h-16 w-16 place-items-center rounded-full bg-accent text-2xl text-bg shadow-[0_0_28px_6px_rgba(214,195,74,0.35)] hover:opacity-90"
        >
          {isPlaying ? '❙❙' : '▶'}
        </button>
      </div>

      <p className="mt-6 text-center font-mono text-xs tracking-[0.2em] text-muted">
        {formatTime(state.positionSec)} / {formatTime(state.durationSec)}
      </p>
      <p className="mt-3 text-center font-mono text-xs text-faint">{t('rite.blindHint')}</p>

      <div className="mx-auto mt-4 h-px w-full max-w-md bg-line">
        <div className="h-full bg-accent" style={{ width: `${progress * 100}%` }} />
      </div>

      {state.status === 'error' ? (
        <p className="mt-3 text-center font-mono text-xs text-danger">{t('rite.audioError')}</p>
      ) : null}
    </div>
  );
}

// mm:ss for the blind counter. Pure, local — a NaN/negative duration reads as 0:00, never a lie.
function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) {
    return '0:00';
  }
  const total = Math.floor(seconds);
  const mins = Math.floor(total / 60);
  const secs = total % 60;
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}
