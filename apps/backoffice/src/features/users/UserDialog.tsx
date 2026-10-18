import { zodResolver } from '@hookform/resolvers/zod';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormGroup from '@mui/material/FormGroup';
import FormHelperText from '@mui/material/FormHelperText';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { z } from 'zod';
import { api, ApiError } from '../../shared';

export const allRoles = ['Solicitante', 'Analista', 'Aprobador', 'Administrador', 'Auditor'];
export type AreaOption = { id: string; name: string; isActive: boolean };
export type UserRow = {
  id: string;
  name: string;
  email: string;
  areaId: string;
  areaName: string;
  roles: string[];
  isActive: boolean;
  hasPassword: boolean;
};

// Zod solo da respuesta inmediata: unicidad, área activa y último administrador los decide la API.
const schema = z.object({
  name: z.string().trim().min(1, 'El nombre es obligatorio.'),
  email: z.string().trim().email('Ingresa un correo válido.'),
  areaId: z.string().min(1, 'Selecciona un área.'),
  roles: z.array(z.string()).min(1, 'Selecciona al menos un rol.'),
});
type Values = z.infer<typeof schema>;

export function UserDialog({
  user,
  areas,
  onClose,
  onSaved,
}: {
  user: UserRow | null; // null = alta
  areas: AreaOption[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: user?.name ?? '', email: user?.email ?? '', areaId: user?.areaId ?? '', roles: user?.roles ?? [] },
  });

  // Solo áreas activas, más la actual del usuario aunque ya esté desactivada (se conserva si no la cambia).
  const options = areas.filter((a) => a.isActive || a.id === user?.areaId);

  const submit = handleSubmit(async (values) => {
    setServerError(null);
    try {
      await api(user ? `/users/${user.id}` : '/users', { method: user ? 'PUT' : 'POST', body: values });
      onSaved();
    } catch (e) {
      setServerError(e instanceof ApiError ? e.title : 'Ocurrió un error inesperado.');
    }
  });

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={submit} noValidate>
        <DialogTitle>{user ? 'Editar usuario' : 'Nuevo usuario'}</DialogTitle>
        <DialogContent sx={{ display: 'grid', gap: 2, pt: 1 }}>
          {serverError && <Alert severity="error">{serverError}</Alert>}
          <TextField label="Nombre" error={!!errors.name} helperText={errors.name?.message} {...register('name')} />
          <TextField label="Correo" type="email" error={!!errors.email} helperText={errors.email?.message} {...register('email')} />
          <Controller
            control={control}
            name="areaId"
            render={({ field }) => (
              <TextField select label="Área" error={!!errors.areaId} helperText={errors.areaId?.message} {...field}>
                {options.map((a) => (
                  <MenuItem key={a.id} value={a.id}>
                    {a.name}
                  </MenuItem>
                ))}
              </TextField>
            )}
          />
          <Controller
            control={control}
            name="roles"
            render={({ field }) => (
              <div>
                <FormGroup row>
                  {allRoles.map((role) => (
                    <FormControlLabel
                      key={role}
                      label={role}
                      control={
                        <Checkbox
                          checked={field.value.includes(role)}
                          onChange={(e) =>
                            field.onChange(e.target.checked ? [...field.value, role] : field.value.filter((r) => r !== role))
                          }
                        />
                      }
                    />
                  ))}
                </FormGroup>
                {errors.roles && <FormHelperText error>{errors.roles.message}</FormHelperText>}
              </div>
            )}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancelar</Button>
          <Button type="submit" variant="contained" disabled={isSubmitting}>
            Guardar
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
