# Roadmap de UnityIA

El roadmap expresa orden y criterios de salida, no promesas de disponibilidad.
Una version solo esta completada cuando sus contratos, implementacion,
documentacion y pruebas de aceptacion estan alineados.

## v0.1 Foundation

- principios de arquitectura y seguridad;
- estructura del repositorio;
- glosario y gobernanza;
- protocolo JSON base;
- contrato base de `ActionResult`;
- distincion entre contrato objetivo y estado actual;
- reglas de contribucion para agentes.

**Salida:** otro agente Codex puede explicar limites, componentes y orden de
trabajo sin inferir capacidades no documentadas.

## v0.2 Unity package base

- package Editor-only para Unity 6.3 LTS;
- assemblies y dependencias unidireccionales;
- Contracts y Core minimos;
- `CommandDispatcher` como nucleo;
- registro explicito de handlers;
- ventana tecnica para invocar el dispatcher sin red.

Puede existir codigo de prototipo para probar piezas del dispatcher o escenas
de ejemplo, pero eso no convierte esos comandos en una Authoring API publica ni
adelanta `v0.3`.

**No incluye:** puente CLI live ni IA.

## v0.3 Authoring API

- `UnityIAAuthoringAPI`;
- Context API;
- `context.snapshot`;
- `authoring.create_gameobject`;
- catalogo cerrado para `authoring.add_component`;
- campos registrados para `authoring.set_component_field`;
- Undo, escena sucia y guardado explicito;
- pruebas EditMode.

**Salida:** authoring controlado desde APIs y ventana local.

## v0.4 CLI live bridge

- CLI .NET 8;
- descubrimiento inequivoco de sesion;
- transporte loopback autenticado;
- ejecucion en el hilo principal del Editor;
- `ActionResult` en stdout;
- pruebas CLI -> transporte -> dispatcher.

**Salida:** un agente externo puede usar live editor mode sin acceso directo a
archivos Unity.

## v0.5 Batch/tests

- batch mode explicito;
- Test API para suites registradas;
- `validate.active_scene`;
- ejecucion reproducible en CI;
- resultados de pruebas serializables;
- rechazo de proyectos bloqueados.

**Salida:** live y batch comparten contratos y dispatcher.

## v0.6 Validation/capabilities

- catalogo estable de capacidades;
- `capabilities.list`;
- schema definitivo de `.unityia/policy.json`;
- modo `confirm_actions`;
- validacion ampliada de escenas y contexto;
- auditoria y pruebas negativas de seguridad;
- evaluacion documentada de JSON Schema dentro de Unity.

**Salida:** permisos y capacidades pueden auditarse y explicarse antes de
integrar IA.

## v0.7 IA integration

- integracion con un proveedor de IA mediante una capa desacoplada;
- traduccion de intencion a comandos publicos existentes;
- ninguna capacidad nueva por el hecho de usar IA;
- confirmacion segun politica;
- trazabilidad entre intencion, comandos y resultados;
- evaluacion de seguridad y calidad.

`full_access` continua reservado hasta superar una revision independiente de
seguridad. No forma parte automatica de v0.7.

## Fuera del roadmap inicial

- agente general para programar cualquier juego;
- generacion libre de scripts C#;
- shell arbitrario;
- edicion directa de YAML o `.meta`;
- acceso no controlado a `ProjectSettings` o `Packages`;
- reflection generica como API publica;
- autosave de mutaciones.

## Estado del repositorio

Hay un prototipo tecnico previo que puede adelantar piezas de fases posteriores.
No modifica el orden de gobernanza ni permite declarar una version completada.
Antes de reutilizarlo, cada pieza debe reconciliarse con los nombres,
permisos, contratos y criterios de este roadmap.
