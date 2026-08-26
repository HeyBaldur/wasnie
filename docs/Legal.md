# Wasnie — Estado de compliance GDPR (documento vivo)

> ## ⚠️ QUÉ ES Y QUÉ NO ES ESTE DOCUMENTO
>
> **Esto es un TABLERO DE ESTADO INTERNO del equipo.** Sirve para saber, en cualquier momento, qué se
> auditó, qué brechas siguen abiertas, qué se decidió y qué falta.
>
> **Esto NO es:**
> - **NO es asesoría legal.** Nadie que haya escrito acá es abogado.
> - **NO es el Anexo Técnico Legal** que se le entrega al abogado. Ese documento se redacta **DESPUÉS**
>   de cerrar las brechas de nivel 1 — presentarle a un abogado una infracción documentada y abierta es
>   quemar dinero y honorarios.
> - **NO es un registro de tratamiento (ROPA), ni una DPIA, ni una política de privacidad.**
>
> Cuando las brechas de nivel 1 estén cerradas, este documento pasa a ser el **insumo** del anexo legal,
> no el anexo mismo.

**Última actualización:** 2026-08-26
**Alcance:** el asistente de IA de Wasnie y su flujo de datos hacia el proveedor de LLM.
**Fuera de alcance:** el resto del producto (importaciones, HubSpot, Stripe, emails) — no auditado bajo
esta óptica todavía.

---

## 1. Propósito y alcance

Este tablero cubre **exclusivamente** el flujo de datos del asistente de IA: qué sale de la aplicación
hacia un proveedor de LLM de terceros, con qué protecciones y con qué huecos.

**No cubre** (y por lo tanto **no debe leerse como que está limpio**): la ingesta por Excel, la
integración con HubSpot, el procesamiento de pagos con Stripe, el envío de emails transaccionales
(Resend), ni las transferencias de datos que esos subsistemas puedan implicar. Cada uno necesitaría su
propia auditoría.

### Procedencia de los hechos

| Origen | Qué aporta | Confiabilidad |
|---|---|---|
| **Auditoría de código, 2026-08-04** | Secciones 2, 3 (brechas técnicas), 5 | Verificado línea por línea contra el código; cada hecho lleva `archivo:línea` |
| **Configuración de la cuenta de OpenRouter** | Sección 6 | ⚠️ **Reportado por Rodolfo desde el panel del proveedor — NO verificable desde el repositorio** |
| **Decisiones de producto** | Mitigaciones y descartes de secciones 3 y 4 | Decisión de Rodolfo, registrada acá |

> **Regla de este documento:** si un dato no está verificado, se marca como pendiente de su fuente. **No
> se supone nada.** Una ausencia declarada es un dato válido; una suposición es un pasivo.

---

## 2. Estado del flujo de datos — lo que SÍ está bien

Hechos de la auditoría del **2026-08-04** (detalle completo en `SESSION_LOG.md`, entrada del 2026-08-04).

### 2.1 Aislamiento de inquilino (multi-tenant)

La herramienta de lectura del asistente **no tiene acceso directo a la base de datos**: emite las mismas
consultas MediatR que usan las pantallas, dentro del scope del request, así que el filtro de tenant y las
guardas de permisos se aplican solos (`GetTransactionTool.cs`, sin `DbContext` en el archivo — es una
regla explícita del diseño).

**El rechazo es indistinguible:** "no existe esa transacción" y "existe pero no es tuya" devuelven un
payload **byte a byte idéntico**. Distinguirlos confirmaría que la referencia existe, que es justamente el
dato que un atacante busca.

Cubierto por tests:
- `A_user_CANNOT_read_a_transaction_belonging_to_another_tenant`
- `Not_found_and_not_allowed_are_BYTE_IDENTICAL`
- `Unreadable_arguments_refuse_the_same_way_as_everything_else`

(en `Wasnie.UnitTests/Application/AssistantGetTransactionToolTests.cs`)

### 2.2 Barrera de autenticación (JWT)

`Program.cs:51-61` — **las cuatro validaciones activas**:

