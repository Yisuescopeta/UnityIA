# Glosario de UnityIA

## ActionResult

Resultado serializable obligatorio de una operación pública. Contiene al menos
`success`, `message`, `code` y `data`. Los clientes automatizados deben usar
`code` y `data`, no interpretar texto libre.

## Authoring

Edición persistente de escenas, prefabs y assets mediante el Unity Editor. No es
equivalente a ejecutar el juego en Play Mode.

## Batch mode

Modo explícito que inicia Unity sin interacción gráfica para ejecutar comandos,
validaciones o pruebas registradas.

## Capability

Permiso semántico y específico requerido por una operación, por ejemplo
`scene.component.add`. Una capability no es un método C# ni una ruta de acceso a
internals.

## CommandDispatcher

Componente Core que recibe un comando validado, localiza un handler registrado y
coordina estado, permisos, auditoría, idempotencia y resultado.

## CommandEnvelope

Contenedor JSON versionado de una solicitud. Incluye identificador, nombre del
comando, argumentos, precondiciones y opciones.

## `confirm_actions`

Modo de autorización previsto en el que cada mutación permitida necesita
confirmación explícita antes de ejecutarse.

## Context version

Número que representa una instantánea lógica del estado editable. Permite
rechazar comandos construidos sobre una jerarquía o escena que ya cambió.

## Contrato objetivo

Comportamiento público aprobado por documentación y gobernanza. Puede estar
planificado aunque todavía no exista implementación.

## Estado actual

Comportamiento que existe y ha sido verificado en el repositorio en un momento
concreto. No se convierte automáticamente en contrato estable.

## `full_access`

Modo futuro que omitiría confirmaciones individuales dentro de capacidades
preautorizadas. No elimina límites, validación, auditoría ni seguridad.

## GlobalObjectId

Identificador de Unity útil para referenciar objetos persistidos. Puede no estar
disponible para objetos nuevos o escenas sin guardar y puede cambiar al mover un
objeto entre escenas.

## Handler

Implementación interna registrada para un comando concreto. Los clientes no lo
invocan directamente.

## Idempotencia

Propiedad por la que repetir una solicitud con el mismo identificador no aplica
la mutación dos veces y devuelve el resultado terminal conocido.

## Live editor mode

Modo interactivo en el que un cliente se comunica con una instancia abierta del
Unity Editor y el trabajo Unity se ejecuta en su hilo principal.

## Play Mode

Runtime temporal de Unity. Sus cambios no son fuente persistente de verdad para
UnityIA salvo que una futura operación controlada defina lo contrario.

## Fuente persistente de verdad

Scenes, Prefabs y Assets guardados por Unity. El estado del Editor es una vista
editable; Play Mode es temporal.

## UnityIAAuthoringAPI

Fachada pública objetivo para operaciones de authoring autorizadas. Evita que
agentes, CLI y tests dependan de internals.

## Validation API

Fachada para validar contratos, estado, contexto y contenido sin realizar
mutaciones implícitas.

## Test API

Fachada para ejecutar suites registradas y obtener resultados serializables. No
permite ejecutar código de test arbitrario proporcionado por un agente.

## `unityia` CLI

Cliente externo oficial del protocolo. Solicita operaciones; no edita
directamente archivos Unity.

## Undo

Sistema del Unity Editor que debe registrar las mutaciones de escena cuando la
API correspondiente lo soporte.

