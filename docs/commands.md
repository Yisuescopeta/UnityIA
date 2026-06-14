# Catálogo inicial de comandos

## Estado del catálogo

Esta página define nombres reservados y contratos objetivo. Los comandos
enumerados aquí **todavía no se consideran implementados**, aunque el
repositorio pueda contener prototipos con nombres o comportamientos parecidos.

Un comando pasa a estado implementado únicamente cuando:

1. existe su DTO y validador;
2. está registrado públicamente;
3. aplica permisos y auditoría;
4. devuelve `ActionResult`;
5. tiene pruebas de contrato y comportamiento;
6. la documentación indica la versión que lo entrega.

## Envelope común objetivo

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

No se permiten nombres de métodos, tipos arbitrarios, código C# ni comandos de
shell dentro de `arguments`.

## `context.snapshot`

**Tipo:** lectura  
**Capacidad prevista:** `context.read`  
**Versión objetivo:** v0.3

Devuelve una instantánea serializable del contexto de authoring:

- sesión y versión del contexto;
- modo del Editor;
- escena activa;
- escenas abiertas;
- selección actual cuando sea seguro exponerla;
- referencias de objetos mediante ruta y un identificador estable cuando esté
  disponible.

No modifica el proyecto.

## `capabilities.list`

**Tipo:** lectura  
**Capacidad prevista:** ninguna para consultar el catálogo público  
**Versión objetivo:** v0.6

Enumera las capacidades y comandos registrados en la sesión, incluyendo:

- versión del protocolo;
- disponibilidad por modo live o batch;
- si una operación es mutadora;
- permiso requerido;
- estado implementado o no disponible;
- restricciones relevantes.

No debe revelar tokens, rutas sensibles ni handlers internos.

## `authoring.create_gameobject`

**Tipo:** mutación  
**Capacidad prevista:** `scene.gameobject.create`  
**Versión objetivo:** v0.3

Crea un `GameObject` vacío en una escena autorizada. Sus argumentos mínimos
previstos son nombre, escena esperada, padre opcional y transform inicial
opcional.

La operación debe:

- exigir Edit Mode y contexto vigente;
- usar Undo;
- rechazar escenas sin persistir cuando la identidad lo requiera;
- dejar la escena sucia;
- no guardar automáticamente;
- devolver una referencia serializable al objeto creado.

## `authoring.add_component`

**Tipo:** mutación  
**Capacidad prevista:** `scene.component.add`  
**Versión objetivo:** v0.3

Añade un componente de un catálogo permitido a un `GameObject` existente.

No acepta nombres de tipos arbitrarios. Cada componente habilitado debe tener un
adaptador o registro explícito, validación propia y política de compatibilidad.

## `authoring.set_component_field`

**Tipo:** mutación  
**Capacidad prevista:** `scene.component.write`  
**Versión objetivo:** v0.3

Modifica un campo autorizado de un componente registrado. El contrato debe
identificar:

- objeto objetivo;
- componente objetivo sin reflection abierta;
- campo permitido;
- valor serializable con tipo validado;
- contexto esperado.

No proporciona acceso genérico al `SerializedObject` ni a cualquier propiedad
descubierta dinámicamente.

## `validate.active_scene`

**Tipo:** verificación  
**Capacidad prevista:** `validation.scene.run`  
**Versión objetivo:** v0.5, ampliado en v0.6

Ejecuta validadores registrados sobre la escena activa y devuelve resultados
estructurados sin corregir automáticamente los problemas.

Los resultados deben diferenciar errores, warnings e información, e identificar
el validador y el objeto afectado cuando sea posible.

## Convenciones

- Lecturas no mutan ni guardan.
- Mutaciones requieren precondiciones y permiso.
- `dryRun` valida sin ejecutar cuando el handler lo soporte.
- Los comandos no se agrupan implícitamente en transacciones.
- Un comando desconocido debe fallar cerrado.
- Cambiar un contrato incompatible requiere una nueva versión de protocolo.

