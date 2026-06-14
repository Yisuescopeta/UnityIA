using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    public sealed class CommandDescriptor
    {
        public CommandDescriptor(string name, bool isMutation, string capability)
        {
            Name = name;
            IsMutation = isMutation;
            Capability = capability ?? string.Empty;
        }

        public string Name { get; }
        public bool IsMutation { get; }
        public string Capability { get; }

        public CommandDescriptorDto ToDto()
        {
            return new CommandDescriptorDto
            {
                Name = Name,
                IsMutation = IsMutation,
                Capability = Capability,
                Version = EditorSession.ProtocolVersion
            };
        }
    }

    public sealed class CommandExecutionContext
    {
        internal CommandExecutionContext(CommandDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public CommandDescriptor Descriptor { get; }
        public string SessionId => EditorSession.SessionId;
        public long ContextVersion => EditorStateTracker.ContextVersion;
        public string ProjectRoot => ProjectPaths.ProjectRoot;
    }

    public interface ICommandHandler
    {
        CommandDescriptor Descriptor { get; }
        ActionResult<JObject> Validate(CommandEnvelope envelope, CommandExecutionContext context);
        ActionResult<JObject> Execute(CommandEnvelope envelope, CommandExecutionContext context);
    }

    public abstract class CommandHandler<TArguments> : ICommandHandler
    {
        protected CommandHandler(string name, bool isMutation, string capability)
        {
            Descriptor = new CommandDescriptor(name, isMutation, capability);
        }

        public CommandDescriptor Descriptor { get; }

        public ActionResult<JObject> Validate(
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            TArguments arguments;
            string error;
            if (!CommandJson.TryConvertArguments(envelope.Arguments, out arguments, out error))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "Invalid arguments for " + Descriptor.Name + ": " + error);
            }

            return Validate(arguments, envelope, context);
        }

        public ActionResult<JObject> Execute(
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            TArguments arguments;
            string error;
            if (!CommandJson.TryConvertArguments(envelope.Arguments, out arguments, out error))
            {
                return Results.Error(
                    ResultCodes.ValidationFailed,
                    "Invalid arguments for " + Descriptor.Name + ": " + error);
            }

            return Execute(arguments, envelope, context);
        }

        protected virtual ActionResult<JObject> Validate(
            TArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context)
        {
            return Results.Ok("Command arguments are valid.");
        }

        protected abstract ActionResult<JObject> Execute(
            TArguments arguments,
            CommandEnvelope envelope,
            CommandExecutionContext context);
    }

    public sealed class CommandRegistry
    {
        private readonly Dictionary<string, ICommandHandler> handlers =
            new Dictionary<string, ICommandHandler>(StringComparer.Ordinal);

        public void Register(ICommandHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (handlers.ContainsKey(handler.Descriptor.Name))
            {
                throw new InvalidOperationException(
                    "A handler is already registered for " + handler.Descriptor.Name + ".");
            }

            handlers.Add(handler.Descriptor.Name, handler);
        }

        public bool TryGet(string name, out ICommandHandler handler)
        {
            return handlers.TryGetValue(name ?? string.Empty, out handler);
        }

        public IReadOnlyList<CommandDescriptorDto> List()
        {
            return handlers.Values
                .Select(handler => handler.Descriptor.ToDto())
                .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
                .ToArray();
        }
    }
}

