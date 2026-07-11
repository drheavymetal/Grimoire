import { useTranslation } from 'react-i18next';
import { useReleaseTracks } from '../../core/hooks/useRecordings';
import { formatTrackLength } from '../../core/domain/recordings';

// B5 — the tracklist of one release, shown when its discography row is expanded. Reads the real
// recordings through a core/ hook (only once the row is open, so the discography does not fan out on
// mount). Each track is its position, title and length; a length MusicBrainz never recorded shows an
// em dash, never a fabricated time (C7 honesty). A release whose tracks the import never reached
// degrades to a designed empty line (R2).
export function Tracklist({ artistId, releaseId, enabled }: { artistId: string; releaseId: string; enabled: boolean }) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useReleaseTracks(artistId, releaseId, enabled);

  if (isLoading) {
    return <p className="font-mono text-xs text-muted">{t('tracks.loading')}</p>;
  }

  if (isError) {
    return <p className="font-mono text-xs text-danger">{t('tracks.error')}</p>;
  }

  if (data === undefined) {
    return null;
  }

  if (data.length === 0) {
    return <p className="font-mono text-xs text-muted">{t('tracks.empty')}</p>;
  }

  return (
    <div>
      <h4 className="font-mono text-[0.6rem] uppercase text-muted">{t('tracks.heading')}</h4>
      <ol className="mt-1 space-y-0.5">
        {data.map((track) => (
          <li
            key={track.position}
            className="flex items-baseline gap-3 font-body text-sm text-strong"
          >
            <span className="w-6 shrink-0 text-right font-mono text-xs text-muted">{track.position}</span>
            <span className="min-w-0 flex-1 truncate">{track.title}</span>
            <span className="shrink-0 font-mono text-xs text-muted tabular-nums">
              {formatTrackLength(track.lengthMs)}
            </span>
          </li>
        ))}
      </ol>
    </div>
  );
}
