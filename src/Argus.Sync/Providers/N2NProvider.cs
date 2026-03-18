using Argus.Sync.Data.Models;

namespace Argus.Sync.Providers;

/// <summary>
/// Cardano chain provider using the Node-to-Node (N2N) native TCP protocol. Not yet implemented.
/// </summary>
public class N2NProvider : ICardanoChainProvider
{
    /// <inheritdoc />
    public Task<Point> GetTipAsync(ulong networkMagic = 2, CancellationToken? stoppingToken = null) => throw new NotImplementedException();

    /// <inheritdoc />
    public IAsyncEnumerable<NextResponse> StartChainSyncAsync(IEnumerable<Point> intersection, ulong networkMagic = 2, CancellationToken? stoppingToken = null) => throw new NotImplementedException();
}
