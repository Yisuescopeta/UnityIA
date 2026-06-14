# Arquitectura de UnityIA

## Propósito

UnityIA proporciona una capa de authoring controlada dentro de Unity. Permite
automatizar tareas del Editor sin entregar a un agente acceso libre al proyecto,
al sistema de archivos o a la ejecución de código.

La arquitectura se diseña como infraestructura de motor/editor, no como un
chatbot ni como un plugin que traduce texto directamente a C#.

## Modelo conceptual

UnityIA adopta estas equivalencias:

- **Unity Scene, Prefab y Asset:** verdad persistente del proyecto.
- **Estado del Unity Editor:** proyección editable de esa verdad.
- **Play Mode:** ejecución temporal; no es authoring persistente.
- **`UnityIAAuthoringAPI`:** fachada pública para cambios autorizados.
- **`unityia` CLI:** entrada externa para agentes, CI y automatización.
- **Validation API:** verifica contratos, contexto y estado sin asumir éxito.
- **Test API:** ejecuta verificaciones registradas y devuelve resultados
  estructurados.

Las mutaciones deben atravesar APIs públicas de UnityIA. Un cliente nunca debe
depender de handlers internos, reflection genérica o archivos serializados de
Unity.

## Flujo objetivo

```text
CLI / Developer UI / pruebas
  -> CommandEnvelope versionado
  -> autenticación del transporte
  -> CommandDispatcher
  -> validación de contrato
  -> validación de contexto y estado
  -> evaluación de permisos
  -> inicio de auditoría
  -> handler registrado
  -> UnityIAAuthoringAPI
  -> API pública del Unity Editor + Undo
  -> validación posterior opcional
  -> ActionResult + cierre de auditoría
```

Cada frontera tiene una responsabilidad:

- **Contracts:** DTOs, envelopes, códigos y `ActionResult`.
- **Core:** dispatcher, registro, validación base, permisos, auditoría e
  idempotencia.
- **Context:** lectura serializable del Editor y resolución controlada de
  referencias.
- **Authoring:** operaciones de Unity registradas explícitamente.
- **Transport:** entrada live o batch; no contiene lógica de authoring.
- **CLI:** cliente externo del protocolo, no editor de archivos Unity.
- **Validation/Tests:** comprobaciones explícitas y reproducibles.

## Modos de operación

### Live editor mode

El CLI se comunica con una instancia abierta del Unity Editor. El transporte
recibe comandos y los encola; toda llamada a APIs de Unity se ejecuta en el hilo
principal del Editor.

Es el modo interactivo prioritario. Debe conservar Undo, contexto de escena y
feedback inmediato. La disponibilidad de un puente live concreto depende de la
fase del roadmap.

### Batch mode

Unity se inicia sin interfaz para procesar una operación o suite controlada.
Está orientado a CI, validación reproducible y tareas no interactivas.

Batch no es un fallback automático de live mode. Debe seleccionarse
explícitamente y reutilizar el mismo protocolo y dispatcher.

### `confirm_actions`

Política de autorización prevista para operaciones mutadoras. Las lecturas
permitidas pueden ejecutarse directamente, pero cada acción que cambie escenas,
prefabs o assets requiere confirmación explícita antes de llegar al handler.

La confirmación no sustituye permisos, validación ni auditoría.

### `full_access`

Modo reservado para una fase futura. Significaría ejecutar sin confirmación
individual dentro de capacidades y rutas previamente autorizadas.

No significa acceso ilimitado al equipo, shell arbitrario, escritura fuera del
proyecto ni evasión de las APIs públicas. No debe implementarse ni activarse
hasta contar con un modelo de amenazas, pruebas de seguridad y mecanismos de
revocación.

## Invariantes

1. Toda operación pública devuelve un `ActionResult` serializable.
2. Toda mutación valida contrato, estado, contexto y permisos.
3. Las mutaciones de escena utilizan Undo cuando Unity lo permite.
4. Guardar es una operación explícita, nunca un efecto lateral implícito.
5. Los comandos son pequeños, auditables e idempotentes cuando sea viable.
6. Las capacidades se registran; no se descubren mediante reflection abierta.
7. Ningún transporte conoce directamente los handlers de Authoring.
8. Play Mode no se trata como fuente persistente de verdad.

## Estado actual

**Contrato objetivo:** la arquitectura descrita en este documento.

**Estado del repositorio:** existe un prototipo técnico no consolidado que
explora algunas de estas capas. Sus nombres y comportamientos no deben
considerarse estables hasta que una versión del roadmap los declare
implementados y cuente con pruebas de aceptación.

