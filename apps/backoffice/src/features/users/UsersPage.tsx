import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Container from '@mui/material/Container';
import MenuItem from '@mui/material/MenuItem';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { api, ApiError, hasRole, Roles, useSession } from '../../shared';
import { allRoles, type AreaOption, UserDialog, type UserRow } from './UserDialog';

export type UserFilters = { role?: string; areaId?: string; status?: string };

export function UsersPage({ filters, onFiltersChange }: { filters: UserFilters; onFiltersChange: (f: UserFilters) => void }) {
  const client = useQueryClient();
  const { data: session } = useSession();
  // El Auditor solo consulta; la API lo exige igual.
  const canWrite = hasRole(session, Roles.Administrador);
  const [dialog, setDialog] = useState<{ user: UserRow | null } | null>(null);

  const query = new URLSearchParams(Object.entries(filters).filter(([, v]) => v)).toString();
  const users = useQuery({ queryKey: ['users', query], queryFn: () => api<UserRow[]>(`/users${query ? `?${query}` : ''}`) });
  const areas = useQuery({ queryKey: ['areas'], queryFn: () => api<AreaOption[]>('/areas') });
  const toggle = useMutation({
    mutationFn: (u: UserRow) => api(`/users/${u.id}/${u.isActive ? 'deactivate' : 'reactivate'}`, { method: 'POST' }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['users'] }),
  });
  const error = toggle.error instanceof ApiError ? toggle.error.title : toggle.error ? 'Ocurrió un error inesperado.' : null;

  const set = (key: keyof UserFilters) => (e: React.ChangeEvent<HTMLInputElement>) =>
    onFiltersChange({ ...filters, [key]: e.target.value || undefined });

  return (
    <Container sx={{ py: 4 }}>
      <Typography variant="h5" component="h1" gutterBottom>
        Usuarios
      </Typography>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'flex', gap: 2, mb: 2, alignItems: 'center' }}>
        <TextField select size="small" label="Rol" sx={{ minWidth: 150 }} value={filters.role ?? ''} onChange={set('role')}>
          <MenuItem value="">Todos</MenuItem>
          {allRoles.map((r) => (
            <MenuItem key={r} value={r}>
              {r}
            </MenuItem>
          ))}
        </TextField>
        <TextField select size="small" label="Área" sx={{ minWidth: 180 }} value={filters.areaId ?? ''} onChange={set('areaId')}>
          <MenuItem value="">Todas</MenuItem>
          {areas.data?.map((a) => (
            <MenuItem key={a.id} value={a.id}>
              {a.name}
            </MenuItem>
          ))}
        </TextField>
        <TextField select size="small" label="Estado" sx={{ minWidth: 150 }} value={filters.status ?? ''} onChange={set('status')}>
          <MenuItem value="">Todos</MenuItem>
          <MenuItem value="active">Activos</MenuItem>
          <MenuItem value="inactive">Desactivados</MenuItem>
        </TextField>
        <Box sx={{ flexGrow: 1 }} />
        {canWrite && (
          <Button variant="contained" onClick={() => setDialog({ user: null })}>
            Nuevo usuario
          </Button>
        )}
      </Box>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Nombre</TableCell>
            <TableCell>Correo</TableCell>
            <TableCell>Área</TableCell>
            <TableCell>Roles</TableCell>
            <TableCell>Estado</TableCell>
            {canWrite && <TableCell align="right">Acciones</TableCell>}
          </TableRow>
        </TableHead>
        <TableBody>
          {users.data?.map((u) => (
            <TableRow key={u.id}>
              <TableCell>{u.name}</TableCell>
              <TableCell>{u.email}</TableCell>
              <TableCell>{u.areaName}</TableCell>
              <TableCell>{u.roles.join(', ')}</TableCell>
              <TableCell>
                <Chip size="small" color={u.isActive ? 'success' : 'default'} label={u.isActive ? 'Activo' : 'Desactivado'} />
              </TableCell>
              {canWrite && (
                <TableCell align="right">
                  <Button size="small" onClick={() => setDialog({ user: u })}>
                    Editar
                  </Button>
                  <Button size="small" onClick={() => toggle.mutate(u)}>
                    {u.isActive ? 'Desactivar' : 'Reactivar'}
                  </Button>
                </TableCell>
              )}
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {dialog && (
        <UserDialog
          user={dialog.user}
          areas={areas.data ?? []}
          onClose={() => setDialog(null)}
          onSaved={() => {
            setDialog(null);
            void client.invalidateQueries({ queryKey: ['users'] });
          }}
        />
      )}
    </Container>
  );
}
