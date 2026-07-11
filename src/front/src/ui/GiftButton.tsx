import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useCreateGift } from '../core/hooks/useGift';
import { ApiError } from '../core/api/client';
import { useAuth } from './auth/AuthProvider';

// C22 — the giver's side: wrap this band as a blind, signed gift and get a shareable link. Only
// offered to signed-in users. A band with no preview cannot be gifted (it could not sound blind);
// the API answers 422 and we say so plainly rather than minting a broken gift.
export function GiftButton({ artistId }: { artistId: string }) {
  const { t } = useTranslation();
  const { isAuthenticated } = useAuth();
  const createGift = useCreateGift();
  const [note, setNote] = useState('');
  const [open, setOpen] = useState(false);

  if (!isAuthenticated) {
    return null;
  }

  const gift = createGift.data;
  const noPreview =
    createGift.isError && createGift.error instanceof ApiError && createGift.error.status === 422;
  // Built in ui/ (not core/): reading the origin here is allowed — invariant 6 only guards core/.
  const shareUrl = gift ? `${window.location.origin}/gift/${encodeURIComponent(gift.token)}` : '';

  return (
    <section className="mt-8 border border-line p-4">
      <h2 className="font-display text-xl text-strong">{t('gift.giveTitle')}</h2>
      <p className="mt-1 font-mono text-xs text-muted">{t('gift.giveHint')}</p>

      {gift === undefined ? (
        <div className="mt-3">
          {open ? (
            <div className="space-y-3">
              <input
                type="text"
                value={note}
                onChange={(event) => setNote(event.target.value)}
                maxLength={280}
                placeholder={t('gift.notePlaceholder')}
                className="w-full border border-line bg-panel px-3 py-2 font-body text-strong outline-none focus:border-accent"
              />
              <button
                type="button"
                onClick={() => createGift.mutate({ artistId, note: note.trim().length > 0 ? note.trim() : null })}
                disabled={createGift.isPending}
                className="border border-accent px-4 py-2 font-mono text-xs uppercase text-accent hover:bg-accent hover:text-bg disabled:opacity-50"
              >
                {createGift.isPending ? t('gift.wrapping') : t('gift.wrap')}
              </button>
              {noPreview ? (
                <p className="font-mono text-xs text-danger">{t('gift.noPreview')}</p>
              ) : createGift.isError ? (
                <p className="font-mono text-xs text-danger">{t('gift.giveError')}</p>
              ) : null}
            </div>
          ) : (
            <button
              type="button"
              onClick={() => setOpen(true)}
              className="border border-line px-4 py-2 font-mono text-xs uppercase text-strong hover:border-accent hover:text-accent"
            >
              {t('gift.wrap')}
            </button>
          )}
        </div>
      ) : (
        <div className="mt-3">
          <p className="font-mono text-xs uppercase text-muted">{t('gift.linkReady')}</p>
          <code className="mt-1 block overflow-x-auto whitespace-nowrap border border-line bg-panel px-3 py-2 font-mono text-xs text-strong">
            {shareUrl}
          </code>
          <p className="mt-2 font-mono text-xs text-muted">{t('gift.linkHint')}</p>
        </div>
      )}
    </section>
  );
}
