import Alert from '@mui/material/Alert';
import Container from '@mui/material/Container';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import Typography from '@mui/material/Typography';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../shared';
import { DocumentosSection, type Documentos } from './DocumentosSection';
import { FormularioSection, type Formulario } from './FormularioSection';

type Caso = { razonSocial: string; cuit: string; tipoPersona: string; estado: string; editable: boolean; pendientes: string[] };

const estados: Record<string, string> = {
  EsperaTercero: 'Espera del tercero',
  EnVerificacion: 'En verificación',
  EnAprobacion: 'En aprobación',
  Aprobado: 'Aprobado',
  Rechazado: 'Rechazado',
  Abandonado: 'Abandonado',
};

export function CasoPage() {
  const client = useQueryClient();
  const caso = useQuery({ queryKey: ['caso'], queryFn: () => api<Caso>('/caso') });
  const documentos = useQuery({ queryKey: ['documentos'], queryFn: () => api<Documentos>('/caso/documentos') });
  const formulario = useQuery({ queryKey: ['formulario'], queryFn: () => api<Formulario>('/caso/formulario') });
  // Subir o eliminar cambia también la lista de pendientes del caso.
  const recargar = () => Promise.all([client.invalidateQueries({ queryKey: ['documentos'] }), client.invalidateQueries({ queryKey: ['caso'] })]);

  if (caso.isError) return <Alert severity="error" sx={{ m: 2 }}>No pudimos cargar tu trámite. Probá de nuevo más tarde.</Alert>;
  if (!caso.data) return <Typography sx={{ p: 2 }}>Cargando…</Typography>;
  const c = caso.data;

  return (
    <Container maxWidth="md" sx={{ py: 4 }}>
      <Typography variant="h5" component="h1">
        {c.razonSocial}
      </Typography>
      <Typography color="text.secondary">
        CUIT {c.cuit} · Estado: {estados[c.estado] ?? c.estado}
      </Typography>
      {c.pendientes.length > 0 && (
        <>
          <Typography variant="h6" component="h2" sx={{ mt: 3 }}>
            Te falta
          </Typography>
          <List dense>
            {c.pendientes.map((p) => (
              <ListItem key={p}>{p}</ListItem>
            ))}
          </List>
        </>
      )}
      {documentos.data && <DocumentosSection datos={documentos.data} readOnly={!c.editable} onChanged={() => void recargar()} />}
      {formulario.data && (
        <FormularioSection
          caso={c}
          formulario={formulario.data}
          onEnviado={(f) => {
            client.setQueryData(['formulario'], f);
            void recargar();
          }}
        />
      )}
    </Container>
  );
}
