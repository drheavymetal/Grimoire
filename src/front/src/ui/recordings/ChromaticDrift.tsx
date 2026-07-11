import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useGrimoireClient } from '../../core/api/context';
import { averageColor, rgbToHex } from '../../core/domain/palette';
import type { Release } from '../../core/domain/types';

// C26 — chromatic drift: the dominant colour of a band's album covers across time, as a visual
// strip. The colour is sampled ON THE CLIENT: each cover is drawn to a small offscreen canvas and
// its opaque pixels are averaged (the averaging is a pure, tested core/ function). Reading canvas
// pixels of a proxied image would normally taint the canvas; the cover proxy now sends an open CORS
// header (C26) so a crossOrigin='anonymous' image can be read. A cover the archive lacks leaves a
// gap in the strip — never an invented swatch. The band with no readable cover degrades to a
// designed empty state (R2).

const MAX_ALBUMS = 24;
const SAMPLE = 12; // px: the covers are downscaled to a tiny square before averaging.

interface Swatch {
  releaseId: string;
  year: string;
  hex: string | null;
}

export function ChromaticDrift({ releases }: { releases: Release[] }) {
  const { t } = useTranslation();
  const client = useGrimoireClient();
  const [tainted, setTainted] = useState(false);

  // The albums with cover art, oldest first — the drift reads left (early) to right (late).
  const albums = useMemo(
    () =>
      releases
        .filter((r) => r.type === 'Album' && r.mbid.length > 0)
        .sort((a, b) => (a.releaseDate ?? '9999').localeCompare(b.releaseDate ?? '9999'))
        .slice(0, MAX_ALBUMS),
    [releases],
  );

  const [swatches, setSwatches] = useState<Swatch[]>([]);

  useEffect(() => {
    setTainted(false);
    setSwatches(albums.map((a) => ({ releaseId: a.id, year: a.releaseDate?.slice(0, 4) ?? '—', hex: null })));

    if (albums.length === 0) {
      return;
    }

    let cancelled = false;
    const canvas = document.createElement('canvas');
    canvas.width = SAMPLE;
    canvas.height = SAMPLE;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });

    const setHex = (releaseId: string, hex: string): void => {
      setSwatches((prev) => prev.map((s) => (s.releaseId === releaseId ? { ...s, hex } : s)));
    };

    const images: HTMLImageElement[] = [];

    for (const album of albums) {
      const img = new Image();
      img.crossOrigin = 'anonymous';
      img.onload = () => {
        if (cancelled || ctx === null) {
          return;
        }
        try {
          ctx.clearRect(0, 0, SAMPLE, SAMPLE);
          ctx.drawImage(img, 0, 0, SAMPLE, SAMPLE);
          const data = ctx.getImageData(0, 0, SAMPLE, SAMPLE).data;
          const avg = averageColor(data);
          if (avg !== null) {
            setHex(album.id, rgbToHex(avg));
          }
        } catch {
          // A SecurityError here means the canvas tainted despite the CORS header — surface it as a
          // designed notice rather than swallowing it (no empty catch: state is set).
          if (!cancelled) {
            setTainted(true);
          }
        }
      };
      // A missing cover (404) simply leaves this swatch a gap — no error state, that is honest.
      img.src = client.coverUrl(album.mbid);
      images.push(img);
    }

    return () => {
      cancelled = true;
      for (const img of images) {
        img.onload = null;
        img.onerror = null;
      }
    };
  }, [albums, client]);

  if (albums.length === 0) {
    return null;
  }

  const sampled = swatches.filter((s) => s.hex !== null).length;

  return (
    <section className="mt-8">
      <h2 className="font-mono text-xs uppercase text-muted">{t('chromatic.title')}</h2>
      <p className="mt-1 max-w-prose font-mono text-[0.65rem] text-muted">{t('chromatic.hint')}</p>

      {sampled === 0 ? (
        <p className="mt-2 font-mono text-xs text-muted">
          {tainted ? t('chromatic.tainted') : t('chromatic.empty')}
        </p>
      ) : (
        <div className="mt-3 flex items-end gap-px overflow-x-auto">
          {swatches.map((swatch) => (
            <div key={swatch.releaseId} className="flex shrink-0 flex-col items-center gap-1">
              <div
                className="h-12 w-6"
                style={
                  swatch.hex !== null
                    ? { backgroundColor: swatch.hex }
                    : {
                        // A gap for a cover the archive lacks: a hatched cell, not a black block.
                        backgroundImage:
                          'repeating-linear-gradient(45deg, var(--color-line) 0 2px, transparent 2px 4px)',
                      }
                }
                title={swatch.year}
              />
              <span className="font-mono text-[0.55rem] text-muted">{swatch.year.slice(2)}</span>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
