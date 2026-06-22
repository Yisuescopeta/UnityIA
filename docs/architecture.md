# Arquitectura de UnityIA

## Proposito

UnityIA proporciona una capa de authoring controlada dentro de Unity. Permite
automatizar tareas del Editor sin entregar a un agente acceso libre al proyecto,
al sistema de archivos o a la ejecucion de codigo.

La arquitectura se disena como infraestructura de motor/editor, no como un
chatbot ni como un plugin que traduce texto directamente a C#.

El orden operativo vigente esta en [project-plan.md](project-plan.md). El
roadmap publico esta en [roadmap.md](roadmap.md).

## Modelo conceptual

UnityIA adopta estas equivalencias:

- **Unity Scene, Prefab y Asset:** verdad persistente del proyecto.
- **Estado del Unity Editor:** proyeccion editable de esa verdad.
- **Play Mode:** ejecucion temporal; no es authoring persistente.
- **`UnityIAAuthoringAPI`:** fachada publica para cambios autorizados.
- **`unityia` CLI:** entrada externa para agentes, CI y automatizacion.
- **Validation API:** verifica contratos, contexto y estado sin asumir exito.
- **Test API:** ejecuta verificaciones registradas y devuelve resultados
  estructurados.

Las mutaciones deben atravesar APIs publicas de UnityIA. Un cliente nunca debe
depender de handlers internos, reflection generica o archivos serializados de
Unity.

## Flujo objetivo

```text
CLI / Developer UI / pruebas
  -> CommandEnvelope versionado
  -> autenticacion del transporte
  -> CommandDispatcher
  -> validacion de contrato
  -> validacion de contexto y estado
  -> evaluacion de permisos
  -> inicio de auditoria
  -> handler registrado
  -> UnityIAAuthoringAPI
  -> API publica del Unity Editor + Undo
  -> validacion posterior opcional
  -> ActionResult + cierre de auditoria
```

Cada frontera tiene una responsabilidad:

- **Contracts:** DTOs, envelopes, codigos y `ActionResult`.
- **Core:** dispatcher, registro, validacion base, permisos, auditoria e
  idempotencia.
- **Context:** lectura serializable del Editor y resolucion controlada de
  referencias.
- **Authoring:** operaciones de Unity registradas explicitamente.
- **Transport:** entrada live o batch; no contiene logica de authoring.
- **CLI:** cliente externo del protocolo, no editor de archivos Unity.
- **Validation/Tests:** comprobaciones explicitas y reproducibles.

## Modos de operacion

### Live editor mode

El CLI se comunica con una instancia abierta del Unity Editor. El transporte
recibe comandos y los encola; toda llamada a APIs de Unity se ejecuta en el hilo
principal del Editor.

Es el modo interactivo prioritario. Debe conservar Undo, contexto de escena y
feedback inmediato. La disponibilidad de un puente live concreto depende de la
fase del roadmap.

### Batch mode

Unity se inicia sin interfaz para procesar una operacion o suite controlada.
Esta orientado a CI, validacion reproducible y tareas no interactivas.

Batch no es un fallback automatico de live mode. Debe seleccionarse
explicitamente y reutilizar el mismo protocolo y dispatcher.

### `confirm_actions`

Politica de autorizacion prevista para operaciones mutadoras. Las lecturas
permitidas pueden ejecutarse directamente, pero cada accion que cambie escenas,
prefabs o assets requiere confirmacion explicita antes de llegar al handler.

La confirmacion no sustituye permisos, validacion ni auditoria.
En v0.6 la aprobacion se vincula al `commandId` y hash canonico del payload.

### `full_access`

Modo reservado para una fase futura. Significaria ejecutar sin confirmacion
individual dentro de capacidades y rutas previamente autorizadas.

No significa acceso ilimitado al equipo, shell arbitrario, escritura fuera del
proyecto ni evasion de las APIs publicas. No debe implementarse ni activarse
hasta contar con un modelo de amenazas, pruebas de seguridad y mecanismos de
revocacion.

## Invariantes

1. Toda operacion publica devuelve un `ActionResult` serializable.
2. Toda mutacion valida contrato, estado, contexto y permisos.
3. Las mutaciones de escena utilizan Undo cuando Unity lo permite.
4. Guardar es una operacion explicita, nunca un efecto lateral implicito.
5. Los comandos son pequenos, auditables e idempotentes cuando sea viable.
6. Las capacidades se registran; no se descubren mediante reflection abierta.
7. Ningun transporte conoce directamente los handlers de Authoring.
8. Play Mode no se trata como fuente persistente de verdad.

## Estado actual

**Contrato objetivo:** la arquitectura descrita en este documento.

**Estado del repositorio:** el package y el CLI implementan el catalogo publico
inicial documentado hasta v0.6 segun el plan maestro. El repositorio conserva
comandos tecnicos de prototipo; sus nombres y comportamientos no deben
considerarse estables salvo que `commands.md` y el plan maestro los declaren
publicos con pruebas de aceptacion.

Los comandos publicos y reservados se documentan en [commands.md](commands.md).
Las reglas de autorizacion se documentan en [permissions.md](permissions.md), y
el contrato JSON base en [protocol/v0.1.md](protocol/v0.1.md).
