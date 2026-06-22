# Plan global de proyecto para UnityIA

Este documento es el plan maestro operativo de UnityIA. No sustituye a
`docs/roadmap.md`: lo convierte en una guia diaria para decidir que construir,
en que orden, que esta terminado y que no debe tocarse todavia.

## Reglas de mantenimiento de este plan

Este archivo solo puede modificarse para:

- marcar tareas como terminadas cuando cumplan su criterio de salida;
- anadir comentarios de estado, bloqueos, deuda tecnica o trabajo pendiente;
- registrar decisiones importantes descubiertas durante el desarrollo.

No debe reescribirse para justificar una implementacion ya hecha. Si el codigo
entra en conflicto con este plan, primero se documenta el conflicto como
comentario y despues se corrige el codigo, el plan o ambos de forma explicita.

## Vision del producto

UnityIA es un paquete de Unity mas un CLI externo para que una IA o herramienta
automatizada pueda hacer authoring en Unity de forma controlada, auditable,
reversible y verificable.

Ruta objetivo de cualquier accion:

```text
IA / agente / usuario
  -> unityia CLI o UI autorizada
  -> comando JSON versionado
  -> validacion de contrato
  -> permisos y confirmacion cuando aplique
  -> CommandDispatcher
  -> UnityIAAuthoringAPI o facade publica equivalente
  -> API publica del Unity Editor
  -> ActionResult serializable, auditoria y validacion posterior
```

Equivalencia conceptual:

- Unity Scene, Prefab y Asset son la fuente persistente de verdad.
- El estado del Editor es authoring editable.
- Play Mode es runtime temporal, no verdad persistente.
- `UnityIAAuthoringAPI` es la fachada publica para mutaciones autorizadas.
- `unityia` CLI es la entrada externa para agentes, CI y automatizacion.

## Principios obligatorios

1. La IA no edita Unity directamente. No toca YAML, `.meta`,
   `ProjectSettings`, `Packages`, `Library`, scripts arbitrarios ni archivos
   internos como mecanismo de authoring.
2. Toda mutacion pasa por una API publica de UnityIA, nunca por handlers
   internos, reflection abierta o acceso directo a serializacion de Unity.
3. Los comandos son pequenos, versionados, auditables e idempotentes cuando sea
   viable.
4. Toda operacion publica devuelve `ActionResult` con `success`, `message`,
   `code` y `data` con forma de objeto.
5. Las mutaciones validan contrato, estado, contexto, permisos y auditoria.
6. Las mutaciones de escena usan Undo cuando Unity lo permite y dejan la escena
   sucia; guardar siempre es un comando explicito.
7. La policy aplica denegacion por defecto. Una capacidad no declarada esta
   prohibida.
8. `confirm_actions` es el modo de autorizacion inicial para mutaciones.
9. `full_access`, shell arbitrario, generacion libre de C# y cambios de
   `ProjectSettings` o `Packages` quedan bloqueados hasta fases futuras
   explicitamente aprobadas.
10. La existencia de un prototipo en el repo no lo convierte en API estable.

## Estado actual detectado

Base existente en el repositorio:

- [x] Documentacion principal: `README.md`, `docs/architecture.md`,
  `docs/roadmap.md`, `docs/commands.md`, `docs/permissions.md`,
  `docs/glossary.md` y `docs/protocol/v0.1.md`.
- [x] Package Unity en `packages/com.unityia.authoring`.
- [x] Contratos base, `ActionResult`, `CommandEnvelope`, codigos de resultado
  y serializacion estricta.
- [x] `CommandDispatcher` con validacion base, permisos, auditoria, Undo e
  idempotencia por `commandId`.
- [x] Comandos tecnicos registrados para prototipo: `system.*`,
  `context.get`, `scene.list-open`, `scene.hierarchy.get`,
  `scene.object.get`, `scene.object.*`, `scene.save`,
  `validation.command.validate` y `permissions.explain`.
- [x] Live server HTTP loopback autenticado y CLI .NET `net8.0`.
- [x] Schemas JSON v0.1 y tests de contrato/protocolo.
- [x] Ventana tecnica de Editor para ejecutar JSON contra el dispatcher.

Estado no estable o pendiente:

