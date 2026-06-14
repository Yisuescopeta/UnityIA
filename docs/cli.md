# `unityia` CLI

## Función

`unityia` será la entrada oficial para agentes, automatización externa y CI.
Actúa como cliente del protocolo UnityIA; no sustituye al Unity Editor ni
implementa authoring por sí mismo.

El CLI no debe:

- editar escenas, prefabs, assets, YAML o `.meta`;
- escribir en `Library`, `ProjectSettings` o `Packages`;
- ejecutar shell arbitrario;
- cargar código generado;
- llamar directamente a internals del paquete Unity;
- cambiar de live a batch sin una opción explícita.

## Estado contractual

Los ejemplos de esta página describen la interfaz objetivo. No garantizan que el
ejecutable o cada opción estén disponibles en el estado actual del repositorio.
Una versión solo puede declararlos implementados cuando tenga pruebas de
contrato e integración.

## Forma prevista

```text
unityia context snapshot
unityia capabilities list
unityia execute --file command.json
unityia validate active-scene
```

El CLI debe ofrecer también selección explícita del modo:

```text
unityia --mode live ...
unityia --mode batch ...
```

No se añadirá `--mode full_access` hasta la fase que lo autorice. La política
`confirm_actions` pertenece a permisos y puede requerir una interfaz de
confirmación separada.

## Live editor mode

En live mode, el CLI:

1. descubre o recibe una sesión concreta del Editor;
2. autentica la petición;
3. valida localmente el contrato JSON cuando exista schema;
4. envía un comando;
5. imprime el `ActionResult` recibido.

Si hay varias sesiones, debe exigir una selección inequívoca por proyecto o
identificador. No debe elegir silenciosamente.

## Batch mode

En batch mode, el CLI podrá iniciar una versión configurada de Unity con una
entrada controlada y argumentos cerrados. No construirá comandos de shell
arbitrarios ni aceptará nombres de métodos proporcionados por el agente.

Batch está planificado para v0.5.

## Entrada y salida

La salida normal debe ser JSON válido y contener:

```json
{
  "success": true,
  "message": "Human-readable summary.",
  "code": "OK",
  "data": {}
}
```

- `stdout`: `ActionResult` serializable.
- `stderr`: diagnóstico del propio CLI.
- código de salida `0`: `success: true`.
- código distinto de `0`: error local o `success: false`.

Los mensajes son informativos; clientes automatizados deben usar `code` y
`data`.

## Reglas para implementar comandos CLI

1. Un subcomando debe mapear a un comando público documentado.
2. El CLI no debe añadir semántica que no exista en el protocolo.
3. Las validaciones locales no sustituyen las validaciones de Unity.
4. Tokens, secretos y payloads sensibles no se imprimen ni registran.
5. Toda nueva opción debe documentar modo, permisos y compatibilidad.

