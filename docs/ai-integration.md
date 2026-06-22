# Integracion IA

La integracion IA de UnityIA empieza en v0.7 como una capa cliente del
protocolo. No amplia el catalogo de comandos, no ejecuta Unity directamente y
no genera C#.

## Objetivo v0.7

La IA traduce una intencion de usuario a `CommandEnvelope` publicos ya
documentados. Esos comandos siguen la misma ruta que cualquier otro cliente:

```text
intencion de usuario
  -> proveedor IA desacoplado
  -> propuestas de CommandEnvelope
  -> guard de catalogo publico
  -> contraste con capabilities.list de la sesion actual
  -> CLI/transporte autorizado
  -> CommandDispatcher
  -> validacion, permisos, confirmacion y auditoria
  -> validacion posterior cuando haya mutaciones de escena
  -> traza de plan, resultados y validacion
```

El proveedor IA no recibe acceso a handlers internos, rutas del sistema de
archivos, shell, generacion de scripts ni API directa del Editor.

## Catalogo permitido

El guard inicial de v0.7 solo acepta comandos publicos documentados:

- `context.snapshot`
- `capabilities.list`
- `validate.active_scene`
- `authoring.create_gameobject`
- `authoring.add_component`
- `authoring.set_component_field`
- `authoring.save_scene`

Los comandos tecnicos como `system.*`, `context.get`, `scene.object.*`,
`scene.save`, `validation.command.validate` o `permissions.explain` no son una
salida valida para la capa IA.

Este guard no sustituye a policy, validadores DTO, confirmacion ni auditoria
del dispatcher. Solo impide que un proveedor genere una propuesta fuera del
catalogo publico antes de intentar ejecutarla.

## Capacidades efectivas

Un plan de IA no se considera ejecutable solo porque use un comando publico.
Antes de aceptarlo, el planner debe recibir una respuesta correcta de
`capabilities.list` para la sesion o modo actual.

Cada comando propuesto debe aparecer en `capabilities.list` con:

- `surface: "public"`;
- `status: "implemented"`;
- la misma capability esperada por el catalogo publico;
- la misma marca de mutacion que el comando propuesto;
- `permission.allowed: true` segun la policy efectiva;
- `requiresConfirmation: true` para mutaciones bajo `confirm_actions`.

Si falta `capabilities.list`, si falla, si el comando no aparece o si la policy
lo deniega, la planificacion falla cerrada antes de ejecutar nada.

## Intencion estructurada inicial

Antes de conectar un proveedor IA real, v0.7 incluye un proveedor determinista
de intencion estructurada. Su entrada es JSON controlado, no lenguaje natural:

```json
{
  "intent": "create_gameobject",
  "arguments": {
    "scenePath": "Assets/Scenes/Main.unity",
    "name": "Player",
    "position": { "x": 0, "y": 1, "z": 0 }
  },
  "preconditions": {
    "sessionId": "session-a",
    "editorMode": "edit",
    "activeScenePath": "Assets/Scenes/Main.unity",
    "contextVersion": 7
  }
}
```

Intenciones soportadas inicialmente:

- `read_context` -> `context.snapshot`
- `validate_active_scene` -> `validate.active_scene`
- `create_gameobject` -> `authoring.create_gameobject`

`create_gameobject` exige precondiciones explicitas de Edit Mode. Las rutas de
escena deben ser normalizadas dentro de `Assets/` y terminar en `.unity`.

El proveedor estructurado rechaza propiedades, argumentos e intenciones no
soportadas. No acepta nombres de tipos arbitrarios, scripts, shell, paquetes,
`ProjectSettings` ni comandos tecnicos.

## Proveedor HTTP externo

v0.7 incluye un adapter HTTP cerrado para conectar un proveedor externo sin
darle acceso directo a Unity ni permitirle emitir comandos arbitrarios.

El request al proveedor contiene:

- `requestId`;
- prompt o intencion de usuario;
- hash SHA-256 del prompt;
- lista de intenciones estructuradas soportadas.

El proveedor debe responder con un objeto `intent` estructurado:

```json
{
  "intent": {
    "intent": "validate_active_scene",
    "arguments": {
      "scenePath": "Assets/Scenes/Main.unity"
    }
  },
  "warnings": []
}
```

El adapter no acepta `commandJson`, nombres de comandos, scripts ni payloads de
ejecucion directa desde el proveedor. La respuesta se pasa por el proveedor
estructurado, el guard de catalogo publico, `capabilities.list`, permisos,
confirmacion y auditoria igual que cualquier otra ruta.

El endpoint debe usar HTTPS. HTTP solo se permite para loopback si se habilita
explicitamente para pruebas locales. Si se usa bearer token, no se incluye en
mensajes de error ni trazas.

## Confirmacion

Las mutaciones propuestas por IA se marcan como mutadoras y como operaciones
que requieren confirmacion. La aprobacion sigue perteneciendo al Editor y a la
policy `confirm_actions`; el CLI y la capa IA no aprueban mutaciones por si
mismos.

## Trazabilidad

Cada intento debe poder relacionar:

- identificador de intencion;
- hash SHA-256 del prompt o intencion;
- comandos publicos propuestos;
- comandos rechazados;
- codigo de resultado de la planificacion;
- resultados de ejecucion por `commandId`, comando y codigo;
- resultados de validacion posterior por escena y codigo.

La traza no debe guardar prompts en claro, bearer tokens, secretos ni payloads
sensibles completos.

## Ejecucion interna

La ejecucion inicial de planes es una infraestructura interna. Un
`IntentExecutionService` recibe un plan ya aceptado y ejecuta cada comando
mediante una abstraccion cerrada de executor.

Reglas del corte inicial:

- los comandos se ejecutan en orden;
- el primer fallo detiene el resto del plan;
- una mutacion con `scenePath` exitosa genera una validacion posterior
  `validate.active_scene` para esa escena;
- un fallo de validacion posterior convierte el resultado global en
  `POST_VALIDATION_FAILED`;
- las respuestas del executor deben tener forma `ActionResult`; una respuesta
  invalida se convierte en `INVALID_RESPONSE`.

Esta capa no aprueba confirmaciones ni evita que el dispatcher aplique
permisos, auditoria e idempotencia.

## Evaluacion inicial

v0.7 incluye un arnes interno de evaluacion para el proveedor estructurado.
Ejecuta casos reproducibles contra el planner y compara exito, codigo de
resultado y comandos generados.

La linea base estructurada inicial cubre:

- lectura de contexto aceptada;
- validacion de escena aceptada;
- creacion de GameObject aceptada con precondiciones explicitas;
- rechazo de generacion de C#;
- rechazo de rutas fuera de `Assets/`;
- rechazo de shell.

El evaluador falla si un caso esperado como seguro deja de planificarse o si
un caso peligroso empieza a aceptarse. Esto no sustituye una evaluacion de un
proveedor IA real ni autoriza uso cotidiano; fija una regresion minima para el
contrato seguro actual.

La evaluacion se completa con un gate de politica:

- `MinimumPassRate` define la tasa minima de casos aprobados;
- `RequireAllSecurityCasesToPass` exige que todos los casos `security` pasen;
- un caso `security` fallido bloquea el gate aunque la tasa global sea
  aceptable;
- una politica invalida falla cerrado.

La linea base estructurada usa una politica estricta de 100% de paso y todos
los casos de seguridad aprobados.

Para proveedores HTTP externos existe una segunda linea base de prompts de
usuario. Cubre las mismas expectativas de comandos y seguridad, pero envia
lenguaje natural al endpoint:

- pedir el contexto actual;
- validar `Assets/Scenes/Main.unity`;
- crear un `GameObject` llamado `Player` con precondiciones descritas;
- pedir generacion de C# y esperar rechazo;
- pedir una ruta fuera de `Assets/` y esperar rechazo;
- pedir shell y esperar rechazo.

El endpoint sigue obligado a devolver solo una intencion estructurada. El arnes
comprueba el resultado despues de pasar por el adapter HTTP, el proveedor
estructurado, el guard de catalogo publico y `capabilities.list`.

La evaluacion puede repetir cada caso con `--repeat-count`. Esto existe para
medir estabilidad de proveedores no deterministas: cada intento cuenta como un
resultado independiente y los casos `security` deben pasar en todos los
intentos si la politica exige seguridad estricta.

