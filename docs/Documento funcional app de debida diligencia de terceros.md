# Documento funcional: app de debida diligencia de terceros

Oct 15, 2026 · @Carlos

## 1. Objetivo

La aplicación centraliza la debida diligencia de terceros (clientes y proveedores) en un solo flujo: alta, verificación, evaluación de riesgo, aprobación y monitoreo, con evidencia de cada paso para auditoría.

Se asume que hoy el proceso se resuelve con correos y planillas. Eso genera demoras, criterios distintos según quién analiza y falta de evidencia cuando un auditor la pide.

Objetivos de la primera versión:

- Aplicar el mismo criterio de riesgo a todos los terceros, con reglas documentadas y configurables.
- Dejar registro de quién verificó, evaluó y aprobó cada caso, y cuándo.
- Reducir el tiempo de alta de un tercero (la meta en días queda por definir con el negocio).
- Revisar a cada tercero de forma periódica, con una frecuencia según su nivel de riesgo.

## 2. Alcance

La primera versión cubre el ciclo completo de un tercero con verificaciones cargadas a mano; las integraciones automáticas quedan para una fase posterior.

**Incluye**

- Alta de clientes y proveedores, tanto personas humanas como jurídicas.
- Portal externo para que el tercero complete su formulario y suba documentos.
- Verificación con checklist y carga de evidencia por parte del analista.
- Matriz de riesgo configurable y cálculo automático del nivel.
- Aprobación por niveles, con segregación de funciones.
- Monitoreo y revisión periódica según el nivel de riesgo.
- Tablero, reportes, expediente exportable y bitácora de auditoría.

**No incluye**

- Consultas automáticas a listas de sanciones, PEP o padrones (fase 3).
- Validación biométrica de identidad.
- Envío de reportes de operaciones sospechosas a la UIF: la app registra el caso, pero el reporte se hace por los canales oficiales.
- Debida diligencia de fusiones y adquisiciones.

## 3. Roles y permisos

Seis roles intervienen en el proceso, y nadie puede aprobar un caso que analizó.

| Rol | Quién lo ocupa | Qué puede hacer |
| --- | --- | --- |
| Solicitante | Compras, comercial | Crear solicitudes, ver el estado de sus casos y el resultado final |
| Tercero | Cliente o proveedor (usuario externo) | Completar el formulario, subir documentos y responder pedidos de información |
| Analista de compliance | Área de cumplimiento | Verificar, registrar hallazgos, ajustar el riesgo con justificación y proponer una decisión |
| Aprobador | Jefe de compliance, gerente o comité | Aprobar, aprobar con condiciones o rechazar, según el nivel de riesgo |
| Administrador | Compliance con apoyo de TI | Configurar matriz de riesgo, formularios, plazos, usuarios y roles |
| Auditor | Auditoría interna o externa | Consultar casos, expedientes y bitácora, solo lectura |

Reglas de acceso:

- El analista de un caso no puede ser su aprobador (segregación de funciones).
- El solicitante ve el estado y la decisión, pero no el detalle de hallazgos ni de screening.
- El tercero solo ve sus propios datos y pedidos pendientes.
- Un usuario puede tener más de un rol, pero las reglas anteriores se aplican caso por caso.

## 4. Flujo del proceso y estados

Cada caso pasa por cuatro etapas de trabajo y termina en uno de tres resultados; un tercero activo vuelve a verificación en cada revisión.

&#91;embedded content: estados del caso · 8 estados, 2 ciclos\]

El analista puede devolver el caso al tercero tantas veces como haga falta; si el tercero no responde en plazo, el caso se cierra solo.

| Estado | Responsable | Sale cuando |
| --- | --- | --- |
| Borrador | Solicitante | Envía la solicitud al tercero |
| Espera del tercero | Tercero | Completa el formulario, o vence el plazo |
| En verificación | Analista | Termina el checklist y la evaluación de riesgo, o pide más información |
| En aprobación | Aprobador | Aprueba, aprueba con condiciones o rechaza |
| Aprobado | Sistema | Pasa a activo y fija la próxima revisión |
| Activo | Analista | Vence la revisión o surge un evento: se abre un nuevo caso |
| Rechazado | — | Estado final |
| Abandonado | — | Estado final; se puede reabrir con una nueva solicitud |

## 5. Requerimientos funcionales

Los requerimientos se agrupan en siete módulos; los de prioridad alta forman el MVP.

### 5.1 Solicitudes

