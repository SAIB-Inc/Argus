using Argus.Sync.Bench.Pipelines;
using BenchmarkDotNet.Running;

namespace Argus.Sync.Bench;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length > 0 && args[0] == "smoke")
        {
            await SmokeTest.RunAsync().ConfigureAwait(false);
            return 0;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
