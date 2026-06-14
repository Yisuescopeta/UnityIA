# Permisos y modos de autorizacion

## Objetivo

El sistema de permisos limita que puede solicitar un agente incluso cuando el
transporte esta autenticado. Autenticacion, permisos, confirmacion y auditoria
son controles distintos y todos pueden ser obligatorios.

UnityIA aplica denegacion por defecto:

- una capacidad no declarada esta prohibida;
- una ruta no autorizada esta prohibida;
- una mutacion sin politica valida esta prohibida;
- un comando no registrado esta prohibido.

## Politica de proyecto

La politica versionable prevista reside en:

```text
.unityia/policy.json
```

UnityIA puede leerla, pero los comandos publicos no deben modificarla. Los
cambios de politica son una accion administrativa fuera del protocolo de
authoring.

La politica debe expresar como minimo:

- version;
- modo de autorizacion;
- capacidades permitidas;
- rutas de lectura;
- rutas de escritura;
- restricciones especificas cuando existan.

El schema y la forma definitiva evolucionaran con v0.6. Cualquier archivo de
ejemplo anterior a esa version es provisional.

## Modos de autorizacion

### `confirm_actions`

Modo previsto para adopcion inicial:

- las lecturas permitidas pueden ejecutarse;
- cada mutacion requiere confirmacion explicita;
- la confirmacion debe mostrar comando, objetivo, capacidad y efecto esperado;
- denegar o expirar la confirmacion cancela la operacion.

Confirmar una accion no concede nuevas capacidades ni amplia rutas.

### `full_access`

Reservado para una version posterior a la integracion inicial de IA.

Permite omitir confirmaciones individuales solo dentro de capacidades y rutas
preautorizadas. Mantiene validacion, auditoria, Undo y limites de transporte.

`full_access` nunca significa:

- acceso libre al sistema de archivos;
- shell arbitrario;
- edicion directa de archivos Unity;
- generacion y ejecucion libre de C#;
- acceso a internals;
- desactivar auditoria o validacion.

No debe implementarse hasta disponer de threat model, revocacion inmediata,
pruebas negativas y una indicacion visible de que esta activo.

## Modos de ejecucion y permisos

Live editor mode y batch mode no conceden permisos por si mismos. Una capacidad
puede permitirse en un modo y denegarse en otro.

Ejemplos:

- una lectura de contexto puede estar disponible en live;
- una validacion de proyecto puede estar disponible en batch;
- una mutacion interactiva puede exigir live + `confirm_actions`;
- una capacidad no declarada se deniega en ambos.

## Capacidades iniciales objetivo

Los nombres definitivos se estabilizaran en v0.6. El catalogo inicial previsto
incluye:

- `context.read`
- `capabilities.read`
- `scene.gameobject.create`
- `scene.component.add`
- `scene.component.write`
- `validation.scene.run`
- `tests.run`

Las capacidades deben ser especificas. No se anadira una capacidad global como
`unity.execute_anything`.

## Estado actual del package base

La policy por defecto del prototipo permite solo lecturas seguras documentadas:

- `context.read`
- `capabilities.read`

La policy por defecto no concede mutaciones.

El package base conserva aliases internos de prototipo para algunos handlers no
estables, por ejemplo `scene.modify` y `scene.save`. Esos nombres no forman
parte del catalogo publico objetivo y no deben tratarse como API estable.

## Rutas seguras

Las operaciones de contenido solo podran actuar sobre rutas normalizadas y
autorizadas dentro de `Assets/`.

La policy por defecto expone `Assets/**` solo para lecturas seguras
documentadas. Las mutaciones siguen denegadas por defecto.

Se rechazan:

- rutas absolutas;
- segmentos `..`;
- enlaces o reparse points que escapen del proyecto;
- `Library`;
- `ProjectSettings`;
- `Packages`;
- `UserSettings`;
- `Temp`;
- cualquier ubicacion externa.

Una fase futura podra autorizar excepciones concretas, pero nunca de forma
implicita.

## Auditoria

Cada solicitud debe producir un registro con identificador, timestamp, comando,
decision de permisos y resultado. Los logs no deben incluir:

- bearer tokens;
- secretos;
- payloads sensibles completos;
- stacks internos innecesarios.

Si la auditoria requerida no esta disponible, las mutaciones fallan cerradas.
Las lecturas pueden operar en modo degradado solo si el resultado incluye un
warning explicito.

## Estado actual

El modelo descrito es el contrato de gobernanza. El prototipo existente no debe
considerarse implementacion completa de `confirm_actions`, `full_access` ni del
catalogo de capacidades objetivo.
