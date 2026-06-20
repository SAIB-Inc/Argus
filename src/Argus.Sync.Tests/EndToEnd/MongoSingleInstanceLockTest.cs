using Argus.Sync.MongoDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Verifies the Mongo single-instance lease lock enforces mutual exclusion across instances: while one
/// worker holds (and renews) the lease a second parks at its gate, and only acquires once the first
/// releases. The Mongo analogue of <see cref="SingleInstanceLockTest"/> — the guard that stops a redeploy
/// overlap from running two indexers against one database. Requires a Mongo replica set on :27017 (skips
/// otherwise); no node required.
/// </summary>
public sealed class MongoSingleInstanceLockTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private const string MongoConnectionString = "mongodb://localhost:27017/?directConnection=true";

    private readonly ITestOutputHelper _output = output;
    private IMongoClient? _client;
    private string _dbName = string.Empty;

    public Task InitializeAsync()
    {
        _client = new MongoClient(MongoConnectionString);
        _dbName = $"argus_lock_{Guid.NewGuid():N}";
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client is not null && !string.IsNullOrEmpty(_dbName))
        {
            try { await _client.DropDatabaseAsync(_dbName); } catch { /* best-effort */ }
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SecondInstance_ParksUntilFirstReleasesTheLease()
    {
        if (!await IsMongoReachableAsync())
        {
            _output.WriteLine($"SKIP: Mongo not reachable at {MongoConnectionString}.");
            return;
        }

        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        FakeLifetime lifetime = new();

        using MongoSingleInstanceLock first = CreateWorker(loggerFactory, lifetime);
        using MongoSingleInstanceLock second = CreateWorker(loggerFactory, lifetime);

        // 1. First instance starts and acquires the (uncontended) lease.
        await first.StartAsync(CancellationToken.None);
        await first.WaitForAcquisitionAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        _output.WriteLine("First instance acquired the lease.");

        // 2. Second instance starts but must NOT acquire while the first holds (and renews) the lease.
        await second.StartAsync(CancellationToken.None);
        Task secondGate = second.WaitForAcquisitionAsync(CancellationToken.None);
        Task settled = await Task.WhenAny(secondGate, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.NotSame(secondGate, settled);
        Assert.False(secondGate.IsCompleted, "second instance acquired the lease while the first still held it");
        _output.WriteLine("Second instance correctly parked at the gate.");

        // 3. First releases (deletes its lease document) → second acquires on its next poll.
        await first.StopAsync(CancellationToken.None);
        _output.WriteLine("First instance released the lease.");
        await secondGate.WaitAsync(TimeSpan.FromSeconds(5));
        _output.WriteLine("Second instance acquired the lease after the first released.");

        await second.StopAsync(CancellationToken.None);
        Assert.False(lifetime.StopCalled, "the host should never be asked to stop in the happy path");
    }

    private MongoSingleInstanceLock CreateWorker(ILoggerFactory loggerFactory, IHostApplicationLifetime lifetime)
    {
        // Both workers share the database + scope → same lease document _id → they contend. A long lease
        // with a short renew keeps the holder's lease alive far beyond the park window, so the second can
        // only acquire via the first's explicit release (not lease expiry).
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:Database"] = _dbName,
            ["ConnectionStrings:CardanoContextSchema"] = "argus-lock-test",
            ["Sync:SingleInstanceLock:PollSeconds"] = "1",
            ["Sync:SingleInstanceLock:RenewSeconds"] = "1",
            ["Sync:SingleInstanceLock:LeaseSeconds"] = "30",
        }).Build();

        return new MongoSingleInstanceLock(
            _client!,
            config,
            loggerFactory.CreateLogger<MongoSingleInstanceLock>(),
            lifetime);
    }

    private async Task<bool> IsMongoReachableAsync()
    {
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
            using IAsyncCursor<string> names = await _client!.ListDatabaseNamesAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public bool StopCalled { get; private set; }
        public void StopApplication() => StopCalled = true;
    }
}
