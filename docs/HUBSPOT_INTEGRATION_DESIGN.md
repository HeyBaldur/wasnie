# HubSpot ↔ Wasnie — Diseño de integración (OAuth Public App)

> Documento de DISEÑO / arquitectura. No es código. Fija decisiones y parte el trabajo en fases verificables. De aquí salen los WIs, uno por fase. Revisar y ajustar con el owner antes de generar WIs.

---

## Objetivo y justificación

Los datos de venta de los clientes viven en su CRM y se generan a diario. Obligar a importar Excel manualmente cada día es fricción que mata el producto. La integración con CRM es **mesa de entrada** en el mercado ICM (toda la competencia, de la más barata a la más cara, integra al menos HubSpot y Salesforce). Wasnie necesita conexión automática a CRM para ser considerado.

Decisión de alcance: **empezar por HubSpot** (API bien documentada, OAuth claro, tier gratuito, el owner ya tiene cuenta). Salesforce y otros CRMs vienen después. Diseñar la capa de ingestión de modo que el modelo interno NO quede acoplado a HubSpot (ver "Capa de abstracción").

Decisión de autenticación (owner, firme): **OAuth Public App desde el principio** — NO private app. Razón: (1) los clientes no son técnicos y no van a crear private apps; el valor es "click, click, conectado"; (2) migrar de private a public después sería tiempo perdido. Hacerlo bien desde el inicio.

---

## Realidades de la API de HubSpot (de la investigación, 2026) — NO ignorar

- **OAuth obligatorio para multi-cuenta.** API keys deprecadas. Public App = el cliente instala/autoriza, Wasnie maneja tokens.
- **Tokens de acceso expiran rápido (~30 min; HubSpot lo bajó de 6h a 30min con poco aviso).** NO hardcodear expiración: leer `expires_in` del response. Un refresh REVOCA el access token viejo — todos los syncs deben usar solo el nuevo. Si el refresh falla con `BAD_REFRESH_TOKEN`, hay un flujo de recuperación/reconexión.
- **Tokens = contraseñas del CRM del cliente.** Encriptar en reposo (envelope encryption / KMS idealmente). NUNCA loguearlos. NUNCA devolverlos al frontend.
- **Versionado de API por fecha** (release 2026-03): formato `/crm/objects/2026-03/...`. Endpoints nuevos deben usar el versionado por fecha; los legacy `/crm/v3/`, `/crm/v4/` siguen vivos hasta su EOL. Versiones nuevas en cadencia marzo/septiembre, ventana de soporte de 18 meses. Diseñar para poder cambiar de versión sin reescribir todo.
- **Rate limits:** 429 → respetar `Retry-After` + backoff exponencial. Para volumen, usar batch endpoints. Para tiempo real, webhooks en vez de polling.
- **App separada para prod y dev** (recomendado fuerte): evita líos al cambiar scopes.
- **Config por cliente varía:** cada cuenta HubSpot puede tener pipelines/stages/propiedades custom. La integración debe tolerar que el "closed won" o las propiedades no sean idénticas entre clientes.
- "El iceberg": el HTTP es el 10%; auth, paginación, rate limits, normalización, webhooks y mantenimiento son el 90%.

---

## Fases (cada una es uno o más WIs; cada una se VERIFICA en pantalla antes de la siguiente)

Principio rector: OAuth desde el inicio, pero construido por fases que NO se desperdician. OAuth (puerta de entrada) y pipeline de datos (deals→transacciones) son piezas independientes que se unen. La lección del proyecto aplica: "tests verdes ≠ runtime correcto" — verificar cada fase en pantalla.

### Fase 1 — OAuth Public App (la puerta)
- Registrar una Public App en el portal de desarrollador de HubSpot (app separada dev y prod).
- Scopes mínimos: lectura de deals/owners/pipelines (`crm.objects.deals.read`, owners, etc. — confirmar el set exacto al implementar; pedir lo mínimo).
- Backend OAuth: endpoint de "Connect" → redirect a HubSpot → callback que intercambia el `code` por access+refresh token → guardar.
- **Token store por tenant**, encriptado en reposo (nunca en claro, nunca al frontend, nunca a logs). Guardar: portalId/hubId, accessToken, refreshToken, expiry (de `expires_in`).
- **Refresh automático**: antes de cada uso o por job, refrescar si está por expirar; usar solo el token nuevo tras refresh; manejar `BAD_REFRESH_TOKEN` → marcar conexión como "necesita reconectar" y avisar en UI.
- UI: una pantalla de "Conectar HubSpot" (botón Connect), estado de conexión (conectado / expirado / desconectado), y botón desconectar.
- **Verificación en pantalla:** el owner conecta su cuenta real de HubSpot, ve "Conectado", y un endpoint de prueba (p. ej. traer 1 deal) responde OK usando el token guardado. Desconectar funciona.

