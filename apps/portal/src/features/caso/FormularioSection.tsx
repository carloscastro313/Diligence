import { zodResolver } from '@hookform/resolvers/zod';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useBlocker } from '@tanstack/react-router';
import { createContext, useContext, useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { FormProvider, get, useFieldArray, useForm, useFormContext, useWatch, type FieldValues, type Resolver } from 'react-hook-form';
import { api, ApiError, PAISES } from '../../shared';
import { formularioSchema, type TipoPersona } from './schema';

export type Formulario = { estado: 'Borrador' | 'Enviado'; datos: FieldValues; version?: string; guardadoUtc?: string; enviadoUtc?: string };
type Caso = { cuit: string; razonSocial: string; tipoPersona: string; editable: boolean };
type Aviso = { severity: 'success' | 'warning' | 'error'; textos: string[] };

const completo = { gridColumn: '1 / -1' };
const IVA = [
  ['ResponsableInscripto', 'Responsable inscripto'],
  ['Monotributo', 'Monotributo'],
  ['Exento', 'Exento'],
  ['NoAlcanzado', 'No alcanzado'],
  ['ConsumidorFinal', 'Consumidor final'],
] as const;
const PAIS_OPCIONES = PAISES.map((p) => [p.codigo, p.nombre] as const);
// Campos sueltos que tienen su propio input; cualquier otro error sin ruta anidada (documentos, beneficiarios…) va como Alert.
const CAMPOS_SIMPLES = new Set(['condicionIva', 'fechaConstitucion', 'numeroInscripcion', 'fechaNacimiento', 'nacionalidad', 'origenFondos']);
const esCampo = (clave: string) => /[.[]/.test(clave) || CAMPOS_SIMPLES.has(clave);
const siempre = () => true;

// El servidor valida formato (fechas, países ISO): los opcionales vacíos se omiten en vez de mandar "".
function limpiar(v: unknown): unknown {
  if (typeof v === 'string') return v.trim() || undefined;
  if (typeof v === 'number') return Number.isNaN(v) ? undefined : v;
  if (Array.isArray(v)) {
    const lista = v.map(limpiar).filter((x) => x !== undefined);
    return lista.length ? lista : undefined;
  }
  if (v && typeof v === 'object') {
    const obj = Object.fromEntries(Object.entries(v).map(([k, x]) => [k, limpiar(x)] as const).filter(([, x]) => x !== undefined));
    return Object.keys(obj).length ? obj : undefined;
  }
  return v ?? undefined;
}
// Excepción a la regla de omitir vacíos: la declaración "sin beneficiarios" se persiste aunque la explicación aún esté vacía (el envío exige 20–1000).
const payload = (v: FieldValues) =>
  ({ ...(limpiar(v) as object), ...(v.sinBeneficiarios && { sinBeneficiarios: { explicacion: String(v.sinBeneficiarios.explicacion ?? '').trim() } }) }) as FieldValues;

const Lectura = createContext(false);

function Grupo({ titulo, anidado, children }: { titulo: string; anidado?: boolean; children: ReactNode }) {
  return (
    <Box
      component="fieldset"
      sx={{
        m: 0,
        minWidth: 0,
        p: anidado ? 2 : 0,
        border: anidado ? 1 : 0,
        borderColor: 'divider',
        borderRadius: 1,
        display: 'grid',
        gap: 2,
        gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' },
        ...(anidado && completo),
      }}
    >
      <Typography component="legend" variant={anidado ? 'subtitle1' : 'h6'} sx={{ p: 0, mb: 1 }}>
        {titulo}
      </Typography>
      {children}
    </Box>
  );
}

type CampoProps = { name: string; label: string; type?: string; rows?: number; opciones?: readonly (readonly [string, string])[]; descartable?: boolean; ancho?: boolean };

// Un input registrado (texto, fecha, número, área de texto o select nativo) con su error.
function Campo({ name, label, type, rows, opciones, descartable, ancho }: CampoProps) {
  const { register, formState } = useFormContext();
  const lectura = useContext(Lectura);
  const error = get(formState.errors, name) as { message?: string } | undefined;
  // Mismos elementos entre renders: React no vuelve a recorrer las 249 opciones de país en cada tecla.
  const nodos = useMemo(
    () => opciones && [<option key="" value="" />, ...opciones.map(([valor, texto]) => <option key={valor} value={valor}>{texto}</option>)],
    [opciones],
  );
  const { ref, ...campo } = register(name, { valueAsNumber: type === 'number', shouldUnregister: descartable });
  return (
    <TextField
      {...campo}
      inputRef={ref}
      label={label}
      type={opciones ? undefined : type}
      select={!!opciones}
      multiline={!!rows}
      minRows={rows}
      disabled={lectura}
      error={!!error}
      helperText={error?.message}
      sx={ancho ? completo : undefined}
      slotProps={{
        inputLabel: type === 'date' || opciones ? { shrink: true } : undefined,
        select: opciones ? { native: true } : undefined,
        htmlInput: type === 'number' ? { step: '0.01', min: 0 } : undefined,
      }}
    >
      {nodos}
    </TextField>
  );
}

function Casilla({ name, label }: { name: string; label: string }) {
  const { register } = useFormContext();
  const lectura = useContext(Lectura);
  const { ref, ...campo } = register(name);
  return <FormControlLabel sx={{ minHeight: 44, ...completo }} label={label} disabled={lectura} control={<Checkbox {...campo} inputRef={ref} />} />;
}

function ErrorLista({ name }: { name: string }) {
  const { formState } = useFormContext();
  const error = get(formState.errors, name) as { message?: string; root?: { message?: string } } | undefined;
  const mensaje = error?.message ?? error?.root?.message;
  return mensaje ? <Alert severity="error" sx={completo}>{mensaje}</Alert> : null;
}

function Pep({ name }: { name: string }) {
  const esPep = useWatch({ name: `${name}.esPep` });
  return (
    <>
      <Casilla name={`${name}.esPep`} label="Es una persona expuesta políticamente (PEP)" />
      {esPep && (
        <>
          <Campo name={`${name}.cargo`} label="Cargo" descartable />
          <Campo name={`${name}.organismo`} label="Organismo" descartable />
        </>
      )}
    </>
  );
}

function Domicilio({ name, descartable }: { name: string; descartable?: boolean }) {
  const c = (campo: string, label: string, opciones?: CampoProps['opciones']) => (
    <Campo name={`${name}.${campo}`} label={label} opciones={opciones} descartable={descartable} />
  );
  return (
    <>
      {c('calle', 'Calle')}
      {c('numero', 'Número')}
      {c('piso', 'Piso (opcional)')}
      {c('depto', 'Departamento (opcional)')}
      {c('localidad', 'Localidad')}
      {c('provincia', 'Provincia')}
      {c('codigoPostal', 'Código postal')}
      {c('pais', 'País', PAIS_OPCIONES)}
    </>
  );
}

function Acciones({ children }: { children: ReactNode }) {
  return <Box sx={{ ...completo, display: 'flex', gap: 1, flexWrap: 'wrap' }}>{children}</Box>;
}

function Beneficiarios() {
  const { control, setValue } = useFormContext();
  const lectura = useContext(Lectura);
  const { fields, append, remove } = useFieldArray({ control, name: 'beneficiarios' });
  const sin = !!useWatch({ name: 'sinBeneficiarios' });
  return (
    <Grupo titulo="Beneficiarios finales">
      {fields.map((f, i) => (
        <Grupo key={f.id} titulo={`Beneficiario ${i + 1}`} anidado>
          <Campo name={`beneficiarios.${i}.nombre`} label="Nombre y apellido" />
          <Campo name={`beneficiarios.${i}.documento`} label="Documento" />
          <Campo name={`beneficiarios.${i}.nacionalidad`} label="Nacionalidad" opciones={PAIS_OPCIONES} />
          <Campo name={`beneficiarios.${i}.porcentaje`} label="Porcentaje de participación (%)" type="number" />
          <Pep name={`beneficiarios.${i}.pep`} />
          {!lectura && (
            <Acciones>
              <Button color="error" sx={{ minHeight: 44 }} onClick={() => remove(i)}>
                Quitar beneficiario {i + 1}
              </Button>
            </Acciones>
          )}
        </Grupo>
      ))}
      <ErrorLista name="beneficiarios" />
      {!lectura && (
        <Acciones>
          <Button variant="outlined" sx={{ minHeight: 44 }} disabled={sin || fields.length >= 20} onClick={() => append({ pep: { esPep: false } })}>
            Agregar beneficiario
          </Button>
        </Acciones>
      )}
      <FormControlLabel
        sx={{ minHeight: 44, ...completo }}
        label="No hay beneficiarios identificables"
        disabled={lectura || fields.length > 0}
        control={
          <Checkbox
            checked={sin}
            onChange={(_, marcado) => setValue('sinBeneficiarios', marcado ? { explicacion: '' } : undefined, { shouldDirty: true })}
          />
        }
      />
      {sin && <Campo name="sinBeneficiarios.explicacion" label="Explicá por qué no hay beneficiarios identificables" rows={3} ancho descartable />}
    </Grupo>
  );
}

function Representantes() {
  const { control } = useFormContext();
  const lectura = useContext(Lectura);
  const { fields, append, remove } = useFieldArray({ control, name: 'representantes' });
  return (
    <Grupo titulo="Representantes legales">
      {fields.map((f, i) => (
        <Grupo key={f.id} titulo={`Representante ${i + 1}`} anidado>
          <Campo name={`representantes.${i}.nombre`} label="Nombre y apellido" />
          <Campo name={`representantes.${i}.documento`} label="Documento" />
          <Pep name={`representantes.${i}.pep`} />
          {!lectura && (
            <Acciones>
              <Button color="error" sx={{ minHeight: 44 }} onClick={() => remove(i)}>
                Quitar representante {i + 1}
              </Button>
            </Acciones>
          )}
        </Grupo>
      ))}
      <ErrorLista name="representantes" />
      {!lectura && (
        <Acciones>
          <Button variant="outlined" sx={{ minHeight: 44 }} disabled={fields.length >= 10} onClick={() => append({ pep: { esPep: false } })}>
            Agregar representante
          </Button>
        </Acciones>
      )}
    </Grupo>
  );
}

function DomicilioLegal() {
  const igual = !!useWatch({ name: 'domicilioLegalIgualFiscal' });
  return (
    <Grupo titulo="Domicilio legal">
      <Casilla name="domicilioLegalIgualFiscal" label="Es igual al domicilio fiscal" />
      {!igual && <Domicilio name="domicilioLegal" descartable />}
    </Grupo>
  );
}

export function FormularioSection({ caso, formulario, onEnviado }: { caso: Caso; formulario: Formulario; onEnviado: (f: Formulario) => void }) {
  const persona = caso.tipoPersona as TipoPersona;
  const juridica = persona === 'Juridica';
  const enviado = formulario.estado === 'Enviado';
  const lectura = enviado || !caso.editable;
  const [aviso, setAviso] = useState<Aviso | null>(null);
  const [aEnviar, setAEnviar] = useState<FieldValues | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const version = useRef(formulario.version ?? null);

  const form = useForm<FieldValues>({
    resolver: zodResolver(formularioSchema(persona)) as unknown as Resolver<FieldValues>,
    // Una persona jurídica necesita al menos un representante: arranca con uno en blanco.
    defaultValues: juridica && !lectura && !formulario.datos.representantes?.length ? { ...formulario.datos, representantes: [{ pep: { esPep: false } }] } : formulario.datos,
  });
  const { isDirty } = form.formState;

  // Salir (de la ruta o de la pestaña) con cambios sin guardar pide confirmación.
  const bloqueo = useBlocker({ shouldBlockFn: siempre, disabled: !isDirty, enableBeforeUnload: false, withResolver: true });
  useEffect(() => {
    if (!isDirty) return;
    const avisar = (e: BeforeUnloadEvent) => e.preventDefault();
    window.addEventListener('beforeunload', avisar);
    return () => window.removeEventListener('beforeunload', avisar);
  }, [isDirty]);

  function fallo(e: unknown) {
    if (!(e instanceof ApiError)) return setAviso({ severity: 'error', textos: ['Ocurrió un error inesperado.'] });
    const generales: string[] = [];
    for (const [clave, mensajes] of Object.entries(e.errors)) {
      if (esCampo(clave)) form.setError(clave, { message: mensajes[0] });
      else generales.push(...mensajes);
    }
    setAviso({ severity: 'error', textos: generales.length ? generales : [e.title] });
  }

  async function guardar() {
    form.clearErrors();
    setAviso(null);
    setOcupado(true);
    try {
      // Guardar no valida obligatorios: manda lo que haya, sin pasar por el resolver.
      const valores = form.getValues();
      const r = await api<{ version: string; sobrescrito: boolean }>('/caso/formulario', {
        method: 'PUT',
        body: { datos: payload(valores), version: version.current },
      });
      version.current = r.version;
      form.reset(valores, { keepValues: true });
      setAviso(
        r.sobrescrito
          ? { severity: 'warning', textos: ['Otra sesión guardó cambios y se sobrescribieron.'] }
          : { severity: 'success', textos: ['Guardado'] },
      );
    } catch (e) {
      fallo(e);
    } finally {
      setOcupado(false);
    }
  }

  async function enviar() {
    setAviso(null);
    setOcupado(true);
    try {
      const r = await api<Formulario>('/caso/formulario/envio', { body: { datos: payload(aEnviar!) } });
      form.reset(form.getValues(), { keepValues: true });
      onEnviado(r);
    } catch (e) {
      fallo(e);
    } finally {
      setAEnviar(null);
      setOcupado(false);
    }
  }

  const confirmar = form.handleSubmit((datos) => setAEnviar(datos));

  return (
    <Box component="section" sx={{ mt: 3 }}>
      <Typography variant="h6" component="h2">
        Formulario
      </Typography>
      {enviado && (
        <Alert severity="success" sx={{ mt: 1 }}>
          Recibimos tu información. Ya no se puede modificar.
        </Alert>
      )}
      <FormProvider {...form}>
        <Lectura.Provider value={lectura}>
          <Box component="form" noValidate onSubmit={(e: FormEvent) => void confirmar(e)} sx={{ display: 'grid', gap: 2 }}>
            <Grupo titulo={juridica ? 'Datos fiscales' : 'Datos personales'}>
              <TextField label="CUIT" value={caso.cuit} disabled />
              <TextField label={juridica ? 'Razón social' : 'Nombre y apellido'} value={caso.razonSocial} disabled />
              <Campo name="condicionIva" label="Condición frente al IVA" opciones={IVA} />
              {juridica ? (
                <>
                  <Campo name="fechaConstitucion" label="Fecha de constitución" type="date" />
                  <Campo name="numeroInscripcion" label="Número de inscripción" />
                </>
              ) : (
                <>
                  <Campo name="fechaNacimiento" label="Fecha de nacimiento" type="date" />
                  <Campo name="nacionalidad" label="Nacionalidad" opciones={PAIS_OPCIONES} />
                  <Campo name="documento.tipo" label="Tipo de documento" opciones={[['DNI', 'DNI'], ['Pasaporte', 'Pasaporte']]} />
                  <Campo name="documento.numero" label="Número de documento" />
                </>
              )}
            </Grupo>
            <Grupo titulo="Domicilio fiscal">
              <Domicilio name="domicilioFiscal" />
            </Grupo>
            {juridica && <DomicilioLegal />}
            <Grupo titulo="Actividad">
              <Campo name="actividad.descripcion" label="Descripción de la actividad" />
              <Campo name="actividad.pais" label="País donde se desarrolla" opciones={PAIS_OPCIONES} />
            </Grupo>
            {juridica ? (
              <>
                <Beneficiarios />
                <Representantes />
              </>
            ) : (
              <Grupo titulo="Declaración PEP">
                <Pep name="pep" />
              </Grupo>
            )}
            <Grupo titulo="Origen de los fondos">
              <Campo name="origenFondos" label="Origen de los fondos" rows={3} ancho />
            </Grupo>
            {aviso && (
              <Alert severity={aviso.severity}>
                {aviso.textos.map((t) => (
                  <div key={t}>{t}</div>
                ))}
              </Alert>
            )}
            {!lectura && (
              <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
                <Button variant="outlined" sx={{ minHeight: 44 }} disabled={ocupado} onClick={() => void guardar()}>
                  Guardar
                </Button>
                <Button type="submit" variant="contained" sx={{ minHeight: 44 }} disabled={ocupado}>
                  Enviar
                </Button>
              </Box>
            )}
          </Box>
        </Lectura.Provider>
      </FormProvider>

      <Dialog open={aEnviar !== null} onClose={() => !ocupado && setAEnviar(null)}>
        <DialogTitle>¿Enviar la información?</DialogTitle>
        <DialogContent>
          <DialogContentText>Una vez enviada no vas a poder modificarla.</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button sx={{ minHeight: 44 }} disabled={ocupado} onClick={() => setAEnviar(null)}>
            Cancelar
          </Button>
          <Button variant="contained" sx={{ minHeight: 44 }} disabled={ocupado} onClick={() => void enviar()}>
            Confirmar envío
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={bloqueo.status === 'blocked'}>
        <DialogTitle>Tenés cambios sin guardar</DialogTitle>
        <DialogContent>
          <DialogContentText>Si salís ahora se pierden los cambios que no guardaste.</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button sx={{ minHeight: 44 }} onClick={() => bloqueo.reset?.()}>
            Quedarme
          </Button>
          <Button color="error" sx={{ minHeight: 44 }} onClick={() => bloqueo.proceed?.()}>
            Salir sin guardar
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