- [x] El catalogo publico objetivo `authoring.*` tiene source/docs/tests,
  paso EditMode en sandbox Unity 6.3 LTS y fue verificado con SDK .NET 8.
- [x] `context.snapshot` tiene source/docs/tests y fue verificado con SDK
  .NET 8.
- [x] `capabilities.list` tiene source/docs/schema/tests y fue verificado con
  SDK .NET 8 y Unity 6.3 LTS.
- [x] `validate.active_scene` tiene source/docs/schema/tests y fue verificado
  con SDK .NET 8 y Unity 6.3 LTS.
- [x] Los comandos tecnicos `scene.object.*` quedan como prototipo y no deben
  declararse API estable ni competir con `authoring.*`.
- [x] El entorno local tiene SDK .NET 8.0.422 instalado en
  `C:\Users\usuario\.dotnet`; `dotnet build cli/UnityIA.sln` y
  `dotnet test cli/UnityIA.sln` pasan usando ese SDK.

Decision tecnica vigente:

- El CLI de este repo sigue siendo .NET 8, no Python.
- El transporte live vigente es HTTP loopback autenticado, no IPC por archivos.
- La idea inicial de Python/file-based IPC queda como antecedente historico,
  no como direccion tecnica para este repositorio.

## Orden global de desarrollo

### v0.1 - Foundation documental y protocolo base

Objetivo: que cualquier agente pueda entender limites, componentes, contratos
y orden de trabajo sin inventar capacidades.

- [x] Definir proposito, arquitectura y limites de seguridad.
- [x] Definir protocolo JSON base y `ActionResult`.
- [x] Documentar distincion entre contrato objetivo y prototipo existente.
- [x] Documentar comandos reservados y convenciones.
- [x] Documentar permisos, policy y denegacion por defecto.
- [x] Crear este plan maestro y `AGENTS.md`.
- [x] Revisar que todos los documentos de nivel superior enlacen entre si sin
  contradicciones.

Criterio de salida: `README.md`, docs principales, schemas y tests de contrato
explican el estado real sin declarar como estable una capacidad no terminada.

### v0.2 - Unity package base

Objetivo: tener una base tecnica de Editor-only package sobre la que construir
capacidades publicas.

- [x] Package `com.unityia.authoring` con `package.json`.
- [x] Assemblies separados para Contracts, Core, Context, Authoring, Transport,
  Testing y DeveloperUI.
- [x] `CommandDispatcher` como nucleo comun.
- [x] Registro explicito de handlers.
- [x] Validacion base de estado del Editor y precondiciones.
- [x] Auditoria y permisos basicos.
- [x] Ventana tecnica para ejecutar comandos JSON sin red.
- [x] Separar claramente handlers de prototipo de APIs publicas estables.
- [x] Documentar como instalar y probar el package en un proyecto Unity real.

Criterio de salida: el package compila en Unity, la ventana tecnica funciona y
ningun comando de prototipo se presenta como authoring publico final.

### v0.3 - Context y Authoring API publica

Objetivo: permitir authoring basico controlado desde APIs publicas y comandos
estables.

- [x] Definir y estabilizar `UnityIAContextAPI`.
- [x] Implementar `context.snapshot` como comando publico.
- [x] Definir y estabilizar `UnityIAAuthoringAPI`.
- [x] Implementar `authoring.create_gameobject`.
- [x] Implementar `authoring.add_component` con catalogo cerrado de
  componentes permitidos.
- [x] Implementar `authoring.set_component_field` con campos registrados y
  valores tipados.
- [x] Implementar guardado explicito de escena dentro del catalogo publico.
- [x] Reconciliar o adaptar `scene.object.*` para que no compita con
  `authoring.*`.
- [x] Cubrir Undo, escena sucia, errores de validacion y permisos con tests
  EditMode.

Criterio de salida: una IA puede crear un objeto, anadir componentes permitidos
y modificar campos permitidos sin tocar internals de Unity.

### v0.4 - CLI live bridge

Objetivo: permitir que un agente externo use el Editor abierto sin acceso
directo a archivos Unity.

- [x] CLI .NET `unityia` con comandos tecnicos iniciales.
- [x] Descubrimiento de sesiones live mediante descriptors.
- [x] Transporte HTTP loopback con bearer token.
- [x] Ejecucion en el hilo principal del Editor.
- [x] Estabilizar UX de CLI para `session list`, `status`, `commands` y
  `execute`, con seleccion explicita por `--project` o `--session`.