| Comprobación | Estado | Valor |
|---|---|---|
| Firma | ✅ `ValidateIssuerSigningKey = true` | HMAC-SHA256 (clave simétrica) |
| Caducidad | ✅ `ValidateLifetime = true` | **`ClockSkew = TimeSpan.Zero`** — sin la tolerancia de 5 min por defecto |
| Emisor | ✅ `ValidateIssuer = true` | `WasnieApi` |
| Audiencia | ✅ `ValidateAudience = true` | `WasnieUi` |

- Vida del access token: **15 minutos** (`JwtSettings:ExpiryMinutes`).
- `UseAuthentication()` → `UseAuthorization()` corren **antes** de llegar al handler (`Program.cs:263-264`).
- El asistente suma dos guardas propias: `entitlement.RequireAsync` y `OwnedConversations.FindMineAsync`
  (`StreamAssistantReplyHandler.cs:50-53`).

### 2.3 Minimización de datos en el DTO de la herramienta

**La minimización SÍ está ejercida** en el payload que la herramienta `get_transaction` entrega al modelo.
Se omite deliberadamente:

| Fuente | Campos que viajan | Campos omitidos |
|---|---|---|
| `TransactionDto` | 15 de 24 | **9** |
| `CreditListDto` | 6 de 16 | **10** |
| `PayoutDto` / `PayoutDetailDto` | 5 de 17 | **12** |

- **Ningún identificador interno (Guid) llega al modelo:** `CreditId`, `PayeeId` y `PlanId` se calculan
  para armar la respuesta pero están marcados `[property: JsonIgnore]` (`GetTransactionTool.cs:402-404`).
- **Ningún actor llega al modelo:** `IngestedBy`, `CalculatedBy`, `UpdatedBy`, `CancelledBy` quedan fuera.
- Las `Lines[]` del payout se recorren **en memoria** para localizar el crédito y **no se serializan**.

> **★ La distinción que importa para el abogado:** el hueco de PII **no está en este DTO**, que está bien
> minimizado. Está en (a) dos campos concretos que sí viajan (§3.2) y (b) el input libre del usuario
> (§3.3). Son riesgos de naturaleza distinta y se mitigan distinto.

---

## 3. 🚨 BRECHAS ABIERTAS — nivel 1 (bloqueantes para el mercado europeo)

### 3.1 🚨 DPA AUSENTE — **RELEASE BLOCKER**

**Estado:** ABIERTA · **Decisión pendiente de Rodolfo**

No hay un **Data Processing Agreement** firmado con el proveedor de IA. Sin DPA, el envío de datos
personales de residentes de la UE a un encargado del tratamiento **es ilegal bajo el Art. 28 del GDPR**,
con independencia de qué toggles de privacidad estén activados en el panel del proveedor.

> **Este es el punto que hay que entender bien:** los controles técnicos de la §6 **no sustituyen al
> DPA**. Reducen el riesgo técnico; **no crean la base jurídica**. Con los toggles perfectos y sin DPA,
> la transferencia sigue siendo una infracción.

**Caminos sobre la mesa (decisión de negocio, no técnica):**

| | Camino | Qué implica |
|---|---|---|
| **A** | OpenRouter Enterprise | Mantener el proveedor actual y contratar el tier que ofrece DPA |
| **B** | Agregador EU-first con DPA base + residencia UE | p. ej. Mistral La Plateforme EU, o Azure OpenAI EU |

**Bloquea a:** el lanzamiento en la UE, y la redacción del anexo legal.

### 3.2 🚨 PII ESTRUCTURAL — `payeeName` + `payeeEmployeeCode` salen en cada lookup

**Estado:** ABIERTA · **Mitigación decidida** · **Prioridad 1**

**Riesgo estructural, no probabilístico.** Cada vez que el asistente consulta una transacción, el
**nombre real del empleado** y su **código de empleado** viajan al proveedor — **por diseño, no por
accidente**, y con independencia de lo que el usuario haya escrito. No hace falta que nadie se equivoque
para que esto ocurra: ocurre siempre.

