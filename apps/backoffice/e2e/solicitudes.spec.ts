import { expect, test } from '@playwright/test';

const mailpit = process.env['MAILPIT_URL'] ?? 'http://localhost:8025';

// CUIT jurídico (prefijo 30) con dígito verificador válido.
function cuitJuridico() {
  const pesos = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
  for (;;) {
    const base = '30' + String(Math.floor(Math.random() * 1e8)).padStart(8, '0');
    const r = 11 - (pesos.reduce((s, p, i) => s + p * Number(base[i]), 0) % 11);
    if (r !== 10) return base + (r === 11 ? 0 : r);
  }
}

test('el solicitante crea una solicitud y el contacto recibe el enlace', async ({ page, request }) => {
  const contacto = `contacto-${Date.now()}@example.com`;

  await page.goto('/login');
  await page.getByLabel('Correo').fill(process.env['E2E_REQUESTER_EMAIL']!);
  await page.getByLabel('Contraseña').fill(process.env['E2E_REQUESTER_PASSWORD']!);
  await page.getByRole('button', { name: 'Ingresar' }).click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));

  await page.goto('/solicitudes/nueva');
  await page.getByLabel('CUIT').fill(cuitJuridico());
  await page.getByLabel('Razón social').fill('Proveedor E2E SA');
  await page.getByLabel('Nombre del contacto').fill('Contacto E2E');
  await page.getByLabel('Correo del contacto').fill(contacto);
  await page.getByLabel('Monto anual estimado (ARS)').fill('1000');
  await page.getByLabel('Motivo').fill('Prueba end-to-end');
  await page.getByRole('button', { name: 'Enviar solicitud' }).click();

  await expect(page.getByRole('heading', { name: 'Mis solicitudes' })).toBeVisible();
  await expect(page.getByRole('row', { name: /Proveedor E2E SA/ })).toContainText('Espera del tercero');

  // El envío corre en el worker (Hangfire): puede tardar hasta un minuto.
  await expect
    .poll(
      async () => {
        const res = await request.get(`${mailpit}/api/v1/messages`);
        const { messages } = (await res.json()) as { messages: { ID: string; To: { Address: string }[] }[] };
        const m = messages.find((x) => x.To.some((t) => t.Address === contacto));
        if (!m) return '';
        const detail = (await (await request.get(`${mailpit}/api/v1/message/${m.ID}`)).json()) as { Text: string };
        return detail.Text;
      },
      { timeout: 90_000 },
    )
    .toContain('/acceso#t=');
});
