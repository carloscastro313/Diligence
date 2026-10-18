import { QueryClient, useQuery } from '@tanstack/react-query';
import { api, ApiError, setUnauthorizedHandler } from './api';

export const Roles = {
  Solicitante: 'Solicitante',
  Analista: 'Analista',
  Aprobador: 'Aprobador',
  Administrador: 'Administrador',
  Auditor: 'Auditor',
} as const;

export type Session = { id: string; name: string; email: string; areaName: string; roles: string[] };

export const queryClient = new QueryClient();
const sessionKey = ['session'];

async function fetchSession(): Promise<Session | null> {
  try {
    return await api<Session>('/auth/me');
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) return null;
    throw e;
  }
}

setUnauthorizedHandler(() => queryClient.setQueryData(sessionKey, null));

/** Para las guardas del router (fuera de React). */
export const loadSession = () => queryClient.fetchQuery({ queryKey: sessionKey, queryFn: fetchSession, staleTime: 30_000 });

/** Vuelve a leer la sesión del servidor (tras ingresar). */
export const refreshSession = () => queryClient.fetchQuery({ queryKey: sessionKey, queryFn: fetchSession, staleTime: 0 });

export const clearSession = () => queryClient.setQueryData(sessionKey, null);

export const useSession = () => useQuery({ queryKey: sessionKey, queryFn: fetchSession, staleTime: 30_000 });

// Solo para mostrar u ocultar opciones: la autoridad es siempre la API.
export const hasRole = (session: Session | null | undefined, ...roles: string[]) =>
  !!session && roles.some((r) => session.roles.includes(r));
