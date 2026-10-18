import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  redirect,
  useNavigate,
  type RouterHistory,
} from '@tanstack/react-router';
import { AreasPage } from '../features/areas/AreasPage';
import { ForgotPasswordPage } from '../features/auth/ForgotPasswordPage';
import { LoginPage } from '../features/auth/LoginPage';
import { ResetPasswordPage } from '../features/auth/ResetPasswordPage';
import { HomePage } from '../features/home/HomePage';
import { NuevaSolicitudPage } from '../features/solicitudes/NuevaSolicitudPage';
import { SolicitudesPage, type SolicitudFilters } from '../features/solicitudes/SolicitudesPage';
import { loadSession } from '../shared';
import { UsersPage, type UserFilters } from '../features/users/UsersPage';
import { Layout } from './Layout';

const rootRoute = createRootRoute({ component: Outlet });

// Rutas protegidas: sin sesión se va a /login.
const protectedRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'protected',
  beforeLoad: async () => {
    if (!(await loadSession())) throw redirect({ to: '/login' });
  },
  component: Layout,
});
const homeRoute = createRoute({ getParentRoute: () => protectedRoute, path: '/', component: HomePage });

const usersRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/usuarios',
  // Los filtros viven en la query string.
  validateSearch: (s: Record<string, unknown>): UserFilters => ({
    role: typeof s.role === 'string' ? s.role : undefined,
    areaId: typeof s.areaId === 'string' ? s.areaId : undefined,
    status: typeof s.status === 'string' ? s.status : undefined,
  }),
  component: function UsersRoute() {
    const filters = usersRoute.useSearch();
    const navigate = useNavigate();
    return <UsersPage filters={filters} onFiltersChange={(search) => navigate({ to: '/usuarios', search })} />;
  },
});
const areasRoute = createRoute({ getParentRoute: () => protectedRoute, path: '/areas', component: AreasPage });

const nuevaSolicitudRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/solicitudes/nueva',
  component: function NuevaSolicitudRoute() {
    const navigate = useNavigate();
    return <NuevaSolicitudPage onCreated={() => navigate({ to: '/solicitudes' })} />;
  },
});

const solicitudesRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: '/solicitudes',
  validateSearch: (s: Record<string, unknown>): SolicitudFilters => ({
    estado: typeof s.estado === 'string' ? s.estado : undefined,
    q: typeof s.q === 'string' ? s.q : undefined,
  }),
  component: function SolicitudesRoute() {
    const filters = solicitudesRoute.useSearch();
    const navigate = useNavigate();
    return (
      <SolicitudesPage
        filters={filters}
        onFiltersChange={(search) => navigate({ to: '/solicitudes', search })}
        onNueva={() => navigate({ to: '/solicitudes/nueva' })}
      />
    );
  },
});

const forgotRoute = createRoute({ getParentRoute: () => rootRoute, path: '/olvide', component: ForgotPasswordPage });

const resetRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/restablecer',
  validateSearch: (s: Record<string, unknown>) => ({ token: typeof s.token === 'string' ? s.token : '' }),
  component: function ResetRoute() {
    const { token } = resetRoute.useSearch();
    const navigate = useNavigate();
    return <ResetPasswordPage token={token} onDone={() => navigate({ to: '/login' })} />;
  },
});

function LoginRoute() {
  const navigate = useNavigate();
  return <LoginPage onSuccess={() => navigate({ to: '/' })} />;
}
const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  beforeLoad: async () => {
    if (await loadSession()) throw redirect({ to: '/' });
  },
  component: LoginRoute,
});

const routeTree = rootRoute.addChildren([protectedRoute.addChildren([homeRoute, usersRoute, areasRoute, solicitudesRoute, nuevaSolicitudRoute]), loginRoute, forgotRoute, resetRoute]);

export const createAppRouter = (history?: RouterHistory) => createRouter({ routeTree, history });
export { createMemoryHistory };

export const router = createAppRouter();

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
