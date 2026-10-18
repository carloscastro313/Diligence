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

const routeTree = rootRoute.addChildren([protectedRoute.addChildren([homeRoute, usersRoute, areasRoute]), loginRoute, forgotRoute, resetRoute]);

export const createAppRouter = (history?: RouterHistory) => createRouter({ routeTree, history });
export { createMemoryHistory };

export const router = createAppRouter();

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
