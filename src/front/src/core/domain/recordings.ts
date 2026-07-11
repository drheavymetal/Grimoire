// Pure, portable helpers for the recording features (B5 tracklist, C7 the duration axis). No DOM,
// no platform coupling (invariant 6, D12): the ui/ layer renders what these return, and they are
// tested without a browser. The rule that matters is honesty about a missing length: MusicBrainz
// leaves many tracks untimed, and an absence must read as an em dash, never a fabricated 0:00.

// Formats a track length in milliseconds as m:ss (or h:mm:ss past an hour). A null or negative
// length — MusicBrainz had none — renders as an em dash, mirroring the server's DurationMath so the
// tracklist and the reveal agree.
export function formatTrackLength(lengthMs: number | null): string {
  if (lengthMs === null || lengthMs < 0) {
    return '—';
  }

  const totalSeconds = Math.floor(lengthMs / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  const ss = seconds.toString().padStart(2, '0');

  if (hours > 0) {
    const mm = minutes.toString().padStart(2, '0');
    return `${hours}:${mm}:${ss}`;
  }

  return `${minutes}:${ss}`;
}

// Formats a mean track length (a float number of ms) as a whole-second m:ss, for the duration axis
// (C7). Rounds to the nearest second so a 251280.4 ms average does not print a spurious tail.
export function formatAverageLength(averageMs: number): string {
  return formatTrackLength(Math.round(averageMs));
}
