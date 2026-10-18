import { zodResolver } from '@hookform/resolvers/zod';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { api, ApiError } from '../../shared';

// Mismos límites que la API (12 a 64 caracteres); la API decide, esto solo da respuesta inmediata.
const schema = z
  .object({
    newPassword: z.string().min(12, 'Usa al menos 12 caracteres.').max(64, 'Usa como máximo 64 caracteres.'),
    confirm: z.string(),
  })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'Las contraseñas no coinciden.' });
type Values = z.infer<typeof schema>;

export function ResetPasswordPage({ token: initialToken, onDone }: { token: string; onDone: () => void }) {
  // El token se guarda en estado y se quita de la URL para que no quede en el historial ni en capturas.
  const [token] = useState(initialToken);
  useEffect(() => {
    history.replaceState(null, '', window.location.pathname);
  }, []);

  const [serverError, setServerError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Values>({ resolver: zodResolver(schema) });

  const submit = handleSubmit(async ({ newPassword }) => {
    setServerError(null);
    try {
      await api('/auth/password/reset', { body: { token, newPassword } });
      setDone(true);
    } catch (e) {
      setServerError(e instanceof ApiError ? (Object.values(e.errors)[0]?.[0] ?? e.title) : 'Ocurrió un error inesperado.');
    }
  });

  return (
    <Container maxWidth="xs" sx={{ py: 8 }}>
      <Typography variant="h5" component="h1" gutterBottom>
        Establecer contraseña
      </Typography>
      {done ? (
        <Box sx={{ display: 'grid', gap: 2 }}>
          <Alert severity="success">Tu contraseña quedó establecida.</Alert>
          <Button variant="contained" onClick={onDone}>
            Ir a ingresar
          </Button>
        </Box>
      ) : (
        <Box component="form" onSubmit={submit} noValidate sx={{ display: 'grid', gap: 2 }}>
          {!token && <Alert severity="error">El enlace no es válido.</Alert>}
          {serverError && <Alert severity="error">{serverError}</Alert>}
          <TextField
            label="Nueva contraseña"
            type="password"
            autoComplete="new-password"
            error={!!errors.newPassword}
            helperText={errors.newPassword?.message}
            {...register('newPassword')}
          />
          <TextField
            label="Repite la contraseña"
            type="password"
            autoComplete="new-password"
            error={!!errors.confirm}
            helperText={errors.confirm?.message}
            {...register('confirm')}
          />
          <Button type="submit" variant="contained" disabled={isSubmitting || !token}>
            Guardar contraseña
          </Button>
        </Box>
      )}
    </Container>
  );
}