**Mitigación decidida — hidratación cliente-side:**
1. El backend envía al modelo un **`payeeId` opaco** en lugar del nombre.
2. Angular **re-sustituye el nombre** desde su caché local antes de renderizar.
3. El dato personal **nunca sale a la red del LLM**; el usuario sigue viendo el nombre en pantalla.

**No construido todavía — WI aparte.**

---

## 3bis. ⚠️ BRECHAS ABIERTAS — nivel 2

### 3.3 ⚠️ INPUT LIBRE SIN DLP

**Estado:** ABIERTA · **Mitigación identificada** · **Prioridad 2**

**No existe ningún middleware, regex, servicio de sanitización ni NER que oculte o anonimice nombres,
correos o datos sensibles antes de enviarlos al LLM. El texto del usuario viaja tal cual.**

Cadena verificada hop por hop, sin una sola transformación:

| # | Punto | Qué hace con el texto | Evidencia |
|---|---|---|---|
| 1 | Store Angular | `content.trim()` y nada más | `assistant.store.ts:132` |
| 2 | Servicio HTTP | `JSON.stringify({ content, isRetry })` | `assistant.api.service.ts:63` |
| 3 | Handler | `var question = userMessage.Content;` | `StreamAssistantReplyHandler.cs:135` |
| 4 | Paso 1 — router | `new ChatMessage(UserRole, question)` | `AssistantSectionRouter.cs:68` |
| 5 | Paso 1.5 — tool | `new ChatMessage(UserRole, question)` | `AssistantToolRunner.cs:53` |
| 6 | Paso 2 — generación | historial completo, contenido intacto | `AssistantPrompt.cs:351-353` |
| 7 | Serialización HTTP | `m.Content` → `JsonSerializer.Serialize` | `OpenAiCompatibleChatProvider.cs:148,307` |

Barridos negativos (`sanitiz`, `anonymi`, `redact`, `scrub`, `mask`, `pii`, `dlp`, `obfusc`) sobre todo
`WasnieApi/src`: **cero coincidencias en el camino del asistente**. Los únicos aciertos son `MaskEmail` en
**logs** de recuperación de contraseña y el stripping de espacios en códigos 2FA — ninguno toca el prompt.
El `DomSanitizer` del pipe de Markdown es protección **XSS sobre la respuesta entrante**, no protección
del dato saliente. **Cero `DelegatingHandler` en todo el repositorio**, así que nada puede reescribir el
cuerpo de la petición en vuelo.

**★ Agravante: no viaja solo la pregunta, viaja el HILO.** El paso 2 envía hasta
**`MaxHistoryMessages = 20`** mensajes previos (del usuario **y** del asistente) con el contenido intacto.
El único filtro es descartar el placeholder de "no conectado".

**Ejemplo del riesgo:** *"¿por qué la comisión de Juan Pérez, que estuvo de baja por depresión en mayo, es
tan baja?"* → nombre + **dato de salud (categoría especial, Art. 9 GDPR)** → sale al proveedor.

**Riesgo probabilístico** (depende de que un usuario escriba algo sensible), a diferencia de §3.2 que
ocurre siempre. **Mitigación: filtro DLP. No construido — WI aparte.**

### 3.4 ⚠️ SIN LOG DE AUDITORÍA DEL LLM

**Estado:** ABIERTA · **Mitigación identificada** · **Prioridad 2**

**Hoy es imposible reconstruir, ante un incidente, qué se envió al LLM, cuándo y por qué usuario.**

Dos causas **independientes** (cerrar una no alcanza):
1. Ningún `AuditActions` de assistant/chat/LLM, y **ningún comando del asistente implementa
   `IAuditableCommand`**, así que `AuditBehavior` cortocircuita en su primera línea
   (`AuditBehavior.cs:20-21`).
2. **Aunque lo implementara**, `StreamAssistantReplyCommand` es `IStreamRequest` y `IPipelineBehavior` no
   intercepta requests de streaming en MediatR.

**Lo que sí existe hoy son logs de aplicación, no auditoría:** Serilog a consola y archivo
(`logs/wasnie-.log`), rotación diaria, **retención 30 días**, formato JSON. Registran nombre de
herramienta y desenlace, y **deliberadamente NO registran ni la pregunta ni el número de referencia**
(decisión de diseño previa, documentada en `GetTransactionTool.cs:197-198`).

