# Permisos y modos de autorización

## Objetivo

El sistema de permisos limita qué puede solicitar un agente incluso cuando el
transporte está autenticado. Autenticación, permisos, confirmación y auditoría
son controles distintos y todos pueden ser obligatorios.

UnityIA aplica denegación por defecto:

- una capacidad no declarada está prohibida;
- una ruta no autorizada está prohibida;
- una mutación sin política válida está prohibida;
- un comando no registrado está prohibido.

## Política de proyecto

La política versionable prevista reside en:

```text
.unityia/policy.json
```

UnityIA puede leerla, pero los comandos públicos no deben modificarla. Los
cambios de política son una acción administrativa fuera del protocolo de
authoring.

La política debe expresar como mínimo:

- versión;
- modo de autorización;
- capacidades permitidas;
- rutas de lectura;
- rutas de escritura;
- restricciones específicas cuando existan.

El schema y la forma definitiva evolucionarán con v0.6. Cualquier archivo de
ejemplo anterior a esa versión es provisional.

## Modos de autorización

### `confirm_actions`

Modo previsto para adopción inicial:

- las lecturas permitidas pueden ejecutarse;
- cada mutación requiere confirmación explícita;
- la confirmación debe mostrar comando, objetivo, capacidad y efecto esperado;
- denegar o expirar la confirmación cancela la operación.

Confirmar una acción no concede nuevas capacidades ni amplía rutas.

### `full_access`

Reservado para una versión posterior a la integración inicial de IA.

Permite omitir confirmaciones individuales solo dentro de capacidades y rutas
preautorizadas. Mantiene validación, auditoría, Undo y límites de transporte.

`full_access` nunca significa:

- acceso libre al sistema de archivos;
- shell arbitrario;
- edición directa de archivos Unity;
- generación y ejecución libre de C#;
- acceso a internals;
- desactivar auditoría o validación.

No debe implementarse hasta disponer de threat model, revocación inmediata,
pruebas negativas y una indicación visible de que está activo.

## Modos de ejecución y permisos

Live editor mode y batch mode no conceden permisos por sí mismos. Una capacidad
puede permitirse en un modo y denegarse en otro.

Ejemplos:

- una lectura de contexto puede estar disponible en live;
- una validación de proyecto puede estar disponible en batch;
- una mutación interactiva puede exigir live + `confirm_actions`;
- una capacidad no declarada se deniega en ambos.

## Capacidades iniciales objetivo

Los nombres definitivos se estabilizarán en v0.6. El catálogo inicial previsto
incluye:

- `context.read`
- `capabilities.read`
- `scene.gameobject.create`
- `scene.component.add`
- `scene.component.write`
- `validation.scene.run`
- `tests.run`

Las capacidades deben ser específicas. No se añadirá una capacidad global como
`unity.execute_anything`.

## Rutas seguras

Las operaciones de contenido solo podrán actuar sobre rutas normalizadas y
autorizadas dentro de `Assets/`.

Se rechazan:

- rutas absolutas;
- segmentos `..`;
- enlaces que escapen del proyecto;
- `Library`;
- `ProjectSettings`;
- `Packages`;
- `UserSettings`;
- `Temp`;
- cualquier ubicación externa.

Una fase futura podrá autorizar excepciones concretas, pero nunca de forma
implícita.

## Auditoría

Cada solicitud debe producir un registro con identificador, timestamp, comando,
decisión de permisos y resultado. Los logs no deben incluir:

- bearer tokens;
- secretos;
- payloads sensibles completos;
- stacks internos innecesarios.

Si la auditoría requerida no está disponible, las mutaciones fallan cerradas.
Las lecturas podrán operar en modo degradado solo si el resultado incluye un
warning explícito.

## Estado actual

El modelo descrito es el contrato de gobernanza. El prototipo existente no debe
considerarse implementación completa de `confirm_actions`, `full_access` ni del
catálogo de capacidades objetivo.

