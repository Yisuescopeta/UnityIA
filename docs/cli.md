# `unityia` CLI

## Funcion

`unityia` sera la entrada oficial para agentes, automatizacion externa y CI.
Actua como cliente del protocolo UnityIA; no sustituye al Unity Editor ni
implementa authoring por si mismo.

El CLI no debe:

- editar escenas, prefabs, assets, YAML o `.meta`;
- escribir en `Library`, `ProjectSettings` o `Packages`;
- ejecutar shell arbitrario;
- cargar codigo generado;
- llamar directamente a internals del paquete Unity;
- cambiar de live a batch sin una opcion explicita.

## Estado contractual

Los ejemplos de esta pagina describen la interfaz objetivo. No garantizan que el
ejecutable o cada opcion esten disponibles en el estado actual del repositorio.
Una version solo puede declararlos implementados cuando tenga pruebas de
contrato e integracion.

El orden activo se decide en [project-plan.md](project-plan.md). El CLI live
corresponde a v0.4; batch/tests corresponden a v0.5.

Estado implementado para el corte v0.4 inicial:

```text
unityia session list
unityia status [--project PATH | --session ID]
unityia commands [--project PATH | --session ID]
unityia context snapshot [--project PATH | --session ID]
unityia execute --file command.json [--project PATH | --session ID]
```

Estado implementado para el corte v0.5 inicial:

```text
unityia --mode batch context snapshot --project PATH --unity UNITY_EXE
unityia --mode batch execute --file command.json --project PATH --unity UNITY_EXE
unityia tests run --mode EditMode --project PATH --unity UNITY_EXE
```

Estado implementado para el corte v0.6 inicial:

```text
unityia capabilities list [--project PATH | --session ID]
unityia validate active-scene --scene Assets/Scenes/Main.unity [--project PATH | --session ID]
unityia --mode batch capabilities list --project PATH --unity UNITY_EXE
unityia --mode batch validate active-scene --scene Assets/Scenes/Main.unity --project PATH --unity UNITY_EXE
```

Estado implementado para el corte v0.7 inicial:

```text
unityia intent plan --provider structured --prompt-file intent.json --capabilities-file capabilities.json
unityia intent plan --provider http --endpoint https://provider.example/intent --prompt-file prompt.txt --capabilities-file capabilities.json
unityia intent evaluate --provider structured --capabilities-file capabilities.json
unityia intent evaluate --provider http --endpoint https://provider.example/intent --capabilities-file capabilities.json
unityia intent evaluate --provider http --endpoint https://provider.example/intent --capabilities-file capabilities.json --output-file intent-evaluation-report.json
unityia intent verify-report --file intent-evaluation-report.json
unityia intent verify-report --file intent-evaluation-report.json --require-real-provider true
```

Opciones de intencion disponibles:

- `--require-real-provider true|false`, para `intent evaluate` y
  `intent verify-report`;
- `--minimum-pass-rate 0..1`; en `intent evaluate` configura el gate de
  evaluacion, y en `intent verify-report` exige que la politica del reporte
  guardado haya usado al menos ese umbral;
- `--repeat-count N`, de 1 a 20, solo para `intent evaluate`;
- `--minimum-real-provider-repeat-count N`, de 1 a 20; en `intent evaluate`
  por defecto es 3 para `--provider http` y 1 para `structured`; en
  `intent verify-report` por defecto es 3 cuando `--require-real-provider true`;
- `--provider-label NAME`, etiqueta auditable opcional para el proveedor;
- `--provider-version VALUE`, version auditable opcional para el proveedor;
- `--token-env ENV`, solo para `--provider http`;
- `--allow-insecure-loopback true|false`, solo para pruebas HTTP loopback;
- `--output-file PATH`, solo para `intent evaluate`; escribe una copia JSON
  nueva del `ActionResult` de evaluacion;
- `--maximum-report-age-hours N`, solo para `intent verify-report`; falla si el
  reporte fue generado hace mas horas de las permitidas;
- `--expect-provider-label NAME`, `--expect-provider-version VALUE`,
  `--expect-endpoint URL` o `--expect-endpoint-sha256 HEX`, y
  `--expect-capabilities-file FILE` o `--expect-capabilities-sha256 HEX`, solo
  para `intent verify-report`; fallan si el reporte no coincide con esos
  valores esperados;
