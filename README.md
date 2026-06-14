# UnityIA

UnityIA es un copiloto de authoring controlado para el Unity Editor.

UnityIA no es un agente libre capaz de programar cualquier juego, ejecutar código
arbitrario o modificar un proyecto Unity sin restricciones. Su propósito es
permitir que agentes, herramientas y pruebas soliciten operaciones pequeñas y
auditables a través de contratos públicos mantenidos por UnityIA.

## Principio rector

Toda acción automatizada debe seguir una ruta controlada:

```text
Agente o usuario
  -> unityia CLI o interfaz autorizada
  -> comando estructurado y versionado
  -> validación
  -> permisos
  -> UnityIAAuthoringAPI
  -> API pública del Unity Editor
  -> ActionResult serializable y auditoría
```

La automatización no debe editar directamente YAML, archivos `.meta`,
`Library`, `ProjectSettings` o `Packages`. Tampoco debe disponer de shell
arbitrario ni generar scripts C# como mecanismo de escape.

## Equivalencia conceptual

| Concepto | Papel en UnityIA |
|---|---|
| Unity Scene, Prefab y Asset | Fuente persistente de verdad |
| Estado del Unity Editor | Estado editable de authoring |
| Play Mode | Runtime temporal, no fuente persistente |
| `UnityIAAuthoringAPI` | Fachada pública para mutaciones autorizadas |
| `unityia` CLI | Entrada externa para agentes y automatización |
| Validation y Test APIs | Verificación previa y posterior |
| Protocolo JSON | Contrato versionado entre clientes y Unity |

## Estado actual y contrato objetivo

Este repositorio contiene documentación y un prototipo técnico no consolidado.
La documentación de nivel superior en `docs/*.md` define el **contrato
objetivo** y la gobernanza que deben guiar el desarrollo.

La existencia de una clase, schema o comando experimental en el repositorio no
lo convierte en API estable. En particular, los comandos documentados para la
primera interfaz pública están **reservados pero todavía no se consideran
implementados**:

- `context.snapshot`
- `capabilities.list`
- `authoring.create_gameobject`
- `authoring.add_component`
- `authoring.set_component_field`
- `validate.active_scene`

Consultar [docs/commands.md](docs/commands.md) antes de implementar o consumir
un comando.

## Documentación

- [Arquitectura](docs/architecture.md)
- [CLI](docs/cli.md)
- [Comandos](docs/commands.md)
- [Permisos](docs/permissions.md)
- [Roadmap](docs/roadmap.md)
- [Glosario](docs/glossary.md)

## Reglas para agentes Codex

1. Leer esta documentación antes de modificar arquitectura o contratos.
2. No acceder a internals de UnityIA para evitar una API pública.
3. No introducir IA, generación de C# o shell antes de la fase correspondiente.
4. Mantener separados el estado implementado y el contrato planificado.
5. Añadir documentación y pruebas junto con cada capacidad pública.
6. Tratar cualquier capacidad no documentada como denegada.
