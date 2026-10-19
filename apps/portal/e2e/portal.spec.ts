import { expect, test, type APIRequestContext, type Page } from '@playwright/test';

const mailpit = process.env['MAILPIT_URL'] ?? 'http://localhost:8025';
// En CI BASE_URL es el backoffice; en el quickstart es el portal y BACKOFFICE_URL el backoffice.
// El portal se abre siempre con el enlace absoluto del correo, nunca con BASE_URL.
const backoffice = process.env['BACKOFFICE_URL'] ?? process.env['BASE_URL']!;

type Persona = 'Humana' | 'Juridica';

// CUIT con dígito verificador válido (prefijo 30 para jurídicas, 20 para humanas).
function cuit(prefijo: string) {
  const pesos = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
  for (;;) {
    const base = prefijo + String(Math.floor(Math.random() * 1e8)).padStart(8, '0');
    const r = 11 - (pesos.reduce((s, p, i) => s + p * Number(base[i]), 0) % 11);
    if (r !== 10) return base + (r === 11 ? 0 : r);
  }
}

// PDF mínimo que acepta el inspector de contenido (empieza con %PDF- y termina con %%EOF).
const pdf = Buffer.from('%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n');

async function crearCaso(request: APIRequestContext, persona: Persona) {
  const login = await request.post(`${backoffice}/api/backoffice/auth/login`, {
    data: { email: process.env['E2E_REQUESTER_EMAIL'], password: process.env['E2E_REQUESTER_PASSWORD'] },
  });
  expect(login.ok()).toBeTruthy();
  // La cookie es Secure: se reenvía a mano para no depender de cómo el jar trata http://localhost.
  const cookie = login
    .headersArray()
    .filter((h) => h.name.toLowerCase() === 'set-cookie')
    .map((h) => h.value.split(';')[0])
    .join('; ');

  const contacto = `contacto-${Date.now()}@example.com`;
  const juridica = persona === 'Juridica';
  const res = await request.post(`${backoffice}/api/backoffice/solicitudes`, {
    headers: { cookie },
    data: {
      tipoTercero: 'Proveedor',
      tipoPersona: persona,
      cuit: cuit(juridica ? '30' : '20'),
      razonSocial: juridica ? 'Proveedor Portal E2E SA' : 'Ana Pérez E2E',
      contactoNombre: 'Contacto E2E',
      contactoEmail: contacto,
      contactoTelefono: null,
      montoAnualEstimado: 1000,
      motivo: 'Prueba end-to-end del portal',
    },
  });
  expect(res.ok()).toBeTruthy();
  return { contacto, razonSocial: juridica ? 'Proveedor Portal E2E SA' : 'Ana Pérez E2E' };
}

// Primer correo al destinatario cuyo asunto y cuerpo coinciden con el patrón.
async function leerCorreo(request: APIRequestContext, destinatario: string, patron: RegExp, timeout: number) {
  let texto = '';
  await expect
    .poll(
      async () => {
        const { messages } = (await (await request.get(`${mailpit}/api/v1/messages`)).json()) as {
          messages: { ID: string; To: { Address: string }[] }[];
        };
        for (const m of messages.filter((x) => x.To.some((t) => t.Address === destinatario))) {
          const d = (await (await request.get(`${mailpit}/api/v1/message/${m.ID}`)).json()) as { Subject: string; Text: string };
          const todo = `${d.Subject}\n${d.Text}`;
          if (patron.test(todo)) return (texto = todo);
        }
        return '';
      },
      { timeout },
    )
    .not.toBe('');
  return texto;
}

