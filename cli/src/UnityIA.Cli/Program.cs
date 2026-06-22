using UnityIA.Protocol;

namespace UnityIA.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Console.Out.WriteLine(
                ResultWriter.Error("INTERNAL_ERROR", "CLI failure: " + exception.Message));
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        return await CliRunner.CreateDefault(Console.Out, Console.Error)
            .RunAsync(args)
            .ConfigureAwait(false);
    }
}