- [x] Definir futuras formas CLI `context`, `capabilities`, `validate` y
  `tests` en sus fases correspondientes.
- [x] Asegurar salida JSON consistente y exit codes documentados.
- [x] Anadir pruebas live CLI -> transport -> dispatcher; existen pruebas
  unitarias de parseo, seleccion de sesion, errores de transporte y respuestas
  invalidas, y verificacion manual live contra sandbox Unity.
- [x] Documentar diagnostico cuando Unity no esta abierto o hay multiples
  sesiones.

Criterio de salida: el CLI puede ejecutar comandos publicos en live mode y
devolver `ActionResult` sin exponer rutas o tokens sensibles.

### v0.5 - Batch mode y tests

Objetivo: ejecutar validacion y pruebas sin Editor interactivo, reutilizando
contratos y dispatcher.

- [x] Definir `UnityIABatchEntrypoint`.
- [x] Ejecutar comandos mediante Unity `-batchmode` y `-executeMethod`.
- [x] Implementar Test API para suites registradas.
- [x] Implementar `unityia tests run --mode EditMode`.
- [ ] Implementar `unityia tests run --mode PlayMode` cuando haya casos
  PlayMode reales.
- [x] Convertir resultados de Unity Test Framework a JSON legible para IA.
- [x] Rechazar batch si el proyecto esta bloqueado por una instancia de Unity
  incompatible.

Criterio de salida: CI o un agente puede lanzar tests y recibir resultados
estructurados con exit code correcto.

### v0.6 - Validation, capabilities y confirmacion

Objetivo: que permisos y capacidades puedan auditarse antes de integrar IA.

- [x] Implementar `validate.active_scene`.
- [x] Implementar validadores registrados para escena activa.
- [x] Implementar `capabilities.list` con estado por comando, modo y permiso.
- [x] Estabilizar schema definitivo de `.unityia/policy.json`.
- [x] Implementar `confirm_actions` para mutaciones.
- [x] Mostrar en UI comando, objetivo, capacidad y efecto esperado antes de
  aprobar.
- [x] Anadir pruebas negativas de seguridad, rutas bloqueadas y auditoria.
- [x] Documentar si Unity usara JSON Schema en runtime o solo validadores DTO.

Criterio de salida: un agente puede preguntar que puede hacer, por que se
permite o deniega y que validaciones fallan antes de tocar la escena.

### v0.7 - Integracion IA

Objetivo: conectar un proveedor de IA sin ampliar capacidades por el mero hecho
de usar IA.

- [x] Crear capa desacoplada para proveedor IA.
- [x] Traducir intencion de usuario a comandos publicos existentes.
- [x] Mantener confirmacion segun policy.
- [x] Registrar trazabilidad entre prompt, comandos, resultados y validacion.
- [x] Impedir que la IA genere capacidades fuera del catalogo.
- [ ] Evaluar seguridad y calidad antes de permitir uso cotidiano.

Criterio de salida: la IA opera como cliente del protocolo, no como acceso
libre al proyecto Unity.

### v0.8 - Recipes oficiales

Objetivo: ofrecer operaciones de alto nivel aprobadas que generen comandos
controlados.

- [ ] Definir contrato de recipe y resultado.
- [ ] Implementar `create_2d_player`.
- [ ] Implementar `create_collectible`.
- [ ] Implementar `create_platform`.
- [ ] Implementar `create_camera_follow`.
- [ ] Implementar `create_basic_platformer_scene`.
- [ ] Garantizar que cada recipe se expande a comandos publicos auditables.

Criterio de salida: la IA puede usar piezas oficiales en vez de inventar
secuencias de bajo nivel cada vez.

### v0.9 - Generacion controlada de scripts

Objetivo: permitir C# generado solo dentro de un flujo restringido,
compilable, auditable y revocable.

- [ ] Definir threat model especifico para generacion de scripts.
- [ ] Definir rutas permitidas dentro de `Assets/`.
- [ ] Generar scripts solo mediante comando publico dedicado.
- [ ] Compilar y rechazar automaticamente si hay errores.
- [ ] Bloquear APIs peligrosas y shell externo.
- [ ] Adjuntar scripts generados solo si compilan y pasan validacion.
- [ ] Mantener `full_access` desactivado salvo revision independiente.

