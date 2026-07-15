import { useTranslation } from 'react-i18next';
import { streamingLinks } from '../core/domain/streaming';

// A row of "listen on" links to the major services, built from a query (a band name, or "band
// album"). Grimoire itself never plays music (invariant 4) — these just open each service's search.
// Opens in a new tab; rel guards against the opener being reachable.
export function StreamingLinks({ query, className }: { query: string; className?: string }) {
  const { t } = useTranslation();
  const links = streamingLinks(query);

  return (
    <div className={className}>
      <span className="mr-2 font-mono text-[0.6rem] uppercase text-muted">{t('artist.listenOn')}</span>
      <span className="inline-flex flex-wrap gap-x-3 gap-y-1">
        {links.map((link) => (
          <a
            key={link.service}
            href={link.url}
            target="_blank"
            rel="noreferrer noopener"
            className="font-mono text-xs text-muted no-underline hover:text-accent"
          >
            {link.label}
          </a>
        ))}
      </span>
    </div>
  );
}
