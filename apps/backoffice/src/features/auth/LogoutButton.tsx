import Button from '@mui/material/Button';
import { api, clearSession } from '../../shared';

export function LogoutButton({ onDone }: { onDone: () => void }) {
  const logout = async () => {
    try {
      await api('/auth/logout', { method: 'POST' });
    } finally {
      clearSession();
      onDone();
    }
  };
  return (
    <Button color="inherit" onClick={logout}>
      Cerrar sesión
    </Button>
  );
}