Criterio de salida: la generacion de C# no puede usarse como escape para
saltar permisos, auditoria o APIs publicas.

## Reglas para declarar una capacidad terminada

Una capacidad solo puede marcarse como terminada si tiene:

- DTO o contrato JSON estable;
- schema o validador estricto;
- registro publico explicito;
- permiso/capacidad documentado;
- auditoria de solicitud y resultado;
- `ActionResult` estable;
- pruebas de contrato y comportamiento;
- documentacion de version que la entrega;
- casos negativos para errores importantes.

Si falta cualquiera de esos puntos, la capacidad puede existir como prototipo,
pero no debe anunciarse como publica ni estable.

## Comentarios de estado

- 2026-06-20: plan inicial creado a partir del estado real del repo. El trabajo
  inmediato debe consolidar v0.1/v0.2 y despues reconciliar `scene.object.*`
  con el catalogo publico `authoring.*`.
- 2026-06-20: primer corte publico v0.3 anadido en source/docs/schemas/tests:
  `context.snapshot`, `authoring.create_gameobject`,
  `authoring.add_component`, `authoring.set_component_field` y
  `authoring.save_scene`. No marcar v0.3 como completado hasta verificar los
  tests EditMode en un sandbox Unity 6.3 LTS y los tests .NET con SDK 8.
- 2026-06-20: docs principales enlazadas y texto con codificacion rota
  corregido en `docs/architecture.md` y `docs/cli.md`. `docs/development/sandbox.md`
  y el README del package documentan instalacion y pruebas del package.
- 2026-06-20: sandbox temporal con Unity 6000.3.17f1 creado mediante
  `tools/create-sandbox/Create-UnityIASandbox.ps1`. Los EditMode tests fallaron
  inicialmente porque `ContextVersion` no avanzaba al leer una seleccion recien
  cambiada en batchmode; se corrigio `EditorStateTracker` para sincronizar la
  seleccion al consultar `ContextVersion`.
- 2026-06-20: repeticion de EditMode tests en sandbox: 18 passed, 0 failed,
  2 ignored por permisos de comandos tecnicos `scene.modify`. El comando
  `dotnet build cli/UnityIA.sln` sigue bloqueado por SDK .NET 7.0.102 con
  `NETSDK1045`; no declarar v0.3 estable hasta ejecutar build/tests .NET con
  SDK 8.
- 2026-06-20: SDK .NET 8.0.422 instalado en `C:\Users\usuario\.dotnet`.
  `dotnet build cli/UnityIA.sln` paso con 0 warnings/0 errores y
  `dotnet test cli/UnityIA.sln --no-build` paso 12/12. Sandbox Unity
  `6000.3.17f1` en `C:\Temp\UnityIA-Sandbox-20260620-211442` ejecuto
  EditMode tests con 18 passed, 0 failed, 2 skipped. Con esos resultados,
  v0.3 queda estable para el catalogo publico inicial documentado.
- 2026-06-20: v0.4 avanzo en CLI live UX: `session list`, `status`,
  `commands` y `execute --file` aceptan seleccion por `--project` o
  `--session`; respuestas no `ActionResult` se convierten en
  `INVALID_RESPONSE` y fallos de conexion en `TRANSPORT_ERROR`. Tests .NET:
  18/18 passed.
- 2026-06-20: verificacion live end-to-end en sandbox Unity
  `C:\Temp\UnityIA-Sandbox-20260620-211442`: `unityia status --project`,
  `unityia commands --project` y
  `unityia execute --file schemas/examples/context.snapshot.json --project`
  devolvieron `success:true` contra el servidor HTTP loopback y dispatcher
  reales. Sigue pendiente decidir si se anaden wrappers CLI como
  `unityia context snapshot`; no son requisito para el bridge live inicial.
- 2026-06-20: v0.5 inicial implementado para batch/tests: CLI acepta
  `--mode batch execute --file ... --project ... --unity ...` y
  `tests run --mode EditMode --project ... --unity ...`; batch execute usa
  `UnityIABatchEntrypoint.ExecuteCommand`, y tests EditMode convierten XML de
  Unity Test Framework a `ActionResult` JSON. Verificacion en sandbox Unity
  `6000.3.17f1` en `C:\Temp\UnityIA-Sandbox-v05`: EditMode directo
  20 passed, 0 failed, 2 skipped; `unityia tests run --mode EditMode` devolvio
  `success:true` con 20/0/2; `unityia --mode batch execute --file
  schemas/examples/context.snapshot.json` devolvio `success:true`. PlayMode
  queda pendiente hasta que existan casos reales.