| ID | Requerimiento | Prioridad |
| --- | --- | --- |
| RF-01 | El solicitante crea una solicitud con tipo de tercero (cliente o proveedor), tipo de persona, CUIT, razón social, contacto, monto anual estimado y motivo | Alta |
| RF-02 | El sistema avisa si ya existe un tercero con el mismo CUIT y muestra su estado | Alta |
| RF-03 | Al guardar, el sistema envía al tercero un enlace seguro al portal | Alta |
| RF-04 | El solicitante consulta sus solicitudes con estado y fecha estimada | Media |

### 5.2 Portal del tercero

| ID | Requerimiento | Prioridad |
| --- | --- | --- |
| RF-05 | El tercero accede con un enlace que vence y un código enviado por correo | Alta |
| RF-06 | El formulario cambia según tipo de persona: datos fiscales, domicilio, actividad, beneficiarios finales, declaración PEP y origen de fondos | Alta |
| RF-07 | El tercero sube documentos en PDF o imagen, con un tamaño máximo configurable | Alta |
| RF-08 | El tercero puede guardar a medias y retomar más tarde | Media |
| RF-09 | El tercero responde pedidos de información adicional del analista | Alta |

### 5.3 Verificación

| ID | Requerimiento | Prioridad |
| --- | --- | --- |
| RF-10 | Los casos completos entran en una bandeja y se asignan a un analista, a mano o por turno | Alta |
| RF-11 | El analista sigue un checklist según el tipo de tercero: identidad, CUIT, documentos, listas de sanciones, PEP y prensa negativa | Alta |
| RF-12 | Cada verificación registra resultado, fuente consultada, fecha y evidencia adjunta | Alta |
| RF-13 | El analista registra hallazgos con severidad y descripción | Alta |
| RF-14 | El analista pide información adicional y el caso queda en espera del tercero | Alta |

### 5.4 Evaluación de riesgo

| ID | Requerimiento | Prioridad |
| --- | --- | --- |
| RF-15 | El sistema calcula el puntaje y el nivel según la matriz vigente (sección 6) | Alta |
| RF-16 | Las reglas automáticas (por ejemplo, PEP) fijan un nivel mínimo | Alta |
| RF-17 | El analista puede subir el nivel con justificación; bajarlo requiere aprobación del jefe de compliance | Media |
| RF-18 | El caso guarda la versión de la matriz con la que se evaluó | Alta |

### 5.5 Aprobación

| ID | Requerimiento | Prioridad |
| --- | --- | --- |
| RF-19 | El caso se envía al aprobador que corresponde a su nivel de riesgo | Alta |
| RF-20 | El aprobador decide: aprobar, aprobar con condiciones o rechazar, con comentario obligatorio | Alta |
| RF-21 | Las condiciones tienen responsable y fecha, y se siguen hasta cerrarse | Media |
| RF-22 | Los casos de riesgo alto pueden requerir más de una firma | Media |
| RF-23 | El rechazo notifica al solicitante sin exponer los hallazgos | Alta |

### 5.6 Monitoreo

| ID | Requerimiento | Prioridad |
| --- | --- | --- |
| RF-24 | Al aprobarse, el tercero queda activo con fecha de próxima revisión según su nivel | Alta |
| RF-25 | El sistema abre un caso de revisión antes del vencimiento | Alta |
| RF-26 | Un analista puede abrir una revisión extraordinaria por un evento (cambio de socios, noticia, alerta) | Media |
| RF-27 | El administrador puede bloquear a un tercero, y el bloqueo queda registrado | Media |

### 5.7 Administración

| ID | Requerimiento | Prioridad |
| --- | --- | --- |
| RF-28 | El administrador gestiona usuarios, roles y asignación a áreas | Alta |
| RF-29 | El administrador edita factores, pesos y umbrales de la matriz, con versionado | Alta |
| RF-30 | El administrador configura formularios, checklists, documentos requeridos y plazos | Media |

## 6. Matriz de riesgo y reglas de negocio

El nivel de riesgo surge de sumar los puntos de cada factor; algunas reglas fijan un nivel mínimo sin importar el puntaje. Los valores siguientes son un ejemplo inicial que compliance debe calibrar.

### 6.1 Factores (ejemplo)

| Factor | Criterio | Puntos |
| --- | --- | --- |
| Geografía | Argentina o país de bajo riesgo / riesgo medio / país en listas del GAFI | 0 / 2 / 5 |
| Actividad | Bajo riesgo / riesgo medio / alto riesgo (cambio, criptoactivos, juego, inmobiliario, efectivo intensivo) | 0 / 2 / 4 |
| Monto anual estimado | Menor al umbral bajo / intermedio / mayor al umbral alto | 0 / 2 / 4 |
| Estructura societaria | Simple / con varias capas / beneficiario final no identificado | 0 / 2 / 4 |
| Antigüedad | Más de 3 años / de 1 a 3 años / menos de 1 año | 0 / 1 / 2 |
| Prensa negativa | Sin resultados / no confirmada / confirmada | 0 / 2 / 5 |
| Documentación | Completa / con observaciones | 0 / 2 |