- `--timeout-seconds N`, solo para `--provider http`.

`--unity` puede omitirse si `UNITYIA_UNITY_EDITOR` apunta a un ejecutable de
Unity valido. `PlayMode` sigue reservado hasta que existan pruebas PlayMode
reales.

Si hay varias sesiones live, el CLI exige `--project` o `--session`; no elige
una sesion implicitamente. `session list` muestra identificadores y rutas de
proyecto para permitir esa seleccion, pero nunca imprime bearer tokens.

## Forma prevista

```text
unityia context snapshot
unityia capabilities list
unityia execute --file command.json
unityia validate active-scene
```

El CLI debe ofrecer tambien seleccion explicita del modo:

```text
unityia --mode live ...
unityia --mode batch ...
```

No se anadira `--mode full_access` hasta la fase que lo autorice. La politica
`confirm_actions` pertenece a permisos y usa la interfaz del Editor para
aprobar o denegar mutaciones pendientes. El CLI no aprueba mutaciones por si
mismo.

## Live editor mode

En live mode, el CLI:

1. descubre o recibe una sesion concreta del Editor;
2. autentica la peticion;
3. valida localmente el contrato JSON cuando exista schema;
4. envia un comando;
5. imprime el `ActionResult` recibido.

Si hay varias sesiones, debe exigir una seleccion inequivoca por proyecto o
identificador. No debe elegir silenciosamente.

## Batch mode

En batch mode, el CLI podra iniciar una version configurada de Unity con una
entrada controlada y argumentos cerrados. No construira comandos de shell
arbitrarios ni aceptara nombres de metodos proporcionados por el agente.

Batch esta disponible en v0.5 para dos rutas controladas:

- `execute --file`, que invoca `UnityIABatchEntrypoint.ExecuteCommand` mediante
  `-batchmode -executeMethod` y escribe un `ActionResult`;
- `tests run --mode EditMode`, que ejecuta Unity Test Framework en batch y
  convierte el XML de resultados a JSON.

El CLI rechaza batch si falta `--project`, si el proyecto no parece una raiz
Unity, si existe `Temp/UnityLockfile` o si no hay ejecutable Unity inequivoco.
La ruta `--unity` o `UNITYIA_UNITY_EDITOR` se pasa como ejecutable directo con
argumentos estructurados, no mediante shell.

## Planificacion de intencion IA

`unityia intent plan` traduce una intencion a `CommandEnvelope` publicos ya
documentados. No contacta con Unity, no ejecuta authoring, no aprueba
confirmaciones y no guarda escenas.

El comando exige:

- `--prompt-file`, con una intencion estructurada para `--provider structured`
  o un prompt de usuario para `--provider http`;
- `--capabilities-file`, con un `ActionResult` valido de `capabilities.list`;
- `--provider structured|http`.

La planificacion solo se acepta si cada envelope propuesto supera el guard de
catalogo publico y aparece en `capabilities.list` como publico, implementado,
permitido por policy y con metadatos de confirmacion coherentes. Las mutaciones
planificadas se devuelven con `requiresConfirmation: true`; el CLI no las
confirma ni las ejecuta.

La salida sigue siendo un `ActionResult`. `data.plannedCommands` contiene los
envelopes que un cliente podria enviar despues por las rutas normales del
protocolo, y `data.traces` contiene hashes de prompt y comandos
planificados/rechazados, no el prompt en claro.

## Evaluacion de intencion IA

`unityia intent evaluate` ejecuta el arnes de evaluacion v0.7 contra un
proveedor de intencion. No contacta con Unity, no ejecuta authoring, no aprueba
confirmaciones y no guarda escenas.

El comando exige un archivo `--capabilities-file` que contenga un `ActionResult`
valido de `capabilities.list`. Ese snapshot se usa para verificar que cualquier
comando propuesto por el proveedor pertenece al catalogo publico, esta
implementado, esta permitido por policy y respeta `confirm_actions`.

`--provider structured` usa el proveedor determinista interno. Por defecto no
exige proveedor real, por lo que sirve para comprobar la baseline segura local.
Si se anade `--require-real-provider true`, el readiness falla con
`REAL_PROVIDER_NOT_EVALUATED` aunque la baseline pase.

