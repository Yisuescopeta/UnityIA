# Catalogo inicial de comandos

## Estado del catalogo

Esta pagina define nombres reservados y contratos objetivo. Los comandos
enumerados aqui todavia no se consideran implementados, aunque el repositorio
pueda contener prototipos con nombres o comportamientos parecidos.

Un comando pasa a estado implementado unicamente cuando:

1. existe su DTO y validador;
2. esta registrado publicamente;
3. aplica permisos y auditoria;
4. devuelve `ActionResult`;
5. tiene pruebas de contrato y comportamiento;
6. la documentacion indica la version que lo entrega.

El paquete actual puede incluir comandos tecnicos de prototipo como
`system.status`, `system.commands.list` o `scene.object.*`. Esos comandos
sirven para validar piezas del dispatcher y del package base; no equivalen al
catalogo publico reservado de authoring.

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
**Capacidad prevista:** `context.read`
**Version objetivo:** v0.3

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
**Capacidad prevista:** `capabilities.read`
**Version objetivo:** v0.6

Enumera las capacidades y comandos registrados en la sesion, incluyendo:

- version del protocolo;
- disponibilidad por modo live o batch;
- si una operacion es mutadora;
- permiso requerido;
- estado implementado o no disponible;
- restricciones relevantes.

No debe revelar tokens, rutas sensibles ni handlers internos.

## `authoring.create_gameobject`

**Tipo:** mutacion
**Capacidad prevista:** `scene.gameobject.create`
**Version objetivo:** v0.3

Crea un `GameObject` vacio en una escena autorizada. Sus argumentos minimos
previstos son nombre, escena esperada, padre opcional y transform inicial
opcional.

La operacion debe:

- exigir Edit Mode y contexto vigente;
- usar Undo;
- rechazar escenas sin persistir cuando la identidad lo requiera;
- dejar la escena sucia;
- no guardar automaticamente;
- devolver una referencia serializable al objeto creado.

## `authoring.add_component`

**Tipo:** mutacion
**Capacidad prevista:** `scene.component.add`
**Version objetivo:** v0.3

Anade un componente de un catalogo permitido a un `GameObject` existente.

No acepta nombres de tipos arbitrarios. Cada componente habilitado debe tener
un adaptador o registro explicito, validacion propia y politica de
compatibilidad.

## `authoring.set_component_field`

**Tipo:** mutacion
**Capacidad prevista:** `scene.component.write`
**Version objetivo:** v0.3

Modifica un campo autorizado de un componente registrado. El contrato debe
identificar:

- objeto objetivo;
- componente objetivo sin reflection abierta;
- campo permitido;
- valor serializable con tipo validado;
- contexto esperado.

No proporciona acceso generico al `SerializedObject` ni a cualquier propiedad
descubierta dinamicamente.

## `validate.active_scene`

**Tipo:** verificacion
**Capacidad prevista:** `validation.scene.run`
**Version objetivo:** v0.5, ampliado en v0.6

Ejecuta validadores registrados sobre la escena activa y devuelve resultados
estructurados sin corregir automaticamente los problemas.

Los resultados deben diferenciar errores, warnings e informacion, e identificar
el validador y el objeto afectado cuando sea posible.

## Convenciones

- Lecturas no mutan ni guardan.
- Mutaciones requieren precondiciones y permiso.
- `dryRun` valida sin ejecutar cuando el handler lo soporte.
- Los comandos no se agrupan implicitamente en transacciones.
- Reutilizar un `commandId` con el mismo payload devuelve el resultado terminal
  conocido.
- Reutilizar un `commandId` con otro payload debe fallar cerrado.
- Un comando desconocido debe fallar cerrado.
- Cambiar un contrato incompatible requiere una nueva version de protocolo.
