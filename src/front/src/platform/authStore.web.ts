import { webStorage } from './storage.web';
import type { AuthTokens } from '../core/domain/types';

// The token store for the Rite's auth (task 1, invariant 6): tokens live in the
// platform layer, never in core. It is a small singleton so the API client can read
// the current access token via an injected getter without knowing about React or
// storage, while AuthProvider mirrors the same tokens into React state for rendering.

const ACCESS_KEY = 'grimoire-access-token';
const REFRESH_KEY = 'grimoire-refresh-token';

let accessToken: string | null = webStorage.get(ACCESS_KEY);
let refreshToken: string | null = webStorage.get(REFRESH_KEY);

export const authStore = {
  getAccessToken(): string | null {
    return accessToken;
  },
  getRefreshToken(): string | null {
    return refreshToken;
  },
  setTokens(tokens: AuthTokens): void {
    accessToken = tokens.accessToken;
    refreshToken = tokens.refreshToken;
    webStorage.set(ACCESS_KEY, tokens.accessToken);
    webStorage.set(REFRESH_KEY, tokens.refreshToken);
  },
  clear(): void {
    accessToken = null;
    refreshToken = null;
    webStorage.set(ACCESS_KEY, '');
    webStorage.set(REFRESH_KEY, '');
  },
};
