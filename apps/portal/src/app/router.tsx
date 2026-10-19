import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  redirect,
  useLocation,
  useNavigate,
  type RouterHistory,
} from '@tanstack/react-router';
import { AccesoPage } from '../features/acceso/AccesoPage';
import { CasoPage } from '../features/caso/CasoPage';
import { HomePage } from '../features/home/HomePage';
import { api, ApiError, setUnauthorizedHandler } from '../shared';
import { queryClient } from './queryClient';

const rootRoute = createRootRoute({ component: Outlet });
const homeRoute = createRoute({ getParentRoute: () => rootRoute, path: '/', component: HomePage });

function AccesoRoute() {
  const navigate = useNavigate();
  const vencida = useLocation({ select: (l) => !!(l.state as { sesionVencida?: boolean }).sesionVencida });
  return <AccesoPage vencida={vencida} onSuccess={() => navigate({ to: '/caso' })} />;
}
const accesoRoute = createRoute({ getParentRoute: () => rootRoute, path: '/acceso', component: AccesoRoute });

const casoRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/caso',
  // Sin sesión (o vencida) se vuelve a /acceso.
  beforeLoad: async () => {
    try {
      await queryClient.fetchQuery({ queryKey: ['caso'], queryFn: () => api('/caso') });
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) throw redirect({ to: '/acceso' });
      throw e;
    }
  },
  component: CasoPage,
});

export const createAppRouter = (history?: RouterHistory) =>
  createRouter({ routeTree: rootRoute.addChildren([homeRoute, accesoRoute, casoRoute]), history });
export { createMemoryHistory };

export const router = createAppRouter();

// Sesión vencida a mitad de camino (p. ej. al subir un archivo): vuelve a /acceso con el aviso, salvo que ya esté ahí.
// ignoreBlocker: el diálogo de "cambios sin guardar" no tiene sentido sin sesión, no hay forma de guardarlos.
export function sesionVencida(r: typeof router) {
  if (r.state.location.pathname !== '/acceso') void r.navigate({ to: '/acceso', ignoreBlocker: true, state: { sesionVencida: true } as never });
}
setUnauthorizedHandler(() => sesionVencida(router));

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