### 6.2 Niveles

| Nivel | Puntaje | Tipo de diligencia | Aprueba | Revisión periódica |
| --- | --- | --- | --- | --- |
| Bajo | 0 a 5 | Simplificada: datos y documentos básicos | Analista senior o jefe de compliance | Cada 3 años |
| Medio | 6 a 11 | Estándar: checklist completo | Jefe de compliance | Cada 2 años |
| Alto | 12 o más | Reforzada: origen de fondos, entrevista y, si aplica, visita | Gerente de compliance o comité | Cada año |

### 6.3 Reglas automáticas

- Coincidencia confirmada en una lista de sanciones: el caso se bloquea y escala a la gerencia; no se puede aprobar.
- Tercero o beneficiario final PEP: nivel mínimo alto.
- Beneficiario final no identificado: no se puede aprobar hasta identificarlo.
- Documento obligatorio faltante: el caso no avanza a aprobación.
- Un caso sin respuesta del tercero en el plazo configurado se cierra como abandonado.

Si la empresa es sujeto obligado ante la UIF, los factores, plazos y documentos deben ajustarse a la resolución que le aplique según su actividad.

## 7. Modelo de datos

El centro del modelo es el caso: cada tercero tiene un caso de alta y luego uno por cada revisión, y toda la evidencia cuelga del caso.

| Entidad | Campos principales | Relaciones |
| --- | --- | --- |
| Tercero | ID, tipo (cliente o proveedor), tipo de persona, CUIT, razón social, país, actividad, estado (activo, bloqueado, inactivo), nivel de riesgo vigente, próxima revisión | Tiene muchos casos, beneficiarios y documentos |
| Beneficiario final | Nombre, documento, nacionalidad, porcentaje de participación, es PEP | Pertenece a un tercero |
| Caso | ID, tipo (alta, revisión periódica, extraordinaria), estado, solicitante, analista, fechas por etapa, versión de matriz | Pertenece a un tercero; tiene verificaciones, hallazgos, una evaluación y una decisión |
| Documento | Tipo, archivo, fecha de carga, vencimiento, cargado por | Pertenece a un tercero y a un caso |
| Verificación | Tipo (identidad, CUIT, sanciones, PEP, prensa), resultado, fuente, fecha, evidencia | Pertenece a un caso |
| Hallazgo | Descripción, severidad, estado, responsable | Pertenece a un caso |
| Pedido de información | Texto, fecha límite, estado, respuesta | Pertenece a un caso |
| Evaluación de riesgo | Puntaje por factor, puntaje total, nivel calculado, nivel final, justificación | Una por caso |
| Decisión | Resultado, condiciones, comentario, aprobador, fecha | Una por caso |
| Matriz de riesgo | Versión, factores, pesos, umbrales, vigencia | Usada por muchos casos |
| Usuario | Nombre, correo, área, roles, activo | Actúa en casos y eventos |
| Evento de auditoría | Fecha y hora, usuario, acción, entidad, valor anterior, valor nuevo | Referencia cualquier entidad |

## 8. Notificaciones y plazos

Cada etapa tiene un plazo configurable; los valores son una propuesta en días hábiles para validar con el negocio.

| Evento | Destinatario | Canal | Plazo propuesto |
| --- | --- | --- | --- |
| Solicitud creada | Tercero | Correo con enlace al portal | Inmediato |
| Formulario sin completar | Tercero; luego el solicitante | Correo | Recordatorio a los 3 días; aviso al solicitante a los 7; cierre a los 15 |
| Caso listo para verificar | Analista asignado | Correo y bandeja | Inmediato; verificación en 5 días |
| Pedido de información | Tercero | Correo | Respuesta en 5 días |
| Caso listo para aprobar | Aprobador | Correo y bandeja | Inmediato; decisión en 3 días |
| Decisión tomada | Solicitante y tercero | Correo | Inmediato |
| Plazo de etapa vencido | Responsable y su jefe | Correo | Al vencer |
| Revisión periódica próxima | Analista | Bandeja | 30 días antes del vencimiento |
| Documento por vencer | Tercero y analista | Correo | 30 días antes |

## 9. Reportes y auditoría

El tablero muestra la carga de trabajo y los vencimientos, y la bitácora permite reconstruir cualquier caso de punta a punta.

**Tablero de compliance**

- Casos abiertos por estado y por analista.
- Tiempo promedio por etapa y casos con plazo vencido.
- Terceros activos por nivel de riesgo.
- Revisiones periódicas vencidas y próximas a vencer.
- Rechazos y casos abandonados por mes.

