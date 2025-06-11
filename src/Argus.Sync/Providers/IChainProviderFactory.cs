namespace Argus.Sync.Providers;

/// <summary>
/// Factory interface for creating ICardanoChainProvider instances.
/// Enables dependency injection of different provider creation strategies
/// for production (configuration-based) and testing (mock provider) scenarios.
/// </summary>
public interface IChainProviderFactory
{
    /// <summary>
    /// Creates an ICardanoChainProvider instance based on the factory's configuration.
    /// </summary>
    /// <returns>A configured ICardanoChainProvider instance</returns>
    ICardanoChainProvider CreateProvider();
}