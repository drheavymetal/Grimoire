// Web download adapter (invariant 6): the ONLY place the DOM is touched to save a file. core/
// never sees this — it builds the URL (a pure string) and the UI asks the platform to fetch it
// with the bearer token and hand the browser a blob to save. A native build swaps this file for
// an expo-file-system implementation of the same contract.

// Fetches an authenticated attachment and triggers a browser "save as". The URL is built by the
// core client (pure string); the bearer comes from the platform auth store. The server sends the
// file as a Content-Disposition attachment, so its filename is honoured when present, with a
// sensible fallback. Throws when the request fails so the UI can surface an error.
export async function downloadAuthenticated(
  url: string,
  token: string | null,
  fallbackFilename: string,
): Promise<void> {
  const headers: Record<string, string> = {};

  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(url, { headers });

  if (!response.ok) {
    throw new Error(`Download failed with ${response.status}.`);
  }

  const blob = await response.blob();
  const filename = filenameFromDisposition(response.headers.get('Content-Disposition')) ?? fallbackFilename;

  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = objectUrl;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(objectUrl);
}

// Pulls the filename out of a Content-Disposition header, tolerating both the plain `filename="…"`
// and the RFC 5987 `filename*=UTF-8''…` forms. Returns null when the header is absent or unparsable.
function filenameFromDisposition(header: string | null): string | null {
  if (header === null) {
    return null;
  }

  const extended = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(header);
  if (extended !== null) {
    try {
      return decodeURIComponent(extended[1].replace(/^"|"$/g, ''));
    } catch {
      // Fall through to the plain form below.
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(header);
  if (plain !== null) {
    return plain[1];
  }

  return null;
}
