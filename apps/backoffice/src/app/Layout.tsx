import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { Link, Outlet, useNavigate } from '@tanstack/react-router';
import { useEffect } from 'react';
import { LogoutButton } from '../features/auth/LogoutButton';
import { hasRole, Roles, useSession } from '../shared';

const linkStyle = { color: 'inherit', textDecoration: 'none', marginRight: 16 };

export function Layout() {
  const { data: session } = useSession();
  const navigate = useNavigate();
  const canSeeDirectory = hasRole(session, Roles.Administrador, Roles.Auditor);

  // Un 401 en cualquier request (sesión vencida o revocada) deja la sesión en null: volver a ingresar.
  useEffect(() => {
    if (session === null) void navigate({ to: '/login' });
  }, [session, navigate]);

  return (
    <>
      <AppBar position="static">
        <Toolbar>
          <Typography variant="h6" component="span" sx={{ mr: 4 }}>
            Diligencia
          </Typography>
          {/* Solo comodidad: la API decide siempre. */}
          {canSeeDirectory && (
            <>
              <Link to="/usuarios" style={linkStyle}>
                Usuarios
              </Link>
              <Link to="/areas" style={linkStyle}>
                Áreas
              </Link>
            </>
          )}
          {hasRole(session, Roles.Solicitante) && (
            <Link to="/solicitudes" style={linkStyle}>
              Mis solicitudes
            </Link>
          )}
          <Box sx={{ flexGrow: 1 }} />
          <Typography variant="body2" sx={{ mr: 2 }}>
            {session?.name}
          </Typography>
          <LogoutButton onDone={() => navigate({ to: '/login' })} />
        </Toolbar>
      </AppBar>
      <Outlet />
    </>
  );
}
