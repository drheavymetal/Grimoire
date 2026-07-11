// The Grimoire mark and wordmark (D27, DESIGN §3). The mark is the app itself, not an ornament:
// the ring is the lineage, the sulphur vertical line is the time axis (the axis of the Gantt), and
// the right edge breaking into halftone dots is the information lost with each generation of copy —
// the same generation loss the app is built on. The mark NEVER carries the wordmark inside it, so it
// survives small sizes; the small favicon is a thicker sibling with three coarse dots (index.html).
//
// Theme-aware without JS: the ring is currentColor (bone on the void, ink on the flyer) and the axis
// is the accent token (bright sulphur in dark, the deep variant in light). Pure SVG primitives —
// react-native-svg accepts them unchanged, keeping the port cheap (invariant 6 / D12).

export function Mark({ size = 96, className, title }: { size?: number; className?: string; title?: string }) {
  const gradientId = 'grimoire-mark-fade';

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 100 100"
      className={className}
      role={title ? 'img' : undefined}
      aria-label={title}
      aria-hidden={title ? undefined : true}
    >
      <defs>
        <linearGradient id={gradientId} x1="0" y1="0" x2="1" y2="0">
          <stop offset="0" stopColor="currentColor" stopOpacity="1" />
          <stop offset="0.5" stopColor="currentColor" stopOpacity="0.95" />
          <stop offset="1" stopColor="currentColor" stopOpacity="0" />
        </linearGradient>
      </defs>
      {/* the lineage ring, dissolving into halftone on the right */}
      <circle cx="50" cy="50" r="36" fill="none" stroke={`url(#${gradientId})`} strokeWidth="2.4" />
      <circle cx="82" cy="38" r="1.8" fill="currentColor" opacity="0.55" />
      <circle cx="85" cy="50" r="1.4" fill="currentColor" opacity="0.4" />
      <circle cx="82" cy="62" r="1.2" fill="currentColor" opacity="0.28" />
      <circle cx="76" cy="70" r="1" fill="currentColor" opacity="0.2" />
      {/* the time axis, in sulphur — the only colour in the whole system */}
      <line x1="50" y1="8" x2="50" y2="92" stroke="var(--color-accent)" strokeWidth="2.4" />
    </svg>
  );
}

// The wordmark: GR[I]MOIRE in the display face, with the I struck in sulphur — the same time axis
// as the mark. `develop` runs the one-shot photo-develop reveal (landing hero); off by default so
// the nav lockup is instant. Rendered as inert text with an aria-label; it is never a heading.
export function Wordmark({ className, develop = false }: { className?: string; develop?: boolean }) {
  return (
    <span
      aria-label="Grimoire"
      className={`font-display ${develop ? 'wordmark-develop' : ''} ${className ?? ''}`}
      style={{ letterSpacing: '0.04em' }}
    >
      <span aria-hidden="true">GR</span>
      <span aria-hidden="true" className="text-accent">
        I
      </span>
      <span aria-hidden="true">MOIRE</span>
    </span>
  );
}

// The nav lockup: the small mark beside the wordmark, linking home. Kept here so the shell composes
// one element.
export function BrandLockup({ className }: { className?: string }) {
  return (
    <span className={`inline-flex items-center gap-2 text-strong ${className ?? ''}`}>
      <Mark size={22} className="shrink-0" />
      <Wordmark className="text-lg" />
    </span>
  );
}
