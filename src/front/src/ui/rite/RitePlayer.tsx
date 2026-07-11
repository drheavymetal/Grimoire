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
    <div className="border border-line bg-panel p-6">
      <div className="flex items-center gap-4">
        <button
          type="button"
          onClick={toggle}
          aria-label={isPlaying ? t('rite.pause') : t('rite.play')}
          className="flex h-14 w-14 shrink-0 items-center justify-center border border-accent text-2xl text-accent hover:bg-accent hover:text-bg"
        >
          {isPlaying ? '❙❙' : '▶'}
        </button>
        <div className="min-w-0 flex-1">
          <p className="font-display text-2xl text-strong">{t('rite.blindTitle')}</p>
          <p className="font-mono text-xs text-muted">{t('rite.blindHint')}</p>
        </div>
      </div>

      <div className="mt-5 h-1 w-full bg-line">
        <div className="h-full bg-accent" style={{ width: `${progress * 100}%` }} />
      </div>

      {state.status === 'error' ? (
        <p className="mt-3 font-mono text-xs text-danger">{t('rite.audioError')}</p>
      ) : null}
    </div>
  );
}