- 2026-06-21: v0.6 inicial implementado: `capabilities.list`,
  `validate.active_scene`, metadatos explicitos de comandos, policy con
  `authorizationMode: "confirm_actions"`, rechazo de `full_access`,
  confirmacion de mutaciones por `commandId` + hash canonico, UI de
  confirmaciones pendientes y wrappers CLI `capabilities list` y
  `validate active-scene`. Verificacion: `dotnet build cli/UnityIA.sln`
  0 warnings/0 errores, `dotnet test cli/UnityIA.sln --no-build` 31/31 passed,
  sandbox Unity `6000.3.17f1` en `C:\Temp\UnityIA-Sandbox-v05` ejecuto
  EditMode con 28 passed, 0 failed, 2 skipped tras actualizar la policy del
  sandbox al schema v0.6.
- 2026-06-21: wrapper CLI `unityia context snapshot` anadido en live y batch,
  reutilizando `context.snapshot` como comando publico sin semantica nueva en
  el CLI. Las formas CLI publicas `context`, `capabilities`, `validate` y
  `tests` quedan definidas; `tests run --mode PlayMode` sigue pendiente hasta
  que existan casos PlayMode reales.
- 2026-06-21: v0.7 arranca con infraestructura interna, no con proveedor real:
  `IIntentCommandProvider`, `IntentPlanningService`, guard de comandos
  publicos, marcado de mutaciones que requieren confirmacion y traza con hash
  del prompt. Tests .NET: 38/38 passed. Sigue pendiente traducir intenciones
  reales, ejecutar planes contra `capabilities.list`, enlazar resultados y
  validacion posterior en la trazabilidad, y evaluar calidad antes de uso
  cotidiano.
- 2026-06-21: el planner IA ahora exige una respuesta correcta de
  `capabilities.list` antes de aceptar comandos propuestos. Cada comando debe
  aparecer como `public`, `implemented`, permitido por policy efectiva y con
  confirmacion requerida para mutaciones. Tests .NET: 41/41 passed. Sigue
  pendiente traducir intenciones reales y enlazar ejecucion/resultados con la
  trazabilidad.
- 2026-06-21: anadido proveedor determinista de intencion estructurada para
  `read_context`, `validate_active_scene` y `create_gameobject`. La entrada es
  JSON controlado y no lenguaje natural; sirve para fijar el contrato seguro de
  traduccion antes de conectar un proveedor IA real. Tests .NET: 45/45 passed.
- 2026-06-21: anadido `IntentExecutionService` interno para ejecutar planes IA
  ya filtrados mediante una abstraccion cerrada. La ejecucion se detiene al
  primer fallo, registra resultados por comando, lanza `validate.active_scene`
  tras mutaciones con `scenePath` y registra resultados de validacion sin
  guardar prompts ni payloads completos. Tests .NET: 49/49 passed.
- 2026-06-21: anadido arnes interno de evaluacion para el proveedor
  estructurado v0.7, con casos positivos (`read_context`,
  `validate_active_scene`, `create_gameobject`) y negativos para generacion de
  C#, rutas fuera de `Assets/` y shell. Tests .NET: 52/52 passed. La casilla
  de evaluacion de seguridad/calidad sigue abierta hasta evaluar un proveedor
  IA real y definir umbrales de uso cotidiano.
- 2026-06-21: anadido gate de evaluacion v0.7 con tasa minima configurable y
  requisito de que todos los casos `security` pasen. La linea base estructurada
  pasa con politica estricta 100%; un fallo de seguridad bloquea el gate aunque
  la tasa global sea laxa. Tests .NET: 55/55 passed.
- 2026-06-21: anadido `IntentReadinessGate` para separar aprobacion de la
  baseline estructurada de readiness de uso cotidiano. Si la politica exige
  proveedor IA real, el proveedor determinista queda bloqueado con
  `REAL_PROVIDER_NOT_EVALUATED` aunque pase el gate de evaluacion. Tests .NET:
  57/57 passed.