### Fase 2 — Traer deals y mapearlos a transacciones de Wasnie (el valor)
- Usando el token de la Fase 1, llamar al endpoint de deals (versionado por fecha), con paginación cursor (`after`).
- **Mapeo deal → transacción** (la lógica de negocio clave, decidir con el owner):
  - Solo deals en estado **closed-won** generan transacción/crédito (los demás se ignoran o se traen como no-elegibles — decidir).
  - `amount` → Amount; `closedate` → fecha de transacción; deal `owner` → payee (¿cómo se resuelve el owner de HubSpot al payee de Wasnie? por email del owner, por un mapeo manual, etc. — DECISIÓN PENDIENTE, importante); `dealname`/id → Reference; currency → Currency.
  - Tolerar pipelines/stages custom por cliente (no asumir que "closedwon" es el id en toda cuenta).
- **Idempotencia:** no crear transacciones duplicadas si un deal ya se importó (clave por deal id de HubSpot + tenant). Encaja con el modelo existente (las transacciones ya tienen anti-duplicado vía Reference; usar el deal id como referencia estable).
- De ahí, el pipeline EXISTENTE de Wasnie hace el resto (Pending → procesar → credits → pay run). NO se reescribe el motor.
- **Verificación en pantalla:** importar deals reales de la cuenta del owner → aparecen como transacciones en Wasnie → se calculan comisiones correctas. Re-importar no duplica.

### Fase 3 — Sincronización automática (polling)
- Job de Hangfire (ya en el stack) que periódicamente trae deals nuevos/modificados usando `lastmodifieddate` para no re-traer todo.
- Catch-up/reconciliación: el job debe poder recuperar lo que se haya perdido.
- **Verificación:** un deal nuevo closed-won en HubSpot aparece en Wasnie tras el siguiente ciclo, sin acción manual.

### Fase 4 — Webhooks (tiempo real) — OPCIONAL, posterior
- Suscribirse a eventos de deals para reflejar cambios en tiempo real, con el job de reconciliación de Fase 3 como red de seguridad.
- Manejar deal editado/borrado DESPUÉS de pagado (caso delicado: un deal que ya generó comisión pagada y luego cambia en HubSpot — definir política; NO romper el anti-doble-pago ni la inmutabilidad de créditos consumidos).

---

## Capa de abstracción (para no casarse con HubSpot)

- Definir una interfaz interna tipo `ICrmDealSource` (traer deals, mapear a un DTO neutro de "deal") para que HubSpot sea UNA implementación. Cuando llegue Salesforce/Pipedrive, se implementa la misma interfaz sin tocar el pipeline interno. Esto preserva el diferenciador de Wasnie (servir a quien NO usa un CRM concreto) y evita acoplamiento.

---

## Decisiones PENDIENTES (resolver con el owner antes de los WIs de Fase 2)

1. **Resolución de owner → payee:** ¿cómo se conecta el deal owner de HubSpot (un usuario de HubSpot, con email) al payee de Wasnie (que tiene código/nombre)? Por email, por mapeo manual en una pantalla, ¿auto-crear payee si no existe? Esta es la decisión más importante del mapeo.
2. **Qué pasa con deals no-closed-won:** ignorarlos del todo, o traerlos como transacciones no-elegibles para visibilidad.
3. **Múltiples owners / splits en un deal:** HubSpot puede tener varios colaboradores; Wasnie hoy es 1 transacción → 1 payee (modelo 1:1). Por ahora, mapear solo al deal owner principal; splits = pendiente (atado a la decisión multi-plan ya parqueada).
4. **Moneda:** si el deal viene en una moneda distinta a la del plan, ¿qué se hace? (FX no se almacena hoy — gap conocido.)

---

## Riesgos / notas

- Esto es un proyecto de SEMANAS, no un WI. El owner lo decidió con la conciencia de que es mesa de entrada del mercado.
- STRATEGIC: HubSpot-first va hacia donde PayClarity (competidor) ya es fuerte. El diferenciador de Wasnie (servir a quien NO está en HubSpot) se preserva vía la capa de abstracción + seguir soportando import por Excel. La pregunta de mercado "¿cuántos de mis clientes europeos usan HubSpot?" sigue abierta y se responde hablando con clientes.
- El Excel import existente NO se elimina — es el camino para los que no usan HubSpot y el fallback.
