// Crea el usuario solicitante del e2e. El administrador inicial no tiene contraseña: se la fija por el flujo
// "olvidé mi contraseña" leyendo el token de Mailpit. Env: BASE_URL, MAILPIT_URL, E2E_REQUESTER_EMAIL/PASSWORD.
const { BASE_URL, MAILPIT_URL, E2E_REQUESTER_EMAIL: reqEmail, E2E_REQUESTER_PASSWORD: reqPass } = process.env;
const admin = 'admin@diligencia.local';
const adminPass = `${reqPass}-admin`;
const api = (path, init = {}) => fetch(`${BASE_URL}/api/backoffice${path}`, init);
const post = (body, extra = {}) => ({
  method: 'POST',
  headers: { 'Content-Type': 'application/json', ...extra },
  body: JSON.stringify(body),
});
const must = async (res) => {
  if (!res.ok) throw new Error(`${res.url} -> ${res.status} ${await res.text()}`);
  return res;
};

async function tokenFor(to, since) {
  for (let i = 0; i < 60; i++) {
    const { messages } = await (await fetch(`${MAILPIT_URL}/api/v1/messages`)).json();
    const m = messages.find((x) => x.To.some((t) => t.Address === to) && new Date(x.Created) >= since);
    if (m) {
      const { Text } = await (await fetch(`${MAILPIT_URL}/api/v1/message/${m.ID}`)).json();
      return /restablecer\?token=([\w-]+)/.exec(Text)[1];
    }
    await new Promise((r) => setTimeout(r, 2000));
  }
  throw new Error(`Sin correo para ${to}`);
}

async function setPassword(email, password, trigger) {
  const since = new Date(Date.now() - 5000);
  await trigger();
  await must(await api('/auth/password/reset', post({ token: await tokenFor(email, since), newPassword: password })));
}

await setPassword(admin, adminPass, async () => must(await api('/auth/password/forgot', post({ email: admin }))));
const login = await must(await api('/auth/login', post({ email: admin, password: adminPass })));
const cookie = login.headers
  .getSetCookie()
  .map((c) => c.split(';')[0])
  .join('; ');
const [area] = await (await must(await api('/areas', { headers: { cookie } }))).json();

await setPassword(reqEmail, reqPass, async () =>
  must(await api('/users', post({ name: 'Solicitante E2E', email: reqEmail, areaId: area.id, roles: ['Solicitante'] }, { cookie }))),
);
console.log('Solicitante listo:', reqEmail);
