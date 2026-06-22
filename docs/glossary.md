# Glosario de UnityIA

Este glosario acompana al [plan maestro](project-plan.md), el
[roadmap](roadmap.md), el [catalogo de comandos](commands.md) y el documento de
[permisos](permissions.md).

## ActionResult

Resultado serializable obligatorio de una operacion publica. Contiene al menos
`success`, `message`, `code` y `data`. `data` debe ser siempre un objeto JSON.
Los clientes automatizados deben usar `code` y `data`, no interpretar texto
libre.

## Authoring

Edicion persistente de escenas, prefabs y assets mediante el Unity Editor. No
es equivalente a ejecutar el juego en Play Mode.

## Batch mode

Modo explicito que inicia Unity sin interaccion grafica para ejecutar comandos,
validaciones o pruebas registradas.

## Capability

Permiso semantico y especifico requerido por una operacion, por ejemplo
`scene.component.add`. Una capability no es un metodo C# ni una ruta de acceso
a internals.

## CommandDispatcher

Componente Core que recibe un comando validado, localiza un handler registrado y
coordina estado, permisos, auditoria, idempotencia y resultado.

## CommandEnvelope

Contenedor JSON versionado de una solicitud. Incluye identificador, nombre del
comando, argumentos, precondiciones y opciones.

## `confirm_actions`

Modo de autorizacion en el que cada mutacion permitida necesita confirmacion
explicita antes de ejecutarse.

## Context version

Numero que representa una instantanea logica del estado editable. Permite
rechazar comandos construidos sobre una jerarquia, seleccion o escena que ya
cambiaron.

## Contrato objetivo

Comportamiento publico aprobado por documentacion y gobernanza. Puede estar
planificado aunque todavia no exista implementacion.

## Estado actual

Comportamiento que existe y ha sido verificado en el repositorio en un momento
concreto. No se convierte automaticamente en contrato estable.

## `full_access`

Modo futuro que omitiria confirmaciones individuales dentro de capacidades
preautorizadas. No elimina limites, validacion, auditoria ni seguridad.

## GlobalObjectId

Identificador de Unity util para referenciar objetos persistidos. Puede no
estar disponible para objetos nuevos o escenas sin guardar y puede cambiar al
mover un objeto entre escenas.

## Handler

Implementacion interna registrada para un comando concreto. Los clientes no lo
invocan directamente.

## Idempotencia

Propiedad por la que repetir una solicitud con el mismo identificador y el
mismo payload no aplica la mutacion dos veces y devuelve el resultado terminal
conocido. Reutilizar el mismo `commandId` con otro payload debe fallar cerrado.

## Live editor mode

Modo interactivo en el que un cliente se comunica con una instancia abierta del
Unity Editor y el trabajo Unity se ejecuta en su hilo principal.

## Play Mode

Runtime temporal de Unity. Sus cambios no son fuente persistente de verdad para
UnityIA salvo que una futura operacion controlada defina lo contrario.

## Fuente persistente de verdad

Scenes, Prefabs y Assets guardados por Unity. El estado del Editor es una vista
editable; Play Mode es temporal.

## UnityIAAuthoringAPI

Fachada publica objetivo para operaciones de authoring autorizadas. Evita que
agentes, CLI y tests dependan de internals.

## Validation API

Fachada para validar contratos, estado, contexto y contenido sin realizar
mutaciones implicitas.

## Test API

Fachada para ejecutar suites registradas y obtener resultados serializables. No
permite ejecutar codigo de test arbitrario proporcionado por un agente.

## `unityia` CLI

Cliente externo oficial del protocolo. Solicita operaciones; no edita
directamente archivos Unity.

## Undo

Sistema del Unity Editor que debe registrar las mutaciones de escena cuando la
API correspondiente lo soporte.