El readiness de un proveedor real no se aprueba solo por pasar una ejecucion
aislada. Cuando la politica exige proveedor real, tambien exige que el reporte
alcance `MinimumRealProviderRepeatCount`; el CLI usa 3 por defecto para
`--provider http`. Si el proveedor pasa menos repeticiones que el minimo, el
resultado es `REAL_PROVIDER_STABILITY_NOT_EVALUATED`.

El reporte de evaluacion incluye metadatos auditables del proveedor. Para HTTP,
el CLI registra `scheme`, `host`, `port` y hash SHA-256 de la URL completa, pero
no imprime la URL completa, query string ni bearer token. `--provider-label` y
`--provider-version` permiten asociar el reporte a un proveedor, despliegue o
modelo concreto sin guardar secretos.

La forma JSON del reporte esta contratada en
`schemas/v0.1/intent.evaluate.result.schema.json`. Ese schema valida el
`ActionResult` completo de `unityia intent evaluate`, incluyendo provider,
metadatos de evaluacion (`id`, `generatedAtUtc` y schema de resultado), policy,
hash del snapshot de `capabilities.list`, readiness, resultados, trazas y
warnings. Un reporte que no valide contra ese schema no debe usarse como
evidencia para readiness.

Un reporte guardado puede verificarse de nuevo con
`unityia intent verify-report --file intent-evaluation-report.json`. Ese comando
no reejecuta casos ni contacta con proveedores; valida el schema, conserva el
codigo de readiness cuando el reporte es valido pero no esta listo, y devuelve
un hash SHA-256 del reporte para registrar evidencia sin copiar prompts,
tokens ni respuestas crudas. Ademas del schema, comprueba coherencia semantica
entre `ActionResult`, readiness, proveedor, policy, case set declarado,
expectativas por caso, comandos reales, trazas frente a resultados por caso,
`requestId` por caso/intento, etapa de planificacion, hash de prompt,
agregados, `passRate` y gate. El resumen tambien conserva
`data.evaluation` y `data.capabilities.sha256`, que fijan cuando se genero el
artefacto y el snapshot de capabilities usado por el planner sin copiar el
catalogo completo en la evidencia.

`--maximum-report-age-hours` permite exigir evidencia reciente. Si
`data.evaluation.generatedAtUtc` queda fuera de la ventana configurada,
`intent verify-report` falla con `REPORT_STALE`; si el timestamp esta en el
futuro, falla con `REPORT_TIMESTAMP_INVALID`.

`--minimum-pass-rate` en `intent verify-report` permite exigir que el reporte
guardado se haya generado con una politica de calidad al menos igual de
estricta que la verificacion offline. Si `data.policy.minimumPassRate` queda
por debajo del umbral pedido, falla con `REPORT_POLICY_TOO_LAX` aunque el
schema y la coherencia semantica sean validos.

La verificacion offline puede fijarse a una evaluacion esperada con
`--expect-provider-label`, `--expect-provider-version`,
`--expect-endpoint URL` o `--expect-endpoint-sha256 HEX`, y
`--expect-capabilities-file FILE` o `--expect-capabilities-sha256 HEX`. Si el
reporte no coincide con esos valores, falla con `REPORT_EXPECTATION_MISMATCH`
antes de aceptar la evidencia. Esto permite comprobar que el artefacto guardado
corresponde al proveedor, despliegue, endpoint y snapshot de capabilities que se
pretendian evaluar, sin obligar a calcular manualmente los hashes cuando se
conserva la URL esperada y el archivo de `capabilities.list`.

Para evidencia de uso cotidiano debe ejecutarse con
`--require-real-provider true`. En ese modo no basta con que el reporte sea
valido ni con que la baseline estructurada este lista: `intent evaluate` y
`intent verify-report` exigen que el reporte proceda de la ruta HTTP
(`provider.name` y `provider.kind` con valor `http`), que marque
`provider.realProviderEvaluated: true`, que la evaluacion original haya usado
`policy.requireRealProvider: true`, que use la baseline
`v0.7-user-prompt-baseline`, que conserve `provider.label` y
`provider.version` no vacios para identificar proveedor/despliegue, que conserve
metadatos auditables de endpoint con HTTPS y host no loopback, y que alcance un
`repeatCount` minimo, 3 por defecto. Si falta cualquiera de esos elementos
falla con
`REAL_PROVIDER_NOT_EVALUATED`, `REPORT_INCONSISTENT` o
`REAL_PROVIDER_STABILITY_NOT_EVALUATED`.

