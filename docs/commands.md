# Catalogo inicial de comandos

El orden de implementacion se decide en [project-plan.md](project-plan.md).
Los permisos asociados se documentan en [permissions.md](permissions.md), y el
roadmap de fases en [roadmap.md](roadmap.md).

## Estado del catalogo

Esta pagina separa comandos publicos implementados, comandos reservados y
comandos tecnicos de prototipo. Un comando tecnico puede existir en el repo sin
formar parte del catalogo publico estable.

Un comando pasa a estado implementado unicamente cuando:

1. existe su DTO y validador;
2. esta registrado publicamente;
3. aplica permisos y auditoria;
4. devuelve `ActionResult`;
5. tiene pruebas de contrato y comportamiento;
6. la documentacion indica la version que lo entrega.

El paquete actual conserva comandos tecnicos como `system.status`,
`system.commands.list`, `context.get`, `scene.object.*` y `scene.save`.
Sirven para validar piezas del dispatcher y del package base. No sustituyen al
catalogo publico `authoring.*`.

Comandos publicos iniciales de v0.3:

- `context.snapshot`
- `authoring.create_gameobject`
- `authoring.add_component`
- `authoring.set_component_field`
- `authoring.save_scene`

Comandos publicos iniciales de v0.6:

- `capabilities.list`
- `validate.active_scene`

## Envelope comun objetivo

```json
{
  "protocolVersion": "0.1",
  "commandId": "uuid",
  "command": "context.snapshot",
  "issuedAtUtc": "2026-06-14T12:00:00Z",
  "preconditions": {},
  "arguments": {},
  "options": {
    "dryRun": false
  }
}
```

No se permiten nombres de metodos, tipos arbitrarios, codigo C# ni comandos de
shell dentro de `arguments`.

## `context.snapshot`

**Tipo:** lectura
**Capacidad:** `context.read`
**Version:** v0.3 inicial

Devuelve una instantanea serializable del contexto de authoring:

- sesion y version del contexto;
- modo del Editor;
- escena activa;
- escenas abiertas;
- seleccion actual cuando sea seguro exponerla;
- referencias de objetos mediante ruta y un identificador estable cuando este
  disponible.

No modifica el proyecto.

## `capabilities.list`

**Tipo:** lectura
**Capacidad:** `capabilities.read`
**Version:** v0.6 inicial

Enumera las capacidades y comandos registrados en la sesion, incluyendo:

- version del protocolo;
- disponibilidad por modo live o batch;
- si una operacion es mutadora;
- permiso requerido;
- estado implementado o no disponible;
- superficie `public` o `technical`;
- acceso de ruta `none`, `read` o `write`;
- si requiere `confirm_actions`;
- decision de permiso segun la policy efectiva;
- restricciones relevantes.

No debe revelar tokens, rutas sensibles ni handlers internos.

## `authoring.create_gameobject`

**Tipo:** mutacion
**Capacidad:** `scene.gameobject.create`
**Version:** v0.3 inicial

Crea un `GameObject` vacio en una escena autorizada. Sus argumentos minimos
son `scenePath`, `name`, `parent` opcional y transform inicial opcional
mediante `position`, `rotationEuler` y `scale`.

La operacion debe:

- exigir Edit Mode y contexto vigente;
- usar Undo;
- rechazar escenas sin persistir cuando la identidad lo requiera;
- dejar la escena sucia;
- no guardar automaticamente;
- devolver una referencia serializable al objeto creado.

## `authoring.add_component`

**Tipo:** mutacion
**Capacidad:** `scene.component.add`
**Version:** v0.3 inicial

Anade un componente de un catalogo permitido a un `GameObject` existente.

No acepta nombres de tipos arbitrarios. Cada componente habilitado debe tener
un adaptador o registro explicito, validacion propia y politica de
compatibilidad.

Catalogo inicial v0.3:

- `BoxCollider`

## `authoring.set_component_field`

**Tipo:** mutacion
**Capacidad:** `scene.component.write`
**Version:** v0.3 inicial

Modifica un campo autorizado de un componente registrado. El contrato debe
identificar:

- objeto objetivo;
- componente objetivo sin reflection abierta;
- campo permitido;
- valor serializable con tipo validado;
- contexto esperado.

No proporciona acceso generico al `SerializedObject` ni a cualquier propiedad
descubierta dinamicamente.

Campos registrados iniciales:

- `Transform.localPosition`
- `Transform.localEulerAngles`
- `Transform.localScale`
- `BoxCollider.center`
- `BoxCollider.size`
- `BoxCollider.isTrigger`

## `authoring.save_scene`

**Tipo:** mutacion
**Capacidad:** `scene.save`
**Version:** v0.3 inicial

Guarda explicitamente la escena activa esperada. Sus argumentos minimos son
`scenePath`, que debe apuntar a la escena activa dentro de `Assets/` y terminar
en `.unity`.

No hay autosave implicito. Las mutaciones de authoring dejan la escena sucia y
este comando es la ruta publica para persistirla.

## `validate.active_scene`

**Tipo:** verificacion
**Capacidad:** `validation.scene.run`
**Version:** v0.6 inicial

Ejecuta validadores registrados sobre la escena activa y devuelve resultados
estructurados sin corregir automaticamente los problemas.

Los resultados deben diferenciar errores, warnings e informacion, e identificar
el validador y el objeto afectado cuando sea posible.

Argumentos minimos:

- `scenePath`: ruta normalizada `Assets/**/*.unity`.

El comando devuelve `VALIDATION_FAILED` cuando hay resultados con severidad
`error`; devuelve `OK` cuando solo hay `warning` o `info`.

## Convenciones

- Lecturas no mutan ni guardan.
- Mutaciones requieren precondiciones y permiso.
- Mutaciones bajo `confirm_actions` requieren una aprobacion exacta para el
  `commandId` y payload canonico antes de llegar al handler.
- `dryRun` valida sin ejecutar cuando el handler lo soporte.
- Los comandos no se agrupan implicitamente en transacciones.
- Reutilizar un `commandId` con el mismo payload devuelve el resultado terminal
  conocido.
- Reutilizar un `commandId` con otro payload debe fallar cerrado.
- Un comando desconocido debe fallar cerrado.
- Cambiar un contrato incompatible requiere una nueva version de protocolo.
