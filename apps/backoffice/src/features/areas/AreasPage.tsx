import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Container from '@mui/material/Container';
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

type Area = { id: string; name: string; isActive: boolean };

export function AreasPage() {
  const client = useQueryClient();
  const { data: session } = useSession();
  // El Auditor solo consulta; la API lo exige igual.
  const canWrite = hasRole(session, Roles.Administrador);
  const [newName, setNewName] = useState('');
  const [editing, setEditing] = useState<{ id: string; name: string } | null>(null);

  const areas = useQuery({ queryKey: ['areas'], queryFn: () => api<Area[]>('/areas') });
  const run = useMutation({
    mutationFn: (op: () => Promise<unknown>) => op(),
    onSuccess: () => {
      setNewName('');
      setEditing(null);
      return client.invalidateQueries({ queryKey: ['areas'] });
    },
  });
  const error = run.error instanceof ApiError ? run.error.title : run.error ? 'Ocurrió un error inesperado.' : null;

  return (
    <Container sx={{ py: 4 }}>
      <Typography variant="h5" component="h1" gutterBottom>
        Áreas
      </Typography>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {canWrite && (
        <Box sx={{ display: 'flex', gap: 2, mb: 2 }}>
          <TextField size="small" label="Nueva área" value={newName} onChange={(e) => setNewName(e.target.value)} />
          <Button variant="contained" disabled={!newName.trim()} onClick={() => run.mutate(() => api('/areas', { body: { name: newName } }))}>
            Crear
          </Button>
        </Box>
      )}
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Nombre</TableCell>
            <TableCell>Estado</TableCell>
            {canWrite && <TableCell align="right">Acciones</TableCell>}
          </TableRow>
        </TableHead>
        <TableBody>
          {areas.data?.map((a) => (
            <TableRow key={a.id}>
              <TableCell>
                {editing?.id === a.id ? (
                  <TextField size="small" value={editing.name} onChange={(e) => setEditing({ id: a.id, name: e.target.value })} />
                ) : (
                  a.name
                )}
              </TableCell>
              <TableCell>
                <Chip size="small" color={a.isActive ? 'success' : 'default'} label={a.isActive ? 'Activa' : 'Inactiva'} />
              </TableCell>
              {canWrite && (
                <TableCell align="right">
                  {editing?.id === a.id ? (
                    <Button size="small" onClick={() => run.mutate(() => api(`/areas/${a.id}`, { method: 'PUT', body: { name: editing.name } }))}>
                      Guardar
                    </Button>
                  ) : (
                    <Button size="small" onClick={() => setEditing({ id: a.id, name: a.name })}>
                      Renombrar
                    </Button>
                  )}
                  <Button
                    size="small"
                    onClick={() => run.mutate(() => api(`/areas/${a.id}/${a.isActive ? 'deactivate' : 'reactivate'}`, { method: 'POST' }))}
                  >
                    {a.isActive ? 'Desactivar' : 'Reactivar'}
                  </Button>
                </TableCell>
              )}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Container>
  );
}
