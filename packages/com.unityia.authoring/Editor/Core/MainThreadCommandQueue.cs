using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityIA.Contracts;

namespace UnityIA.Core
{
    [InitializeOnLoad]
    public static class MainThreadCommandQueue
    {
        private const int MaximumQueueLength = 64;
        private static readonly ConcurrentQueue<PendingCommand> Queue =
            new ConcurrentQueue<PendingCommand>();
        private static int queuedCount;

        static MainThreadCommandQueue()
        {
            EditorApplication.update += DrainOne;
        }

        public static Task<ActionResult<JObject>> Enqueue(
            Func<ActionResult<JObject>> operation)
        {
            if (operation == null)
            {
                return Task.FromResult(
                    Results.Error(ResultCodes.InvalidCommand, "No operation was provided."));
            }

            if (queuedCount >= MaximumQueueLength)
            {
                return Task.FromResult(
                    Results.Error(ResultCodes.EditorBusy, "The UnityIA command queue is full."));
            }

            TaskCompletionSource<ActionResult<JObject>> completion =
                new TaskCompletionSource<ActionResult<JObject>>();
            Queue.Enqueue(new PendingCommand(operation, completion));
            System.Threading.Interlocked.Increment(ref queuedCount);
            return completion.Task;
        }

        private static void DrainOne()
        {
            PendingCommand pending;
            if (!Queue.TryDequeue(out pending))
            {
                return;
            }

            System.Threading.Interlocked.Decrement(ref queuedCount);
            try
            {
                pending.Completion.SetResult(pending.Operation());
            }
            catch (Exception exception)
            {
                pending.Completion.SetResult(
                    Results.Error(
                        ResultCodes.InternalError,
                        "Unhandled main-thread command failure: " + exception.Message));
            }
        }

        private sealed class PendingCommand
        {
            public PendingCommand(
                Func<ActionResult<JObject>> operation,
                TaskCompletionSource<ActionResult<JObject>> completion)
            {
                Operation = operation;
                Completion = completion;
            }

            public Func<ActionResult<JObject>> Operation { get; }
            public TaskCompletionSource<ActionResult<JObject>> Completion { get; }
        }
    }
}