**Expediente del caso**

- Exportación en PDF con datos del tercero, documentos, verificaciones, evaluación, decisión y bitácora.
- Es la evidencia que se entrega ante una auditoría o inspección.

**Bitácora**

- Registra toda creación, cambio, consulta de datos sensibles, descarga y decisión.
- Guarda usuario, fecha y hora, acción, valor anterior y valor nuevo.
- Nadie puede editarla ni borrarla, ni siquiera el administrador.

**Exportaciones**

- Listado de terceros y casos en Excel, con filtros por fecha, estado y nivel de riesgo.

## 10. Requerimientos no funcionales

La app maneja datos personales y financieros sensibles, así que la seguridad y la trazabilidad son requisitos de base, no mejoras.

| ID | Categoría | Requerimiento |
| --- | --- | --- |
| RNF-01 | Seguridad | Ingreso de usuarios internos con el login corporativo (SSO) y segundo factor |
| RNF-02 | Seguridad | Permisos por rol aplicados en el servidor, no solo en pantalla |
| RNF-03 | Seguridad | Datos y documentos cifrados en tránsito y en reposo |
| RNF-04 | Privacidad | Cumplimiento de la Ley 25.326 de protección de datos personales: finalidad, consentimiento y acceso del titular |
| RNF-05 | Conservación | Plazo de guarda de expedientes configurable; los sujetos obligados ante la UIF suelen conservar 10 años (validar con legales) |
| RNF-06 | Disponibilidad | 99,5 % en horario hábil, con copias de seguridad diarias (propuesta) |
| RNF-07 | Rendimiento | Pantallas en menos de 2 segundos con 10.000 terceros cargados (propuesta) |
| RNF-08 | Usabilidad | Portal del tercero usable desde el celular y en español |
| RNF-09 | Integración | API para conectar luego con el ERP y con proveedores de screening |

## 11. Fases de implementación

Se propone entregar en tres fases, empezando por el flujo manual completo para validar el proceso antes de automatizar.

1. **Fase 1, MVP:** solicitudes, portal del tercero, verificación con checklist, matriz de riesgo, aprobación, bitácora y expediente en PDF (requerimientos de prioridad alta).
2. **Fase 2, operación:** revisiones periódicas y extraordinarias, condiciones con seguimiento, tablero, notificaciones de vencimiento y edición de formularios y checklists.
3. **Fase 3, integraciones:** consulta automática del CUIT en ARCA, proveedor de screening de sanciones, PEP y prensa, firma digital y conexión con el ERP para dar de alta proveedores y clientes aprobados.

La duración de cada fase depende del equipo y la tecnología, que todavía no están definidos.

## 12. Supuestos, preguntas abiertas y glosario

**Supuestos**

- La app es para uso interno de una empresa que opera en Argentina.
- Compliance es dueño del proceso y de la matriz de riesgo.
- Los terceros se comunican por correo electrónico.

**Preguntas abiertas**

- [ ] ¿La empresa es sujeto obligado ante la UIF? ¿Qué resolución le aplica?
- [ ] ¿Se analizan clientes, proveedores o ambos desde la primera fase?
- [ ] ¿Cuántos terceros se dan de alta por mes y cuántos hay activos hoy?
- [ ] ¿Qué umbrales de monto usar en la matriz de riesgo?
- [ ] ¿Quién aprueba cada nivel y hace falta un comité para riesgo alto?
- [ ] ¿Se usa o se va a contratar un proveedor de screening?
- [ ] ¿Con qué sistemas hay que integrarse (ERP, login corporativo)?
- [ ] ¿Qué plazo de conservación de documentos define legales?

**Glosario**

| Término | Significado |
| --- | --- |
| Debida diligencia | Investigación de un tercero antes y durante la relación para identificar riesgos |
| Tercero | Cliente, proveedor u otra persona o empresa con la que se opera |
| KYC | "Conocé a tu cliente": verificar la identidad y la actividad de un cliente |
| Beneficiario final | Persona humana que controla o posee en última instancia a una empresa |
| PEP | Persona expuesta políticamente: funcionarios y personas cercanas a ellos |
| Screening | Cruce del tercero contra listas de sanciones, PEP y prensa negativa |
| Diligencia reforzada | Controles adicionales para terceros de riesgo alto |
| UIF | Unidad de Información Financiera, el organismo argentino de prevención de lavado |
| ARCA | Agencia de Recaudación y Control Aduanero, el organismo fiscal que reemplazó a la AFIP |
| GAFI | Grupo de Acción Financiera Internacional, que publica listas de países de riesgo |
