import { zodResolver } from '@hookform/resolvers/zod';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Link from '@mui/material/Link';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { api, ApiError } from '../../shared';

const schema = z.object({ email: z.string().trim().email('Ingresa un correo válido.') });
type Values = z.infer<typeof schema>;

export function ForgotPasswordPage() {
  const [sent, setSent] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Values>({ resolver: zodResolver(schema) });

  const submit = handleSubmit(async (values) => {
    setServerError(null);
    try {
      await api('/auth/password/forgot', { body: values });
      setSent(true);
    } catch (e) {
      setServerError(e instanceof ApiError ? e.title : 'Ocurrió un error inesperado.');
    }
  });

  return (
    <Container maxWidth="xs" sx={{ py: 8 }}>
      <Typography variant="h5" component="h1" gutterBottom>
        Recuperar acceso
      </Typography>
      {sent ? (
        // Mismo mensaje exista o no el correo: no se revela qué cuentas existen.
        <Alert severity="info">Si el correo corresponde a una cuenta activa, te enviamos un enlace para restablecer tu contraseña.</Alert>
      ) : (
        <Box component="form" onSubmit={submit} noValidate sx={{ display: 'grid', gap: 2 }}>
          {serverError && <Alert severity="error">{serverError}</Alert>}
          <TextField
            label="Correo"
            type="email"
            autoComplete="username"
            error={!!errors.email}
            helperText={errors.email?.message}
            {...register('email')}
          />
          <Button type="submit" variant="contained" disabled={isSubmitting}>
            Enviar enlace
          </Button>
        </Box>
      )}
      <Link href="/login" variant="body2" sx={{ display: 'inline-block', mt: 2 }}>
        Volver a ingresar
      </Link>
    </Container>
  );
}
