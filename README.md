# UnityIA

UnityIA es un copiloto de authoring controlado para el Unity Editor.

UnityIA no es un agente libre capaz de programar cualquier juego, ejecutar
codigo arbitrario o modificar un proyecto Unity sin restricciones. Su
proposito es permitir que agentes, herramientas y pruebas soliciten operaciones
pequenas y auditables a traves de contratos publicos mantenidos por UnityIA.

## Principio rector

Toda accion automatizada debe seguir una ruta controlada:

```text
Agente o usuario
  -> unityia CLI o interfaz autorizada
  -> comando estructurado y versionado
  -> validacion
  -> permisos
  -> CommandDispatcher
  -> UnityIAAuthoringAPI o facade equivalente
  -> API publica del Unity Editor
  -> ActionResult serializable y auditoria
```

La automatizacion no debe editar directamente YAML, archivos `.meta`,
`Library`, `ProjectSettings` o `Packages`. Tampoco debe disponer de shell
arbitrario ni generar scripts C# como mecanismo de escape.

## Equivalencia conceptual

| Concepto | Papel en UnityIA |
|---|---|
| Unity Scene, Prefab y Asset | Fuente persistente de verdad |
| Estado del Unity Editor | Estado editable de authoring |
| Play Mode | Runtime temporal, no fuente persistente |
| `UnityIAAuthoringAPI` | Fachada publica para mutaciones autorizadas |
| `CommandDispatcher` | Nucleo que coordina validacion, permisos, auditoria e idempotencia |
| `unityia` CLI | Entrada externa para agentes y automatizacion |
| Validation y Test APIs | Verificacion previa y posterior |
| Protocolo JSON | Contrato versionado entre clientes y Unity |

## Estado actual y contrato objetivo

Este repositorio contiene documentacion, un package Unity Editor-only y un CLI
.NET en evolucion. El plan maestro en `docs/project-plan.md` separa el estado
implementado del contrato futuro y debe usarse como fuente operativa antes de
ampliar capacidades.

En el roadmap actual:

- `v0.1` = foundation documental, protocolo base y contratos minimos.
- `v0.2` = package base de Unity y piezas tecnicas de soporte.
- `v0.3` = primera Authoring API controlada.
- `v0.6` = capabilities, validacion de escena activa y confirmacion.

El paquete actual en `packages/com.unityia.authoring` entrega el catalogo
publico inicial de authoring y validacion documentado hasta v0.6. No debe
describirse como una implementacion completa mas alla de esos comandos ni como
una autorizacion para usar prototipos tecnicos como API estable.

La existencia de una clase, schema o comando experimental en el repositorio no
lo convierte en API estable. Los primeros comandos publicos de authoring v0.3
son:

- `context.snapshot`
- `authoring.create_gameobject`
- `authoring.add_component`
- `authoring.set_component_field`
- `authoring.save_scene`

Los comandos publicos incorporados en v0.6 son:

- `capabilities.list`
- `validate.active_scene`

Las mutaciones autorizadas bajo `confirm_actions` requieren aprobacion
explicita antes de ejecutarse.

Un comando solo pasa de reservado a implementado cuando existe su DTO,
validador, registro publico, permisos, auditoria, `ActionResult` estable y
pruebas verificadas.

Consultar [docs/commands.md](docs/commands.md) antes de implementar o consumir
un comando.

## Documentacion

- [Plan maestro](docs/project-plan.md)
- [Arquitectura](docs/architecture.md)
- [CLI](docs/cli.md)
- [Comandos](docs/commands.md)
- [Integracion IA](docs/ai-integration.md)
- [Permisos](docs/permissions.md)
- [Roadmap](docs/roadmap.md)
- [Protocolo v0.1](docs/protocol/v0.1.md)
- [Sandbox de desarrollo](docs/development/sandbox.md)
- [Glosario](docs/glossary.md)

## Reglas para agentes Codex

1. Leer esta documentacion antes de modificar arquitectura o contratos.
2. No acceder a internals de UnityIA para evitar saltarse la API publica.
3. No introducir IA, generacion de C# o shell antes de la fase correspondiente.
4. Mantener separados el estado implementado y el contrato planificado.
5. Anadir documentacion y pruebas junto con cada capacidad publica.
6. Tratar cualquier capacidad no documentada como denegada.