**Mitigación: log de auditoría inmutable. Requiere tocar el manejo de SSE** (por la causa 2). **No
construido — WI aparte.**

---

## 4. Riesgos evaluados y DESCARTADOS

Se listan con su justificación para que nadie los vuelva a abrir sin argumento nuevo.

### 4.1 Revocación del access token dentro de su ventana de 15 minutos — **DESCARTADO**

**Hecho:** la validación del JWT es **puramente criptográfica** — no hay lista de revocación ni consulta
contra la base. Un token robado sirve **hasta que caduca**.

**Por qué se descarta construir la mitigación:** una lista negra con estado destruye el propósito de la
autenticación stateless, agregando una consulta a base en cada request. **Una ventana de 15 minutos es un
riesgo estándar aceptado en la industria**, siempre que el refresh token sí sea revocable.

**Se documenta, no se construye.** Si un abogado o auditor lo cuestiona, esta sección es la respuesta.

---

## 5. Cadena de subencargados (relevante para el DPA)

**OpenRouter es un agregador, no un proveedor final:** recibe la petición y la **reenvía a un vendor
subyacente** que ejecuta el modelo. Eso significa que la cadena de subencargados tiene **al menos dos
eslabones**, y el DPA debe cubrirlos a ambos.

Configuración activa en el momento de la auditoría:

| | |
|---|---|
| Proveedor | `Assistant:Provider = OpenRouter` |
| Endpoint | `https://openrouter.ai/api/v1` |
| Modelo | `openai/gpt-oss-20b` |
| Jurisdicción | **Estados Unidos** |

**Verificado y favorable:** los headers de atribución que Wasnie añade (`HTTP-Referer`, `X-Title`) llevan
**solo el nombre del producto y una URL pública** — sin secretos y sin datos de usuario
(`OpenRouterChatProvider.cs:48-54`). Se comprobó en lugar de asumirse.

**Nota:** existe un segundo proveedor implementado (Groq, también EEUU), seleccionable por configuración.
Cualquier DPA debe contemplar qué proveedores quedan habilitados.

**⚠️ PENDIENTE:** identificar **qué vendor downstream** sirve efectivamente `openai/gpt-oss-20b` a través
de OpenRouter. No es determinable desde el repositorio; hay que obtenerlo del proveedor.

### 5.1 La selección de proveedor es explícita y obligatoria (2026-08-26)

Hasta el 2026-08-26 `Assistant:Provider` **tenía un valor por defecto** (`Groq`), en dos lugares
independientes: el `appsettings.json` **base** y el propio valor por defecto de la clase de opciones. La
resolución además caía a Groq ante un valor **no reconocido**. El resultado: cualquier entorno que no
cargara su override —un staging nuevo, un contenedor mal armado, una variable de entorno faltante, un
`Assistant:Provider` mal escrito— enviaba datos hacia Groq **en silencio**, sin que nadie lo hubiera
elegido. Esta sección describía a OpenRouter como el proveedor activo mientras el archivo base decía
Groq.

**Corregido (FAIL-CLOSED).** No hay valor por defecto en ninguno de los dos lugares. Si la clave falta,
está vacía o no se reconoce, **la API no arranca**, con un mensaje que nombra la clave y los valores
admitidos. La comprobación vive en la resolución misma (`DependencyInjection.cs`), que corre al construir
el host — lo descubre el despliegue, no un usuario preguntando por su comisión.

Selección declarada por entorno, tras el cambio:

| Entorno | `Assistant:Provider` |
|---|---|
| `appsettings.json` (base) | **ausente a propósito** — no hay defecto |
| `appsettings.Development.json` | `OpenRouter` |
| `appsettings.Production.json` | `OpenRouter` |
| `appsettings.Development.template.json` | vacío, marcado como obligatorio |
| Host de tests de integración | `OpenRouter` (declarado explícitamente; sin clave, así que no sale nada) |

