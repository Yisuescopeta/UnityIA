# Instrucciones para agentes en UnityIA

Antes de implementar cualquier funcionalidad en este proyecto, lee en este
orden:

1. `README.md`
2. `docs/project-plan.md`
3. `docs/roadmap.md`
4. `docs/commands.md`
5. `docs/permissions.md`

El plan maestro esta en `docs/project-plan.md`. Usalo para decidir por donde
continuar, que fase corresponde y que capacidades todavia no son estables.

## Reglas obligatorias

- No implementes funcionalidades fuera del orden marcado por
  `docs/project-plan.md` salvo que el usuario lo pida explicitamente y quede
  documentado como decision.
- No trates prototipos como API estable. En particular, los comandos tecnicos
  `scene.object.*` no sustituyen automaticamente al catalogo publico
  `authoring.*`.
- No introduzcas IA, generacion libre de C#, shell arbitrario, `full_access`,
  cambios en `ProjectSettings` o cambios en `Packages` antes de la fase
  correspondiente.
- Toda capacidad publica nueva debe incluir contrato, validacion, permisos,
  auditoria, `ActionResult`, documentacion y pruebas.
- No edites YAML, `.meta`, `Library`, `ProjectSettings`, `Packages` ni assets
  de Unity directamente como mecanismo de authoring.

## Modificacion del plan

Solo puedes modificar `docs/project-plan.md` para:

- marcar partes como terminadas cuando cumplan su criterio de salida;
- anadir comentarios de estado, bloqueos, deuda tecnica o trabajo pendiente;
- registrar decisiones importantes descubiertas durante el desarrollo.

No reescribas el plan para hacerlo coincidir con una implementacion incompleta.
Si encuentras una contradiccion entre codigo, docs y plan, documenta el
conflicto y corrige la fuente adecuada.
