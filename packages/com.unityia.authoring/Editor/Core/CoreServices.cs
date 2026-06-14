using UnityEditor;

namespace UnityIA.Core
{
    [InitializeOnLoad]
    public static class CoreServices
    {
        static CoreServices()
        {
            Registry = new CommandRegistry();
            Permissions = new PermissionService();
            Audit = new AuditService();
            Dispatcher = new CommandDispatcher(Registry, Permissions, Audit);

            Registry.Register(new SystemStatusHandler());
            Registry.Register(new CommandsListHandler());
            Registry.Register(new ValidateCommandHandler());
            Registry.Register(new ExplainPermissionHandler());
        }

        public static CommandRegistry Registry { get; }
        public static PermissionService Permissions { get; }
        public static AuditService Audit { get; }
        public static CommandDispatcher Dispatcher { get; }
    }
}

