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
import { api, ApiError, refreshSession } from '../../shared';

// Zod solo da respuesta inmediata en el formulario; la API revalida todo.
const schema = z.object({
  email: z.string().trim().min(1, 'Ingresa tu correo.'),
  password: z.string().min(1, 'Ingresa tu contraseña.'),
});
type Values = z.infer<typeof schema>;

export function LoginPage({ onSuccess }: { onSuccess: () => void }) {
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Values>({ resolver: zodResolver(schema) });

  const submit = handleSubmit(async (values) => {
    setServerError(null);
    try {
      await api('/auth/login', { body: values });
      await refreshSession();
      onSuccess();
    } catch (e) {
      setServerError(e instanceof ApiError ? e.title : 'Ocurrió un error inesperado.');
    }
  });

  return (
    <Container maxWidth="xs" sx={{ py: 8 }}>
      <Typography variant="h5" component="h1" gutterBottom>
        Ingresar
      </Typography>
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
        <TextField
          label="Contraseña"
          type="password"
          autoComplete="current-password"
          error={!!errors.password}
          helperText={errors.password?.message}
          {...register('password')}
        />
        <Button type="submit" variant="contained" disabled={isSubmitting}>
          Ingresar
        </Button>
        <Link href="/olvide" variant="body2">
          Olvidé mi contraseña
        </Link>
      </Box>
    </Container>
  );
}
