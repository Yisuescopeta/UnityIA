# Roadmap de UnityIA

El roadmap expresa orden y criterios de salida, no promesas de disponibilidad.
Una versión solo está completada cuando sus contratos, implementación,
documentación y pruebas de aceptación están alineados.

## v0.1 Foundation

- principios de arquitectura y seguridad;
- estructura del repositorio;
- glosario y gobernanza;
- contrato base de `ActionResult`;
- distinción entre contrato objetivo y estado actual;
- reglas de contribución para agentes.

**Salida:** otro agente Codex puede explicar límites, componentes y orden de
trabajo sin inferir capacidades no documentadas.

## v0.2 Unity package base

- package Editor-only para Unity 6.3 LTS;
- assemblies y dependencias unidireccionales;
- Contracts y Core mínimos;
- registro explícito de handlers;
- ventana técnica para invocar el dispatcher sin red.

**No incluye:** puente CLI live ni IA.

## v0.3 Authoring API

- `UnityIAAuthoringAPI`;
- Context API;
- `context.snapshot`;
- `authoring.create_gameobject`;
- catálogo cerrado para `authoring.add_component`;
- campos registrados para `authoring.set_component_field`;
- Undo, escena sucia y guardado explícito;
- pruebas EditMode.

**Salida:** authoring controlado desde APIs y ventana local.

## v0.4 CLI live bridge

- CLI .NET 8;
- descubrimiento inequívoco de sesión;
- transporte loopback autenticado;
- ejecución en el hilo principal del Editor;
- `ActionResult` en stdout;
- pruebas CLI → transporte → dispatcher.

**Salida:** un agente externo puede usar live editor mode sin acceso directo a
archivos Unity.

## v0.5 Batch/tests

- batch mode explícito;
- Test API para suites registradas;
- `validate.active_scene`;
- ejecución reproducible en CI;
- resultados de pruebas serializables;
- rechazo de proyectos bloqueados.

**Salida:** live y batch comparten contratos y dispatcher.

## v0.6 Validation/capabilities

- catálogo estable de capacidades;
- `capabilities.list`;
- schema definitivo de `.unityia/policy.json`;
- modo `confirm_actions`;
- validación ampliada de escenas y contexto;
- auditoría y pruebas negativas de seguridad;
- evaluación documentada de JSON Schema dentro de Unity.

**Salida:** permisos y capacidades pueden auditarse y explicarse antes de
integrar IA.

## v0.7 IA integration

- integración con un proveedor de IA mediante una capa desacoplada;
- traducción de intención a comandos públicos existentes;
- ninguna capacidad nueva por el hecho de usar IA;
- confirmación según política;
- trazabilidad entre intención, comandos y resultados;
- evaluación de seguridad y calidad.

`full_access` continúa reservado hasta superar una revisión independiente de
seguridad. No forma parte automática de v0.7.

## Fuera del roadmap inicial

- agente general para programar cualquier juego;
- generación libre de scripts C#;
- shell arbitrario;
- edición directa de YAML o `.meta`;
- acceso no controlado a `ProjectSettings` o `Packages`;
- reflection genérica como API pública;
- autosave de mutaciones.

## Estado del repositorio

Hay un prototipo técnico previo que puede adelantar piezas de fases posteriores.
No modifica el orden de gobernanza ni permite declarar una versión completada.
Antes de reutilizarlo, cada pieza debe reconciliarse con los nombres, permisos y
criterios de este roadmap.

