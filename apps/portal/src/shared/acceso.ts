// El token del enlace vive solo en sessionStorage (por pestaña); todo acceso va con try/catch por si el navegador lo bloquea.
const TOKEN = 'portal.token';
const PEDIDO = 'portal.codigoPedido';

const leer = (clave: string) => {
  try {
    return sessionStorage.getItem(clave);
  } catch {
    return null;
  }
};
const escribir = (clave: string, valor: string | null) => {
  try {
    if (valor === null) sessionStorage.removeItem(clave);
    else sessionStorage.setItem(clave, valor);
  } catch {
    // Sin almacenamiento: el flujo sigue, solo que recargar pierde el token.
  }
};

export const getToken = () => leer(TOKEN);
/** Un token nuevo reinicia la marca de "código ya pedido". */
export function saveToken(token: string) {
  if (token !== getToken()) escribir(PEDIDO, null);
  escribir(TOKEN, token);
}
export function clearToken() {
  escribir(TOKEN, null);
  escribir(PEDIDO, null);
}
export const codigoPedido = () => leer(PEDIDO) === '1';
export const marcarCodigoPedido = () => escribir(PEDIDO, '1');