- 2026-06-21: anadido `HttpIntentCommandProvider` como adapter externo
  cerrado. El endpoint recibe prompt + intents soportadas y solo puede devolver
  una intencion estructurada; no se aceptan `commandJson`, scripts ni comandos
  directos. HTTPS es obligatorio salvo loopback explicito para pruebas, y los
  bearer tokens no se ecoan en errores. Tests .NET: 63/63 passed. Sigue
  pendiente evaluar un proveedor IA real contra el readiness gate.
- 2026-06-21: anadido `unityia intent evaluate` como comando local de
  evaluacion v0.7. Ejecuta la baseline estructurada contra proveedor
  `structured` o `http`, consume un snapshot de `capabilities.list`, devuelve
  `ActionResult` con report/readiness/trazas hash y no ejecuta authoring ni
  contacta con Unity. Tests .NET: 66/66 passed. Sigue pendiente evaluar un
  proveedor IA real antes de marcar lista la evaluacion de seguridad/calidad.
- 2026-06-21: anadido `unityia intent plan` como comando local de
  planificacion v0.7. Traduce una intencion estructurada o prompt delegado a
  proveedor HTTP en `CommandEnvelope` publicos filtrados por catalogo,
  `capabilities.list`, permisos y confirmacion; no ejecuta Unity ni aprueba
  mutaciones. Con esto queda cubierta la traduccion de intencion a comandos
  publicos en el alcance v0.7 inicial. Tests .NET: 69/69 passed. Sigue
  pendiente evaluar un proveedor IA real antes de cerrar seguridad/calidad.
- 2026-06-21: el arnes de evaluacion distingue baseline estructurada y
  baseline de prompts de usuario para `--provider http`. La evaluacion HTTP ya
  envia prompts naturales y exige los mismos resultados positivos/negativos
  tras pasar por el adapter cerrado, el guard publico y `capabilities.list`.
  Tests .NET: 70/70 passed. La casilla de seguridad/calidad sigue abierta
  hasta ejecutar y registrar un proveedor IA real concreto.
- 2026-06-22: anadida repeticion configurable de casos con
  `unityia intent evaluate --repeat-count N`. Cada intento cuenta en el reporte
  y en el gate, permitiendo medir estabilidad de proveedores no deterministas
  sin guardar prompts en claro. Tests .NET: 72/72 passed. Sigue pendiente la
  ejecucion contra un proveedor IA real concreto para cerrar seguridad/calidad.
- 2026-06-22: el reporte de `unityia intent evaluate` incluye metadatos
  auditables de proveedor: etiqueta y version opcionales, mas scheme/host/port
  y hash SHA-256 de endpoint para HTTP. No se imprime URL completa, query
  string, bearer token ni respuesta cruda. Tests .NET: 73/73 passed. Sigue
  pendiente ejecutar y registrar un proveedor IA real concreto.
- 2026-06-22: el `IntentReadinessGate` ahora exige estabilidad minima para
  proveedor real. Con `--provider http`, el CLI requiere por defecto al menos
  3 repeticiones; si una evaluacion real pasa menos intentos devuelve
  `REAL_PROVIDER_STABILITY_NOT_EVALUATED` aunque el pass rate disponible sea
  100%. Tests .NET: 75/75 passed. Sigue pendiente ejecutar y registrar un
  proveedor IA real concreto.
- 2026-06-22: anadido schema de contrato para la salida de
  `unityia intent evaluate` en `schemas/v0.1/intent.evaluate.result.schema.json`.
  La prueba del CLI valida una salida real contra ese schema, cubriendo
  provider, policy, readiness, report, traces y warnings. Tests .NET: 76/76
  passed. Sigue pendiente ejecutar y registrar un proveedor IA real concreto.
- 2026-06-22: anadido `unityia intent verify-report --file` para validar
  reportes guardados de `intent evaluate` contra el schema v0.1 y resumir
  evidencia auditable mediante hash, proveedor, politica, readiness y totales
  del reporte. Tests .NET: 79/79 passed. Sigue pendiente ejecutar y registrar
  un proveedor IA real concreto antes de cerrar seguridad/calidad.