async function ingresar(page: Page, request: APIRequestContext, contacto: string, razonSocial: string) {
  // El worker envía el enlace por Hangfire: puede tardar hasta ~90 s.
  const enlace = /https?:\/\/[^\s<>"]+\/acceso#t=[\w-]+/.exec(await leerCorreo(request, contacto, /\/acceso#t=/, 90_000))![0];
  await page.goto(enlace);
  await expect(page.getByText(/Te enviamos un código a/)).toBeVisible();

  const codigo = /Tu código de ingreso es: (\d{6})/.exec(await leerCorreo(request, contacto, /Tu código de ingreso al portal/, 30_000))![1]!;
  await page.getByLabel('Código de 6 dígitos').fill(codigo);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await expect(page.getByRole('heading', { name: razonSocial })).toBeVisible();
}

async function subirDocumentos(page: Page, nombres: string[]) {
  for (const [i, nombre] of nombres.entries()) {
    await page.getByLabel(`Archivo: ${nombre}`, { exact: true }).setInputFiles({ name: 'documento.pdf', mimeType: 'application/pdf', buffer: pdf });
    await expect(page.getByText(/documento\.pdf · /)).toHaveCount(i + 1);
  }
}

const campo = (page: Page, grupo: string, etiqueta: string) =>
  page.getByRole('group', { name: grupo, exact: true }).getByLabel(etiqueta, { exact: true });

async function completarComun(page: Page, fiscales: string) {
  await campo(page, fiscales, 'Condición frente al IVA').selectOption('Monotributo');
  await campo(page, 'Domicilio fiscal', 'Calle').fill('Mitre');
  await campo(page, 'Domicilio fiscal', 'Número').fill('123');
  await campo(page, 'Domicilio fiscal', 'Localidad').fill('CABA');
  await campo(page, 'Domicilio fiscal', 'Provincia').fill('Buenos Aires');
  await campo(page, 'Domicilio fiscal', 'Código postal').fill('1000');
  await campo(page, 'Domicilio fiscal', 'País').selectOption('AR');
  await campo(page, 'Actividad', 'Descripción de la actividad').fill('Desarrollo de software');
  await campo(page, 'Actividad', 'País donde se desarrolla').selectOption('AR');
  await campo(page, 'Origen de los fondos', 'Origen de los fondos').fill('Venta de servicios profesionales');
}

async function enviar(page: Page) {
  await page.getByRole('button', { name: 'Enviar', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar envío' }).click();
  await expect(page.getByText('Recibimos tu información')).toBeVisible();
}

test('carga de documentos en portal: persona jurídica completa el formulario, sube documentos y envía', async ({ page, request }) => {
  const { contacto, razonSocial } = await crearCaso(request, 'Juridica');
  await ingresar(page, request, contacto, razonSocial);

  await subirDocumentos(page, [
    'Estatuto o contrato social',
    'Constancia de inscripción en ARCA',
    'Acta de designación de autoridades',
    'Documento del representante legal',
  ]);

  await completarComun(page, 'Datos fiscales');
  await campo(page, 'Datos fiscales', 'Fecha de constitución').fill('2010-05-01');
  await campo(page, 'Datos fiscales', 'Número de inscripción').fill('12345');
  await campo(page, 'Domicilio legal', 'Es igual al domicilio fiscal').check();
  for (const [i, porcentaje] of ['60', '40'].entries()) {
    await page.getByRole('button', { name: 'Agregar beneficiario' }).click();
    const b = `Beneficiario ${i + 1}`;
    await campo(page, b, 'Nombre y apellido').fill(`Socio ${i + 1}`);
    await campo(page, b, 'Documento').fill(`2011122${i}`);
    await campo(page, b, 'Nacionalidad').selectOption('AR');
    await campo(page, b, 'Porcentaje de participación (%)').fill(porcentaje);
  }
  await campo(page, 'Representante 1', 'Nombre y apellido').fill('Ana Pérez');
  await campo(page, 'Representante 1', 'Documento').fill('20111222');

  // Guardar a medias confirma y no bloquea el envío.
  await page.getByRole('button', { name: 'Guardar', exact: true }).click();
  await expect(page.getByText('Guardado', { exact: true })).toBeVisible();

  await enviar(page);
});

test('persona humana sin beneficiarios sube sus documentos y envía desde el celular @mobile', async ({ page, request }) => {
  const { contacto, razonSocial } = await crearCaso(request, 'Humana');
  await ingresar(page, request, contacto, razonSocial);
  await expect(page.getByRole('group', { name: 'Beneficiarios finales' })).toHaveCount(0);

  await subirDocumentos(page, ['Documento de identidad (frente)', 'Documento de identidad (dorso)', 'Constancia de inscripción en ARCA']);

  await completarComun(page, 'Datos personales');
  await campo(page, 'Datos personales', 'Fecha de nacimiento').fill('1985-03-10');
  await campo(page, 'Datos personales', 'Nacionalidad').selectOption('AR');
  await campo(page, 'Datos personales', 'Tipo de documento').selectOption('DNI');
  await campo(page, 'Datos personales', 'Número de documento').fill('12345678');

  await enviar(page);
});