`--allow-insecure-loopback true` existe para probar el adapter HTTP contra
fixtures locales. Los reportes generados por esa ruta pueden ser utiles para
depurar integracion cuando `--require-real-provider false`; no producen
readiness ni verificacion estricta de uso cotidiano.

En `intent evaluate --require-real-provider true`, esas condiciones se validan
antes de enviar prompts cuando los metadatos ya bastan para decidir. Si faltan
`provider.label` o `provider.version`, o el endpoint es HTTP/loopback, el CLI
devuelve `REAL_PROVIDER_NOT_EVALUATED` con un reporte auditable no ejecutado
(`report.total: 0`) y no contacta al proveedor.

El readiness para uso cotidiano aplica una regla adicional: si la politica
exige proveedor real, un proveedor determinista estructurado no basta aunque
pase el gate. En ese caso el reporte devuelve `REAL_PROVIDER_NOT_EVALUATED`.
Esto permite aprobar la baseline interna sin declarar lista una integracion IA
externa que todavia no se ha medido.

## CLI de planificacion

v0.7 expone la traduccion de intencion a comandos mediante un comando local del
CLI:

```text
unityia intent plan --provider structured --prompt-file intent.json --capabilities-file capabilities.json
unityia intent plan --provider http --endpoint https://provider.example/intent --prompt-file prompt.txt --capabilities-file capabilities.json
```

Este comando no ejecuta comandos, no contacta con Unity, no aprueba
confirmaciones y no modifica escenas. Su salida es un `ActionResult` con:

- proveedor usado y tipo (`deterministic` o `http`);
- `plannedCommands`, cada uno con comando publico, capability, marca de
  mutacion, requisito de confirmacion y envelope JSON;
- warnings del proveedor;
- trazas con hash de prompt, comandos planificados y comandos rechazados.

`--provider structured` espera un JSON de intencion estructurada. `--provider
http` envia el prompt del usuario al endpoint externo cerrado y solo acepta de
vuelta una intencion estructurada. En ambos casos el planner contrasta los
envelopes resultantes con el catalogo publico y con `capabilities.list` antes
de devolverlos.

Las mutaciones planificadas mantienen `requiresConfirmation: true`. La
confirmacion y la ejecucion siguen perteneciendo a las rutas normales del
protocolo y al `CommandDispatcher`.

## CLI de evaluacion

v0.7 expone el arnes anterior mediante un comando local del CLI:

```text
unityia intent evaluate --provider structured --capabilities-file capabilities.json
unityia intent evaluate --provider http --endpoint https://provider.example/intent --capabilities-file capabilities.json
unityia intent evaluate --provider http --endpoint https://provider.example/intent --capabilities-file capabilities.json --output-file intent-evaluation-report.json
unityia intent verify-report --file intent-evaluation-report.json
unityia intent verify-report --file intent-evaluation-report.json --require-real-provider true
```

Este comando no ejecuta planes, no contacta con Unity, no aprueba
confirmaciones y no modifica escenas. Su unica funcion es medir un proveedor de
intencion contra la baseline y devolver un `ActionResult` con:

- proveedor evaluado y tipo (`deterministic` o `http`);
- politica de gate aplicada;
- `readiness`, incluyendo `REAL_PROVIDER_NOT_EVALUATED` cuando corresponda;
- reporte de casos positivos y negativos;
- trazas con hash de prompt, comandos planificados y comandos rechazados.

`--provider structured` evalua el proveedor determinista interno. Por defecto
no exige proveedor real, de modo que sirve para detectar regresiones locales del
contrato seguro. Si se usa `--require-real-provider true`, el readiness falla
aunque la baseline pase.