`--provider http` llama a un endpoint externo cerrado. El endpoint recibe prompt
e intenciones soportadas, y debe devolver una intencion estructurada; no puede
devolver `commandJson` ni comandos directos. HTTPS es obligatorio salvo
`--allow-insecure-loopback true` contra loopback. Los bearer tokens se leen desde
`--token-env` y no se imprimen. Esta ruta usa una baseline de prompts de usuario
en lenguaje natural, no la entrada JSON estructurada interna, para medir la
traduccion real del proveedor.

Cuando `intent evaluate` se ejecuta con `--require-real-provider true`, el
readiness usa el mismo criterio estricto que `verify-report`: proveedor HTTP
real, baseline de prompts de usuario, `provider.label` y `provider.version`,
endpoint HTTPS no loopback y repeticion minima. Los endpoints loopback pueden
ejecutar la baseline con `--require-real-provider false` para probar el adapter,
pero no producen readiness de uso cotidiano.

El modo estricto valida esas precondiciones antes de contactar al endpoint
cuando es posible. Si faltan `provider.label` o `provider.version`, o el
endpoint no puede producir evidencia real por no ser HTTPS o por ser loopback,
`intent evaluate` devuelve `REAL_PROVIDER_NOT_EVALUATED` con un reporte
auditable no ejecutado (`report.total: 0`) y no envia prompts al proveedor.

La salida sigue siendo un `ActionResult`. `data.report` contiene resultados de
casos de evaluacion, `data.readiness` contiene el gate aplicado y `data.traces`
incluye hashes de prompt y comandos planificados/rechazados, no prompts en
claro. `data.provider.caseSet` indica si se uso la baseline estructurada o la
baseline de prompts de usuario. `data.capabilities.sha256` identifica el
snapshot de `capabilities.list` usado para filtrar comandos y permisos sin
copiar ese snapshot completo en el reporte. `data.evaluation` identifica el
artefacto con un `id`, `generatedAtUtc` y el schema de resultado usado.

El contrato de esa salida esta fijado en
`schemas/v0.1/intent.evaluate.result.schema.json`. El schema cubre proveedor,
metadatos de evaluacion, politica, readiness, reporte de casos, trazas y
warnings. No es un comando de Unity; es el contrato del `ActionResult` local
del CLI para conservar evidencia auditable de evaluacion.

`--output-file` permite registrar esa evidencia sin redireccionar stdout. El
archivo debe ser `.json`, su directorio padre debe existir y no se sobrescribe
si ya existe. Para evitar authoring accidental, el CLI rechaza rutas dentro de
`Assets`, `Library`, `ProjectSettings` o `Packages`. Se escribe el mismo
`ActionResult` que se imprime por stdout, incluso cuando el readiness falla.

`unityia intent verify-report --file` valida un reporte guardado de
`intent evaluate` contra ese contrato y devuelve un resumen auditable con
`reportSha256`, proveedor, metadatos de evaluacion, politica, hash de
capabilities, readiness y totales del reporte. Si el reporte valida y
`readiness.ready` es `true`, el comando termina con exit code 0. Si el reporte
valida pero no esta listo, termina con exit code 1 y conserva el codigo de
readiness original. Si no cumple el schema, devuelve
`VALIDATION_FAILED`. Despues del schema aplica validaciones semanticas sobre el
reporte: coherencia entre `ActionResult` y readiness, metadatos de proveedor,
`repeatCount`, case set declarado, expectativas por caso, comandos reales,
trazas frente a resultados por caso, `requestId` por caso/intento, etapa de
planificacion, hash de prompt, agregados de casos, `passRate` y datos del
gate.

`--maximum-report-age-hours` permite rechazar evidencia antigua. Si
`data.evaluation.generatedAtUtc` queda fuera de esa ventana, la verificacion
falla con `REPORT_STALE`; si el timestamp esta en el futuro, falla con
`REPORT_TIMESTAMP_INVALID`.

