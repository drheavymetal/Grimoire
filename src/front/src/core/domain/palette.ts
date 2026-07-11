// Pure colour helpers for the chromatic drift (C26): the dominant palette of a band's discography
// over time, sampled from the album covers. No DOM here — the ui/ layer draws each cover to a canvas
// and hands the raw RGBA pixels in (invariant 6, D12), so the averaging is tested without a browser.

export interface Rgb {
  r: number;
  g: number;
  b: number;
}

// The mean colour of an RGBA pixel buffer, skipping (near-)transparent pixels so a cover with an
// alpha border does not wash the average toward black. Returns null when nothing opaque is present —
// the honest "no colour to sample", which the strip renders as a gap rather than a fabricated swatch.
export function averageColor(pixels: Uint8ClampedArray): Rgb | null {
  let r = 0;
  let g = 0;
  let b = 0;
  let count = 0;

  for (let i = 0; i + 3 < pixels.length; i += 4) {
    const alpha = pixels[i + 3];
    if (alpha < 8) {
      continue;
    }

    r += pixels[i];
    g += pixels[i + 1];
    b += pixels[i + 2];
    count += 1;
  }

  if (count === 0) {
    return null;
  }

  return {
    r: Math.round(r / count),
    g: Math.round(g / count),
    b: Math.round(b / count),
  };
}

// A #rrggbb string for a sampled colour, each channel clamped to a byte.
export function rgbToHex({ r, g, b }: Rgb): string {
  const channel = (n: number): string => Math.max(0, Math.min(255, Math.round(n))).toString(16).padStart(2, '0');
  return `#${channel(r)}${channel(g)}${channel(b)}`;
}