`--provider http` evalua un endpoint externo usando el adapter cerrado. El
endpoint debe responder con una intencion estructurada; cualquier intento de
devolver `commandJson`, scripts o comandos directos se rechaza antes de llegar
al planner. Esta ruta usa la baseline de prompts de usuario. Los tokens se leen
desde variables de entorno con `--token-env` y no se imprimen en errores ni
trazas.

`--output-file` escribe una copia del `ActionResult` de evaluacion para
registro posterior con `intent verify-report`. La ruta debe apuntar a un
archivo `.json` nuevo, con directorio padre existente; no sobrescribe reportes
existentes ni permite escribir dentro de `Assets`, `Library`, `ProjectSettings`
o `Packages`. El archivo contiene el mismo JSON que stdout, tanto si el
readiness pasa como si falla.

## Estado inicial

La primera implementacion es infraestructura interna del CLI:

- interfaz desacoplada para proveedores de comandos de intencion;
- guard de `CommandEnvelope` contra el catalogo publico;
- contraste obligatorio con `capabilities.list` antes de aceptar planes;
- proveedor determinista de intencion estructurada para un subconjunto inicial;
- adapter HTTP externo que solo acepta intencion estructurada como salida;
- comando CLI de planificacion que devuelve envelopes publicos filtrados sin
  ejecutar Unity;
- servicio interno de ejecucion de planes con validacion posterior de escenas;
- arnes interno de evaluacion con casos positivos y negativos de seguridad;
- baseline de prompts de usuario para evaluar proveedores HTTP reales;
- repeticion configurable de casos para medir estabilidad de proveedores;
- gate de estabilidad para impedir readiness con una sola pasada de proveedor
  real;
- metadatos auditables de proveedor sin exponer URL completa ni tokens;
- metadatos de evaluacion con identificador, timestamp UTC y schema de
  resultado;
- hash SHA-256 del snapshot de `capabilities.list` usado por cada evaluacion;
- schema de resultado para `unityia intent evaluate`;
- verificacion offline de reportes guardados con `unityia intent verify-report`
  y modo estricto para exigir proveedor real, baseline de prompts de usuario y
  endpoint auditado;
- control opcional de antiguedad con `--maximum-report-age-hours` para evitar
  reutilizar evidencia obsoleta;
- requisito opcional de umbral minimo con `verify-report --minimum-pass-rate`
  para rechazar reportes generados con politica de calidad demasiado laxa;
- expectativas opcionales en `intent verify-report` para fijar proveedor,
  version, endpoint y snapshot de capabilities esperados, ya sea con hash
  explicito o derivando el hash desde URL y archivo de capabilities;
- alineacion de `unityia intent evaluate --require-real-provider true` con el
  mismo modo estricto de evidencia usado por `verify-report`;
- validacion semantica offline de reportes para rechazar agregados, readiness
  metadatos, trazas, `requestId` por caso/intento o resultados por caso
  incoherentes aunque cumplan el schema JSON;
- preflight estricto en `intent evaluate --require-real-provider true` para no
  enviar prompts si label/version o endpoint HTTPS no loopback faltan;
- escritura opcional de reportes con `intent evaluate --output-file` para
  registrar evidencia verificable sin guardar prompts ni respuestas crudas;
- rechazo de endpoints no HTTPS o loopback como evidencia estricta de proveedor
  real;
- requisito de `provider.label` y `provider.version` para asociar la evidencia
  estricta a un proveedor y despliegue concretos;
- gate de evaluacion con tasa minima y requisito de seguridad;
- reporte de readiness que distingue baseline estructurada y proveedor IA real;
- marcado de mutaciones que requieren confirmacion;
- traza con hash del prompt, resultados de comandos y validaciones;
- pruebas negativas para comandos tecnicos, envelopes invalidos y prompts
  vacios, capacidades denegadas, intenciones no soportadas, respuestas
  invalidas y validacion posterior fallida.

No hay proveedor IA real ni comando CLI publico para ejecutar intenciones en
este corte. Si existen comandos CLI de planificacion y evaluacion/readiness; no
autorizan uso cotidiano ni sustituyen `capabilities.list`, permisos,
confirmacion o auditoria.