`--minimum-pass-rate` permite rechazar evidencia generada con una politica de
calidad mas laxa que la exigida durante la verificacion offline. Si
`data.policy.minimumPassRate` esta por debajo del umbral pedido, falla con
`REPORT_POLICY_TOO_LAX` aunque el reporte sea valido.

`intent verify-report` tambien acepta expectativas opcionales para ligar la
verificacion a una evaluacion concreta: `--expect-provider-label`,
`--expect-provider-version`, `--expect-endpoint URL` o
`--expect-endpoint-sha256 HEX`, y `--expect-capabilities-file FILE` o
`--expect-capabilities-sha256 HEX`. Si el reporte no coincide, falla con
`REPORT_EXPECTATION_MISMATCH`. Los hashes esperados manuales deben ser SHA-256
hex en minusculas; las formas con URL y archivo calculan el hash esperado antes
de validar el reporte.

Cuando se usa `--require-real-provider true`, `intent evaluate` y
`verify-report` aplican una verificacion adicional para evidencia de uso
cotidiano: exigen
`provider.name: "http"`, `provider.kind: "http"`,
`provider.realProviderEvaluated: true`, `policy.requireRealProvider: true`,
`provider.caseSet: "v0.7-user-prompt-baseline"`, `provider.label` y
`provider.version` no vacios, metadatos auditables de endpoint con
`scheme: "https"` y host no loopback, y que
`data.report.repeatCount` alcance `--minimum-real-provider-repeat-count`. Si
falla, devuelven `REAL_PROVIDER_NOT_EVALUATED` o
`REAL_PROVIDER_STABILITY_NOT_EVALUATED` sin registrar prompts ni respuestas
crudas.

Para evaluaciones HTTP, `data.provider.endpoint` incluye solo `scheme`, `host`,
`port` y `sha256` de la URL completa. No se imprime la URL completa, query
string, bearer token ni respuesta cruda del proveedor. `--provider-label` y
`--provider-version` identifican el proveedor, modelo, despliegue o build
evaluado sin depender de secretos o rutas completas. Los endpoints loopback
permitidos con
`--allow-insecure-loopback true` son utiles para pruebas del adapter, pero no
cuentan como evidencia estricta de proveedor real.

`--repeat-count` repite cada caso de evaluacion para medir estabilidad. Un
proveedor HTTP real deberia pasar todos los intentos, incluidos los casos
`security`, antes de considerarse candidato a uso cotidiano. El limite inicial
es 20 repeticiones para evitar llamadas accidentales excesivas al endpoint.

Cuando `--require-real-provider true` esta activo, el readiness tambien exige
que `repeatCount` alcance `--minimum-real-provider-repeat-count`. Si no se
cumple, la salida falla con `REAL_PROVIDER_STABILITY_NOT_EVALUATED` aunque la
tasa de paso de la ejecucion disponible sea 100%.

## Entrada y salida

La salida normal debe ser JSON valido y contener:

```json
{
  "success": true,
  "message": "Human-readable summary.",
  "code": "OK",
  "data": {}
}
```

- `stdout`: `ActionResult` serializable.
- `stderr`: diagnostico del propio CLI.
- codigo de salida `0`: `success: true`.
- codigo distinto de `0`: error local o `success: false`.
- una respuesta de Unity que no sea `ActionResult` JSON valido se convierte en
  `INVALID_RESPONSE` en stdout.
- un fallo de conexion con la sesion seleccionada se convierte en
  `TRANSPORT_ERROR` en stdout.

Los mensajes son informativos; clientes automatizados deben usar `code` y
`data`.

## Reglas para implementar comandos CLI

1. Un subcomando debe mapear a un comando publico documentado.
2. El CLI no debe anadir semantica que no exista en el protocolo.
3. Las validaciones locales no sustituyen las validaciones de Unity.
4. Tokens, secretos y payloads sensibles no se imprimen ni registran.
5. Toda nueva opcion debe documentar modo, permisos y compatibilidad.

## Verificacion local

El CLI usa .NET 8. Desde la raiz del repositorio:

```powershell
dotnet build cli/UnityIA.sln
dotnet test cli/UnityIA.sln
```

Las pruebas live requieren un Unity Editor abierto con el package instalado en
un sandbox. El flujo de sandbox se documenta en
[development/sandbox.md](development/sandbox.md).
