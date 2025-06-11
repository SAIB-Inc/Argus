using Argus.Sync.Providers;

namespace Argus.Sync.Tests.Mocks;

/// <summary>
/// Test implementation of IChainProviderFactory that creates separate MockChainSyncProvider instances.
/// Each call to CreateProvider() returns a new provider instance for separate reducer synchronization.
/// Collects all created providers so the test can trigger events on all instances simultaneously.
/// </summary>
public class MockChainProviderFactory(string testDataDirectory) : IChainProviderFactory
{
    private readonly List<MockChainSyncProvider> _createdProviders = new();

    public ICardanoChainProvider CreateProvider()
    {
        var provider = new MockChainSyncProvider(testDataDirectory);
        _createdProviders.Add(provider);
        return provider;
    }

    /// <summary>
    /// Gets all provider instances created by this factory for test control.
    /// </summary>
    public IReadOnlyList<MockChainSyncProvider> CreatedProviders => _createdProviders.AsReadOnly();
}