using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityIA.Contracts;
using UnityIA.Core;

namespace UnityIA.Context
{
    [InitializeOnLoad]
    internal static class ContextBootstrap
    {
        static ContextBootstrap()
        {
            CoreServices.Registry.Register(new ContextGetHandler());
            CoreServices.Registry.Register(new SceneListOpenHandler());
            CoreServices.Registry.Register(new SceneHierarchyHandler());
            CoreServices.Registry.Register(new SceneObjectGetHandler());
        }
    }

    internal sealed class ContextGetHandler : CommandHandler<ContextQuery>
    {
        public ContextGetHandler() : base("context.get", false, "scene.read")
        {
        }

        protected override ActionResult<JObject> Execute(
            ContextQuery arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return ContextService.GetSnapshot(arguments);
        }
    }

    internal sealed class SceneListOpenHandler : CommandHandler<EmptyArguments>
    {
        public SceneListOpenHandler() : base("scene.list-open", false, "scene.read")
        {
        }

        protected override ActionResult<JObject> Execute(
            EmptyArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return Results.Ok(
                "Open scenes.",
                new JObject { ["scenes"] = ContextService.GetOpenScenesArray() });
        }
    }

    internal sealed class SceneHierarchyHandler : CommandHandler<HierarchyQuery>
    {
        public SceneHierarchyHandler() : base("scene.hierarchy.get", false, "scene.read")
        {
        }

        protected override ActionResult<JObject> Execute(
            HierarchyQuery arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return ContextService.GetHierarchy(arguments);
        }
    }

    internal sealed class SceneObjectGetHandler : CommandHandler<SceneObjectQuery>
    {
        public SceneObjectGetHandler() : base("scene.object.get", false, "scene.read")
        {
        }

        protected override ActionResult<JObject> Validate(
            SceneObjectQuery arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return arguments == null || arguments.Target == null
                ? Results.Error(ResultCodes.ValidationFailed, "target is required.")
                : Results.Ok("Object query is valid.");
        }

        protected override ActionResult<JObject> Execute(
            SceneObjectQuery arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return ContextService.GetObject(arguments);
        }
    }
}