**⚠️ LO QUE ESTO NO ARREGLA.** Esto elimina la elección *por omisión*; **no elige proveedor ni crea base
jurídica**. La brecha §3.1 (DPA ausente) sigue abierta e igual de bloqueante.

**★ CONSECUENCIA PARA EL DPA:** Groq sigue implementado y sigue siendo seleccionable con una línea de
configuración. Que hoy ningún entorno lo seleccione **no lo saca de la cadena de subencargados**: el DPA
tiene que nombrar a **todos los proveedores habilitados en el código**, no solo al activo. La alternativa
—si se decide que Groq no debe poder elegirse— es quitarlo del código, y eso es una decisión de negocio
que este cambio deliberadamente no toma.

---

## 6. Estado de configuración de privacidad en el proveedor

> ⚠️ **PROCEDENCIA: reportado por Rodolfo desde el panel de OpenRouter el 2026-08-03. NO verificable desde
> el repositorio y NO verificado por la auditoría de código.** Antes de que esto entre en cualquier
> documento legal, hay que respaldarlo con una captura fechada o la confirmación escrita del proveedor.

| Control | Estado reportado |
|---|---|
| ZDR (Zero Data Retention) "Non-frontier" | Activado |
| Toggles de Data Training (los 4) | Apagados |
| Prompt logging | Apagado |

### Qué significa esto realmente

**Protege TÉCNICAMENTE. No protege LEGALMENTE.**

Estos controles reducen la probabilidad de que los datos se retengan o se usen para entrenar. **No crean
la base jurídica para la transferencia.** Sin el DPA de la §3.1, el envío de datos personales de la UE
sigue siendo una infracción del Art. 28 — con todos los toggles en la posición correcta.

Dicho de otro modo: la §6 es un buen argumento de **mitigación de daño**; no es una defensa de
**legalidad**.

---

## 7. Próximos pasos (en orden)

El orden importa: cada paso abarata el siguiente.

| # | Paso | Responsable | Estado |
|---|---|---|---|
| **1** | **Decisión de DPA / proveedor** (Camino A vs B, §3.1) | Rodolfo — decisión de negocio | ⏳ Pendiente |
| **2** | **Pseudonimización estructural** — hidratación cliente-side (§3.2) | Ingeniería, tras el paso 1 | ⏳ WI no creado |
| **3** | **Filtro DLP** (§3.3) **+ log de auditoría del LLM** (§3.4) | Ingeniería | ⏳ WI no creado |
| **4** | **Redactar el Anexo Técnico Legal y contratar al abogado** | Rodolfo + abogado | ⛔ **Bloqueado hasta cerrar 1-3** |

> **★ Por qué el paso 4 va último, y no primero:** presentarle a un abogado una infracción documentada y
> **abierta** convierte su trabajo en gestión de un incidente en lugar de revisión de un diseño. Es más
> caro, y el resultado es peor. Primero se cierran las brechas de nivel 1; después se paga por la revisión.

---

## 8. Bitácora de cambios

| Fecha | Cambio |
|---|---|
| 2026-08-26 | **§5.1 nueva** — la selección de proveedor pasa a ser explícita y obligatoria (FAIL-CLOSED). Se elimina el valor por defecto `Groq` del `appsettings.json` base y de la clase de opciones, y el fallback ante valor no reconocido; sin proveedor declarado la API no arranca. Corrige la contradicción entre esta §5 (que documentaba OpenRouter como activo) y el archivo base (que decía Groq). **No cambia de proveedor y no cierra la §3.1.** |
| 2026-08-04 | Documento creado a partir de la auditoría GDPR read-only del 2026-08-04 (§§2-5), la configuración del proveedor reportada el 2026-08-03 (§6) y las decisiones de producto registradas (§§3-4). **Cuatro brechas abiertas: 2 de nivel 1, 2 de nivel 2. Un riesgo descartado con justificación.** |

**Mantenimiento:** este documento es **vivo**. Cada vez que se cierre una brecha, se actualiza su estado
acá **en el mismo WI que la cierra**, junto con `PROJECT_STATUS.md` y `SESSION_LOG.md`. Un tablero de
estado desactualizado es peor que no tenerlo: se le cree.
