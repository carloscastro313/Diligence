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
import { useQuery } from '@tanstack/react-query';
import { api } from '../../shared';

export type SolicitudFilters = { estado?: string; q?: string };

type MiSolicitud = {
  casoId: string;
  cuit: string;
  razonSocial: string;
  tipoTercero: string;
  estado: string;
  creadaUtc: string;
  esOriginal: boolean;
  fechaEstimada?: string | null;
  cerradoUtc?: string | null;
  enlaceNoEntregado: boolean;
};

const estados: Record<string, string> = {
  EsperaTercero: 'Espera del tercero',
  EnVerificacion: 'En verificación',
  EnAprobacion: 'En aprobación',
  Aprobado: 'Aprobado',
  Rechazado: 'Rechazado',
  Abandonado: 'Abandonado',
};
const finales = ['Aprobado', 'Rechazado', 'Abandonado'];
const fecha = (iso: string) => new Date(iso).toLocaleDateString('es-AR', { timeZone: 'UTC' });

function resultado(s: MiSolicitud) {
  if (finales.includes(s.estado)) return `Resultado: ${estados[s.estado]}${s.cerradoUtc ? ` el ${fecha(s.cerradoUtc)}` : ''}`;
  return s.fechaEstimada ? fecha(s.fechaEstimada) : '';
}

export function SolicitudesPage({
  filters,
  onFiltersChange,
  onNueva,
}: {
  filters: SolicitudFilters;
  onFiltersChange: (f: SolicitudFilters) => void;
  onNueva: () => void;
}) {
  const query = new URLSearchParams(Object.entries(filters).filter(([, v]) => v) as [string, string][]).toString();
  const { data } = useQuery({ queryKey: ['solicitudes', query], queryFn: () => api<MiSolicitud[]>(`/solicitudes${query ? `?${query}` : ''}`) });
  const set = (key: keyof SolicitudFilters) => (e: React.ChangeEvent<HTMLInputElement>) =>
    onFiltersChange({ ...filters, [key]: e.target.value || undefined });

  return (
    <Container sx={{ py: 4 }}>
      <Typography variant="h5" component="h1" gutterBottom>
        Mis solicitudes
      </Typography>
      <Box sx={{ display: 'flex', gap: 2, mb: 2, alignItems: 'center' }}>
        <TextField select size="small" label="Estado" sx={{ minWidth: 200 }} value={filters.estado ?? ''} onChange={set('estado')}>
          <MenuItem value="">Todos</MenuItem>
          {Object.entries(estados).map(([k, v]) => (
            <MenuItem key={k} value={k}>
              {v}
            </MenuItem>
          ))}
        </TextField>
        <TextField size="small" label="Buscar" value={filters.q ?? ''} onChange={set('q')} />
        <Box sx={{ flexGrow: 1 }} />
        <Button variant="contained" onClick={onNueva}>
          Nueva solicitud
        </Button>
      </Box>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Razón social</TableCell>
            <TableCell>CUIT</TableCell>
            <TableCell>Tipo</TableCell>
            <TableCell>Estado</TableCell>
            <TableCell>Solicitada</TableCell>
            <TableCell>Fecha estimada / resultado</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {data?.map((s) => (
            <TableRow key={s.casoId}>
              <TableCell>
                {s.razonSocial} {!s.esOriginal && <Chip size="small" label="Sumado" />}
              </TableCell>
              <TableCell>{s.cuit}</TableCell>
              <TableCell>{s.tipoTercero}</TableCell>
              <TableCell>
                {estados[s.estado] ?? s.estado}
                {s.enlaceNoEntregado && (
                  <Alert severity="warning" sx={{ mt: 1 }}>
                    Enlace no entregado
                  </Alert>
                )}
              </TableCell>
              <TableCell>{fecha(s.creadaUtc)}</TableCell>
              <TableCell>{resultado(s)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Container>
  );
}
