import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { useDropzone, type FileRejection } from 'react-dropzone';
import { api, ApiError } from '../../shared';

export type DocumentoCargado = { id: string; nombreOriginal: string; contentType: string; tamanoBytes: number; cargadoUtc: string };
export type DocumentoRequerido = { tipo: string; nombre: string; obligatorio: boolean; cargado: DocumentoCargado | null };
export type Documentos = { tamanoMaximoBytes: number; formatos: string[]; documentos: DocumentoRequerido[] };

const accept = { 'application/pdf': ['.pdf'], 'image/jpeg': ['.jpg', '.jpeg'], 'image/png': ['.png'] };
const mb = (bytes: number) => Number((bytes / 1024 / 1024).toFixed(1));

function rechazo(r: readonly FileRejection[], datos: Documentos) {
  const codes = r[0]?.errors.map((e) => e.code) ?? [];
  if (codes.includes('file-invalid-type')) return `Formatos aceptados: ${datos.formatos.join(', ')}`;
  if (codes.includes('file-too-large')) return `El máximo es ${mb(datos.tamanoMaximoBytes)} MB`;
  return codes.length ? 'Subí un solo archivo.' : null;
}

function Zona({ doc, datos, readOnly, onChanged }: { doc: DocumentoRequerido; datos: Documentos; readOnly: boolean; onChanged: () => void }) {
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);

  async function ejecutar(accion: () => Promise<unknown>) {
    setError(null);
    setOcupado(true);
    try {
      await accion();
      onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? e.title : 'Ocurrió un error inesperado.');
    } finally {
      setOcupado(false);
    }
  }

  const { getRootProps, getInputProps, fileRejections, isDragActive, open } = useDropzone({
    accept,
    maxFiles: 1,
    multiple: false,
    maxSize: datos.tamanoMaximoBytes,
    disabled: readOnly || ocupado,
    // Los botones abren el selector: así el cartel y el reemplazo comparten el mismo flujo.
    noClick: true,
    noKeyboard: true,
    onDropAccepted: ([archivo]) =>
      void ejecutar(() => {
        const form = new FormData();
        form.append('archivo', archivo!);
        return api(`/caso/documentos/${doc.tipo}`, { method: 'PUT', body: form });
      }),
  });
  const cargado = doc.cargado;
  const aviso = error ?? rechazo(fileRejections, datos);

  return (
    <Box
      {...getRootProps()}
      sx={{ border: 2, borderStyle: 'dashed', borderColor: isDragActive ? 'primary.main' : 'divider', borderRadius: 1, p: 2, display: 'grid', gap: 1 }}
    >
      {!readOnly && <input {...getInputProps({ 'aria-label': `Archivo: ${doc.nombre}` })} />}
      <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography component="h3" variant="subtitle1">
          {doc.nombre}
        </Typography>
        <Chip size="small" label={doc.obligatorio ? 'Obligatorio' : 'Opcional'} />
      </Box>
      {cargado ? (
        <Typography>
          {cargado.nombreOriginal} · {mb(cargado.tamanoBytes)} MB · {new Date(cargado.cargadoUtc).toLocaleDateString('es-AR')}
        </Typography>
      ) : (
        <Typography color="text.secondary">{readOnly ? 'No se cargó.' : 'Arrastrá el archivo acá o elegilo desde tu dispositivo.'}</Typography>
      )}
      {!readOnly && (
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
          <Button variant={cargado ? 'outlined' : 'contained'} sx={{ minHeight: 44 }} disabled={ocupado} onClick={open}>
            {cargado ? 'Reemplazar' : 'Elegir archivo'}
          </Button>
          {cargado && (
            <Button color="error" sx={{ minHeight: 44 }} disabled={ocupado} onClick={() => void ejecutar(() => api(`/caso/documentos/${doc.tipo}`, { method: 'DELETE' }))}>
              Eliminar
            </Button>
          )}
        </Box>
      )}
      {ocupado && <LinearProgress aria-label="Subiendo" />}
      {aviso && <Alert severity="error">{aviso}</Alert>}
    </Box>
  );
}

export function DocumentosSection({ datos, readOnly, onChanged }: { datos: Documentos; readOnly: boolean; onChanged: () => void }) {
  return (
    <Box component="section" sx={{ display: 'grid', gap: 2, mt: 3 }}>
      <Typography variant="h6" component="h2">
        Documentos
      </Typography>
      <Typography color="text.secondary">
        Formatos aceptados: {datos.formatos.join(', ')}. Tamaño máximo: {mb(datos.tamanoMaximoBytes)} MB.
      </Typography>
      {datos.documentos.map((d) => (
        <Zona key={d.tipo} doc={d} datos={datos} readOnly={readOnly} onChanged={onChanged} />
      ))}
    </Box>
  );
}
