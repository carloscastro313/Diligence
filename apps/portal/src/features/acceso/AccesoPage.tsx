import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, clearToken, codigoPedido, getToken, marcarCodigoPedido, saveToken } from '../../shared';

const SESION_VENCIDA = 'Tu sesión venció. Volvé a ingresar con el enlace y un código nuevo; lo guardado se conserva.';
const ENLACE_INVALIDO = 'El enlace no es válido o venció. Comunicate con quien te pidió el alta.';

export function AccesoPage({ onSuccess, vencida }: { onSuccess: () => void; vencida?: boolean }) {
  const [hayToken, setHayToken] = useState(() => !!getToken());
  const [enlaceInvalido, setEnlaceInvalido] = useState(false);
  const [correo, setCorreo] = useState<string | null>(null);
  const [codigo, setCodigo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [espera, setEspera] = useState(0);
  const [ocupado, setOcupado] = useState(false);
  const iniciado = useRef(false);

  function fallo(e: unknown) {
    if (!(e instanceof ApiError)) return setError('Ocurrió un error inesperado.');
    if (e.status === 404) {
      clearToken();
      setHayToken(false);
      setEnlaceInvalido(true);
    } else if (e.status === 401) {
      setCodigo('');
      setError(
        e.extras['codigoInvalidado']
          ? 'El código no es válido y ya no se puede usar. Pedí uno nuevo con "Reenviar código".'
          : 'El código no es válido.',
      );
    } else if (e.status === 429) {
      setEspera(e.retryAfter ?? 0);
      setError('Hiciste demasiados intentos. Esperá un momento antes de volver a intentar.');
    } else if (e.status === 503) setError('No pudimos enviarte el correo. Probá de nuevo con "Reenviar código".');
    else setError(e.title);
  }

  async function pedirCodigo() {
    setError(null);
    setOcupado(true);
    try {
      const r = await api<{ correo: string }>('/acceso/codigo', { body: { token: getToken() } });
      setCorreo(r.correo);
      setCodigo('');
    } catch (e) {
      fallo(e);
    } finally {
      setOcupado(false);
    }
  }

  useEffect(() => {
    // El ref evita el doble montaje de StrictMode; la marca en sessionStorage, reenviar al recargar.
    if (iniciado.current) return;
    iniciado.current = true;
    const token = new URLSearchParams(location.hash.slice(1)).get('t');
    if (token) {
      saveToken(token);
      history.replaceState(null, '', location.pathname + location.search);
      setHayToken(true);
    }
    if (getToken() && !codigoPedido()) {
      marcarCodigoPedido();
      void pedirCodigo();
    }
  }, []);

  useEffect(() => {
    if (espera <= 0) return;
    const t = setTimeout(() => setEspera((s) => s - 1), 1000);
    return () => clearTimeout(t);
  }, [espera]);

  async function ingresar(ev: FormEvent) {
    ev.preventDefault();
    if (!/^\d{6}$/.test(codigo)) return setError('El código tiene 6 dígitos.');
    setError(null);
    setOcupado(true);
    try {
      await api('/acceso', { body: { token: getToken(), codigo } });
      onSuccess();
    } catch (e) {
      fallo(e);
      setOcupado(false);
    }
  }

  return (
    <Container maxWidth="xs" sx={{ py: 4 }}>
      <Typography variant="h5" component="h1" gutterBottom>
        Ingresá a tu trámite
      </Typography>
      {vencida && <Alert severity="warning" sx={{ mb: 2 }}>{SESION_VENCIDA}</Alert>}
      {enlaceInvalido && <Alert severity="error">{ENLACE_INVALIDO}</Alert>}
      {!hayToken && !enlaceInvalido && <Alert severity="info">Abrí el enlace que recibiste por correo.</Alert>}
      {hayToken && (
        <form onSubmit={ingresar} noValidate style={{ display: 'grid', gap: 16 }}>
          <Typography>{correo ? `Te enviamos un código a ${correo}.` : 'Ingresá el código que te enviamos por correo.'}</Typography>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField
            label="Código de 6 dígitos"
            value={codigo}
            onChange={(e) => setCodigo(e.target.value.trim())}
            slotProps={{ htmlInput: { inputMode: 'numeric', autoComplete: 'one-time-code', maxLength: 6 } }}
          />
          <Button type="submit" variant="contained" size="large" disabled={ocupado}>
            Ingresar
          </Button>
          <Button onClick={pedirCodigo} disabled={ocupado || espera > 0}>
            Reenviar código{espera > 0 && ` (${espera} s)`}
          </Button>
        </form>
      )}
    </Container>
  );
}