- 2026-06-22: `unityia intent verify-report` ahora puede exigir evidencia de
  proveedor real con `--require-real-provider true` y
  `--minimum-real-provider-repeat-count`. La verificacion rechaza reportes
  listos de la baseline estructurada como evidencia de uso cotidiano y bloquea
  reportes con repeticiones insuficientes. Tests .NET: 81/81 passed. Sigue
  pendiente ejecutar y registrar un proveedor IA real concreto antes de cerrar
  seguridad/calidad.
- 2026-06-22: endurecida la verificacion estricta de reportes IA reales:
  `intent verify-report --require-real-provider true` exige ruta de proveedor
  HTTP, baseline `v0.7-user-prompt-baseline`, marca de proveedor real,
  metadatos auditables de endpoint y repeticion minima. Tests .NET: 84/84
  passed. Sigue pendiente ejecutar y registrar un proveedor IA real concreto
  antes de cerrar seguridad/calidad.
- 2026-06-22: `intent verify-report` ahora valida coherencia semantica offline
  del reporte ademas del schema: `ActionResult` frente a readiness, metadatos
  provider/readiness, `repeatCount`, case set declarado, agregados de casos,
  `passRate` y gate. La evidencia estricta de proveedor real tambien exige que
  la evaluacion original usara `policy.requireRealProvider: true`. Tests .NET:
  86/86 passed. Sigue pendiente ejecutar y registrar un proveedor IA real
  concreto antes de cerrar seguridad/calidad.
- 2026-06-22: ampliada la validacion semantica offline de `intent verify-report`
  para recomputar cada caso contra la baseline declarada: categoria,
  `expectedSuccess`, `expectedCode`, comandos esperados, comandos reales,
  `passed` y numero de trazas. Esto bloquea reportes editados que conserven
  agregados validos pero no evidencien los resultados reales de la baseline.
  Tests .NET: 88/88 passed. Sigue pendiente ejecutar y registrar un proveedor
  IA real concreto antes de cerrar seguridad/calidad.
- 2026-06-22: la evidencia estricta de proveedor real en
  `intent verify-report --require-real-provider true` ahora rechaza endpoints
  no HTTPS y hosts loopback/locales. `--allow-insecure-loopback` sigue siendo
  valido para pruebas del adapter HTTP, pero esos reportes no cuentan como
  evidencia de uso cotidiano. Tests .NET: 90/90 passed. Sigue pendiente
  ejecutar y registrar un proveedor IA real concreto antes de cerrar
  seguridad/calidad.
- 2026-06-22: la verificacion estricta de evidencia real ahora exige
  `provider.label` y `provider.version` no vacios, de modo que un reporte listo
  quede asociado a un proveedor/modelo/despliegue concreto y no solo a un
  endpoint anonimo. Tests .NET: 92/92 passed. Sigue pendiente ejecutar y
  registrar un proveedor IA real concreto antes de cerrar seguridad/calidad.
- 2026-06-22: `unityia intent evaluate --provider http` queda alineado con
  `intent verify-report`: cuando `--require-real-provider true` esta activo,
  el readiness falla si la evidencia no cumple el modo estricto
  (label/version, HTTPS no loopback, baseline de prompts y repeticion minima).
  Los endpoints loopback siguen disponibles para fixtures solo con
  `--require-real-provider false`. Tests .NET: 94/94 passed. Sigue pendiente
  ejecutar y registrar un proveedor IA real concreto antes de cerrar
  seguridad/calidad.
- 2026-06-22: la validacion semantica offline de `intent verify-report`
  ahora contrasta cada traza con su resultado de caso correspondiente:
  `trace.code`, comandos planificados y comandos rechazados deben cuadrar con
  `actualCode`, `actualCommands` y exito/fallo del caso. Tests .NET: 96/96
  passed. Sigue pendiente ejecutar y registrar un proveedor IA real concreto
  antes de cerrar seguridad/calidad.
- 2026-06-22: `intent evaluate --provider http --require-real-provider true`
  valida en preflight las evidencias que pueden comprobarse sin enviar prompts:
  `provider.label`, `provider.version` y endpoint HTTPS no loopback. Si fallan,
  devuelve un reporte auditable no ejecutado y no contacta al proveedor; esos
  reportes no cuentan como readiness. Tests .NET: 98/98 passed. Sigue
  pendiente ejecutar y registrar un proveedor IA real concreto antes de cerrar
  seguridad/calidad.
