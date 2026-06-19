using Argus.Sync.Extensions;

namespace Argus.Sync.Tests.Unit;

/// <summary>
/// Pins the <c>dotnet ef</c> design-time detection in <see cref="ReducerExtensions"/> — the guard that
/// skips reducer registration during migrations. The detection was narrowed to the <b>entry</b> assembly
/// named exactly "ef" (case-sensitive): previously it scanned every loaded assembly, so a real application
/// that referenced — or was itself named — "ef" would silently register zero reducers and the indexer would
/// start but do nothing. Now only the actual EF CLI tool (whose entry assembly is "ef") matches.
/// </summary>
public sealed class EfDesignTimeDetectionTest
{
    [Theory]
    [InlineData("ef", true)]        // the `dotnet ef` tool's entry assembly
    [InlineData("EF", false)]       // case-sensitive — a project named "EF" is not the tool
    [InlineData("ef.tool", false)]  // must be an exact match
    [InlineData("MyIndexer", false)]
    [InlineData("EfConsumer", false)]
    [InlineData(null, false)]       // no managed entry assembly → register normally
    public void IsEfDesignTime_MatchesOnlyTheExactEfToolEntryAssembly(string? entryAssemblyName, bool expected)
        => Assert.Equal(expected, ReducerExtensions.IsEfDesignTime(entryAssemblyName));
}
