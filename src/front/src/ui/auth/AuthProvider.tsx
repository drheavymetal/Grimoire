import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useGrimoireClient } from '../../core/api/context';
import { authStore } from '../../platform/authStore.web';
import type { AuthTokens } from '../../core/domain/types';

// Auth state for The Rite (task 1). Tokens are held by the platform authStore (invariant 6);
// this provider mirrors sign-in state into React and exposes login/register/logout. It lives
// in ui/, which may import both core/ and platform/.

type AuthStatus = 'unknown' | 'authenticated' | 'anonymous';

interface AuthContextValue {
  status: AuthStatus;
  isAuthenticated: boolean;
  login(email: string, password: string): Promise<void>;
  register(email: string, password: string): Promise<void>;
  logout(): void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const client = useGrimoireClient();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<AuthStatus>('unknown');

  // On load, if a refresh token survives from a previous visit, rotate it for a fresh
  // access token (the access token may have expired, but the 16-day refresh token has not).
  // If refresh fails, the session is over: clear and fall back to anonymous.
  useEffect(() => {
    let cancelled = false;
    const refreshToken = authStore.getRefreshToken();

    if (!refreshToken) {
      setStatus('anonymous');
      return;
    }

    client
      .refresh(refreshToken)
      .then((tokens: AuthTokens) => {
        if (cancelled) {
          return;
        }
        authStore.setTokens(tokens);
        setStatus('authenticated');
      })
      .catch(() => {
        if (cancelled) {
          return;
        }
        authStore.clear();
        setStatus('anonymous');
      });

    return () => {
      cancelled = true;
    };
  }, [client]);

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      isAuthenticated: status === 'authenticated',
      async login(email, password) {
        const tokens = await client.login(email, password);
        authStore.setTokens(tokens);
        void queryClient.invalidateQueries({ queryKey: ['rite'] });
        setStatus('authenticated');
      },
      async register(email, password) {
        const tokens = await client.register(email, password);
        authStore.setTokens(tokens);
        void queryClient.invalidateQueries({ queryKey: ['rite'] });
        setStatus('authenticated');
      },
      logout() {
        authStore.clear();
        queryClient.removeQueries({ queryKey: ['rite'] });
        setStatus('anonymous');
      },
    }),
    [status, client, queryClient],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);

  if (value === null) {
    throw new Error('useAuth must be used within an AuthProvider.');
  }

  return value;
}