- 2026-06-22: `intent evaluate` acepta `--output-file` para registrar en un
  `.json` nuevo el mismo `ActionResult` que imprime en stdout, tanto en
  readiness positivo como fallido. La ruta rechaza sobrescritura y carpetas
  Unity reservadas (`Assets`, `Library`, `ProjectSettings`, `Packages`) para no
  convertir evidencia en authoring accidental. Tests .NET: 100/100 passed.
  Sigue pendiente ejecutar y registrar un proveedor IA real concreto antes de
  cerrar seguridad/calidad.
- 2026-06-22: el contrato de `intent evaluate` ahora incluye
  `data.capabilities.sha256`, un hash SHA-256 del snapshot de
  `capabilities.list` usado por el planner. `intent verify-report` conserva ese
  hash en el resumen y el schema rechaza reportes sin esa evidencia, de modo
  que una evaluacion real queda ligada al catalogo/permisos evaluados sin copiar
  el snapshot completo. Tests .NET: 101/101 passed. Sigue pendiente ejecutar y
  registrar un proveedor IA real concreto antes de cerrar seguridad/calidad.
- 2026-06-22: `intent verify-report` acepta expectativas opcionales
  (`--expect-provider-label`, `--expect-provider-version`,
  `--expect-endpoint-sha256`, `--expect-capabilities-sha256`) para fallar con
  `REPORT_EXPECTATION_MISMATCH` si el reporte guardado no corresponde al
  proveedor, despliegue, endpoint o snapshot de capabilities que se pretendia
  evaluar. Tests .NET: 102/102 passed. Sigue pendiente ejecutar y registrar un
  proveedor IA real concreto antes de cerrar seguridad/calidad.
- 2026-06-22: el contrato de `intent evaluate` ahora incluye
  `data.evaluation` con `id`, `generatedAtUtc` y `resultSchema`, y
  `intent verify-report` conserva esos metadatos en su resumen. Esto permite
  auditar que artefacto se verifico y cuando fue generado sin guardar prompts
  ni respuestas crudas. Tests .NET: 103/103 passed. Sigue pendiente ejecutar y
  registrar un proveedor IA real concreto antes de cerrar seguridad/calidad.
- 2026-06-22: `intent verify-report` acepta
  `--maximum-report-age-hours` para rechazar evidencia obsoleta con
  `REPORT_STALE` y timestamps futuros con `REPORT_TIMESTAMP_INVALID`. Esto
  evita reutilizar indefinidamente un reporte antiguo como readiness de uso
  cotidiano. Tests .NET: 104/104 passed. Sigue pendiente ejecutar y registrar
  un proveedor IA real concreto antes de cerrar seguridad/calidad.
- 2026-06-22: `intent verify-report` puede derivar expectativas de evidencia
  desde `--expect-endpoint URL` y `--expect-capabilities-file FILE`, como
  alternativa a pasar hashes SHA-256 manuales. El CLI rechaza combinaciones
  ambiguas con `--expect-endpoint-sha256` o
  `--expect-capabilities-sha256`. Tests .NET: 106/106 passed. Sigue pendiente
  ejecutar y registrar un proveedor IA real concreto antes de cerrar
  seguridad/calidad.
- 2026-06-22: `intent verify-report --minimum-pass-rate` ahora puede exigir que
  el reporte guardado haya sido generado con una politica de calidad al menos
  igual de estricta que el umbral de verificacion. Si
  `data.policy.minimumPassRate` es inferior, falla con
  `REPORT_POLICY_TOO_LAX`. Tests .NET: 107/107 passed. Sigue pendiente
  ejecutar y registrar un proveedor IA real concreto antes de cerrar
  seguridad/calidad.
- 2026-06-22: la validacion semantica offline de `intent verify-report` ahora
  ata cada traza de evaluacion a su caso e intento esperados mediante
  `trace.requestId`, exige etapa `planning` y hash de prompt presente. Esto
  evita aceptar reportes donde las trazas hayan sido mezcladas o sustituidas
  manteniendo agregados validos. Tests .NET: 108/108 passed. Sigue pendiente
  ejecutar y registrar un proveedor IA real concreto antes de cerrar
  seguridad/calidad.
