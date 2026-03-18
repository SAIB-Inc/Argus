using Argus.Sync.Data.Models;
using Point = Argus.Sync.Data.Models.Point;
namespace Argus.Sync.Providers;

/// <summary>
/// Interface for Cardano chain providers that supply blockchain synchronization and tip query capabilities.
/// </summary>
public interface ICardanoChainProvider
{
    /// <summary>
    /// Starts chain synchronization from the given intersection points, yielding block responses as they arrive.
    /// </summary>
    /// <param name="intersection">The intersection points to sync from.</param>
    /// <param name="networkMagic">The network magic number identifying the Cardano network.</param>
    /// <param name="stoppingToken">An optional cancellation token to stop synchronization.</param>
    /// <returns>An async enumerable of chain sync responses.</returns>
    IAsyncEnumerable<NextResponse> StartChainSyncAsync(IEnumerable<Point> intersection, ulong networkMagic = 2, CancellationToken? stoppingToken = null);

    /// <summary>
    /// Gets the current tip of the blockchain.
    /// </summary>
    /// <param name="networkMagic">The network magic number identifying the Cardano network.</param>
    /// <param name="stoppingToken">An optional cancellation token.</param>
    /// <returns>The current chain tip as a <see cref="Point"/>.</returns>
    Task<Point> GetTipAsync(ulong networkMagic = 2, CancellationToken? stoppingToken = null);
}
