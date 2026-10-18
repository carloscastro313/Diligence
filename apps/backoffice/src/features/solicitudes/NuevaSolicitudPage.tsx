import { zodResolver } from '@hookform/resolvers/zod';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { api, ApiError } from '../../shared';
import { cuitMatchesPersona, isValidCuit } from './cuit';

// Zod solo da respuesta inmediata: la API decide siempre.
const schema = z
  .object({
    tipoTercero: z.enum(['Cliente', 'Proveedor']),
    tipoPersona: z.enum(['Humana', 'Juridica']),
    cuit: z.string().trim().min(1, 'El CUIT es obligatorio.').max(20),
    razonSocial: z.string().trim().min(1, 'Este dato es obligatorio.').max(200),
    contactoNombre: z.string().trim().min(1, 'El contacto es obligatorio.').max(200),
    contactoEmail: z.string().trim().email('Ingresa un correo válido.').max(254),
    contactoTelefono: z.string().trim().max(50),
    montoAnualEstimado: z.string().refine((v) => Number(v.replace(',', '.')) > 0, 'Ingresa un monto mayor a cero.'),
    motivo: z.string().trim().min(1, 'El motivo es obligatorio.').max(1000),
  })
  .superRefine((v, ctx) => {
    if (!v.cuit) return;
    if (!isValidCuit(v.cuit)) ctx.addIssue({ code: 'custom', path: ['cuit'], message: 'El CUIT no es válido.' });
    else if (!cuitMatchesPersona(v.cuit, v.tipoPersona))
      ctx.addIssue({ code: 'custom', path: ['cuit'], message: 'El CUIT no corresponde al tipo de persona.' });
  });
type Values = z.infer<typeof schema>;
const fields = Object.keys(schema.shape) as (keyof Values)[];

type Aviso = {
  existe: boolean;
  razonSocial?: string | null;
  estadoTercero?: string | null;
  etapaCasoAbierto?: string | null;
  yaLoSigue?: boolean;
  admiteSolicitud?: boolean;
};

export function NuevaSolicitudPage({ onCreated }: { onCreated: (casoId: string) => void }) {
  const [serverError, setServerError] = useState<string | null>(null);
  const [sumado, setSumado] = useState<string | null>(null);
  const [debouncedCuit, setDebouncedCuit] = useState('');
  const {
    register,
    watch,
    setError,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      tipoTercero: 'Proveedor',
      tipoPersona: 'Juridica',
      cuit: '',
      razonSocial: '',
      contactoNombre: '',
      contactoEmail: '',
      contactoTelefono: '',
      montoAnualEstimado: '',
      motivo: '',
    },
  });
  const humana = watch('tipoPersona') === 'Humana';
  const cuit = watch('cuit');
  useEffect(() => {
    const t = setTimeout(() => setDebouncedCuit(cuit), 400);
    return () => clearTimeout(t);
  }, [cuit]);
  const aviso = useQuery({
    queryKey: ['tercero-existente', debouncedCuit],
    queryFn: () => api<Aviso>(`/solicitudes/tercero-existente?cuit=${encodeURIComponent(debouncedCuit)}`),
    enabled: isValidCuit(debouncedCuit),
  });
  const existente = aviso.data?.existe ? aviso.data : null;
  // Solo comodidad: la API decide siempre.
  const bloqueado = existente?.admiteSolicitud === false;

  const submit = handleSubmit(async (v) => {
    setServerError(null);
    try {
      const body = { ...v, contactoTelefono: v.contactoTelefono || null, montoAnualEstimado: Number(v.montoAnualEstimado.replace(',', '.')) };
      const res = await api<{ casoId: string; resultado?: string }>('/solicitudes', { method: 'POST', body });
      if (res.resultado === 'SumadoACasoAbierto') setSumado(res.casoId);
      else onCreated(res.casoId);
    } catch (e) {
      if (!(e instanceof ApiError)) return setServerError('Ocurrió un error inesperado.');
      if (e.status !== 400) return setServerError(e.title);
      // Las claves del ProblemDetails pueden venir en PascalCase o camelCase.
      let matched = false;
      for (const [key, msgs] of Object.entries(e.errors)) {
        const field = fields.find((f) => f.toLowerCase() === key.toLowerCase());
        if (field) {
          setError(field, { message: msgs.join(' ') });
          matched = true;
        }
      }
      if (!matched) setServerError(e.title);
    }
  });

  const text = (name: keyof Values, label: string, extra: object = {}) => (
    <TextField label={label} error={!!errors[name]} helperText={errors[name]?.message} {...register(name)} {...extra} />
  );

  return (
    <Container maxWidth="sm" sx={{ py: 4 }}>
      <Typography variant="h5" component="h1" gutterBottom>
        Nueva solicitud
      </Typography>
      <form onSubmit={submit} noValidate style={{ display: 'grid', gap: 16 }}>
        {serverError && <Alert severity="error">{serverError}</Alert>}
        <TextField select label="Tipo de tercero" value={watch('tipoTercero')} {...register('tipoTercero')}>
          <MenuItem value="Cliente">Cliente</MenuItem>
          <MenuItem value="Proveedor">Proveedor</MenuItem>
        </TextField>
        <TextField select label="Tipo de persona" value={watch('tipoPersona')} {...register('tipoPersona')}>
          <MenuItem value="Humana">Humana</MenuItem>
          <MenuItem value="Juridica">Jurídica</MenuItem>
        </TextField>
        {sumado && (
          <Alert severity="success" action={<Button onClick={() => onCreated(sumado)}>Ver mis solicitudes</Button>}>
            Te sumaste al caso existente.
          </Alert>
        )}
        {text('cuit', 'CUIT')}
        {existente && (
          <Alert severity={bloqueado ? 'warning' : 'info'}>
            {existente.razonSocial} — estado: {existente.estadoTercero}
            {existente.etapaCasoAbierto && `, etapa del caso: ${existente.etapaCasoAbierto}`}.
            {existente.yaLoSigue && ' Ya seguís este caso.'}
            {bloqueado && !existente.yaLoSigue && ' No se admiten nuevas solicitudes para este tercero.'}
          </Alert>
        )}
        {text('razonSocial', humana ? 'Nombre y apellido' : 'Razón social')}
        {text('contactoNombre', 'Nombre del contacto')}
        {text('contactoEmail', 'Correo del contacto', { type: 'email' })}
        {text('contactoTelefono', 'Teléfono del contacto')}
        {text('montoAnualEstimado', 'Monto anual estimado (ARS)', { inputMode: 'decimal' })}
        {text('motivo', 'Motivo', { multiline: true, minRows: 3 })}
        <Button type="submit" variant="contained" disabled={isSubmitting || bloqueado || !!sumado}>
          Enviar solicitud
        </Button>
      </form>
    </Container>
  );
}
