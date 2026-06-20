using Argus.Sync.EntityFramework.Postgres;
using Argus.Sync.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Verifies the Postgres single-instance lock enforces mutual exclusion across instances:
/// while one worker holds the lock a second parks at its gate, and only acquires once the
/// first releases. This is the guard that stops a redeploy overlap from running two indexers
/// against one database. Postgres only — no node required.
/// </summary>
public sealed class SingleInstanceLockTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private readonly ITestOutputHelper _output = output;
    private TestDatabaseManager? _db;

    public Task InitializeAsync()
    {
        _db = new TestDatabaseManager(_output);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
            _db = null;
        }
    }

    public void Dispose()
    {
        _db?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _db = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SecondInstance_ParksUntilFirstReleasesTheLock()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        FakeLifetime lifetime = new();

        using PostgresSingleInstanceLock first = CreateWorker(loggerFactory, lifetime);
        using PostgresSingleInstanceLock second = CreateWorker(loggerFactory, lifetime);

        // 1. First instance starts and acquires the (uncontended) lock.
        await first.StartAsync(CancellationToken.None);
        await first.WaitForAcquisitionAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        _output.WriteLine("First instance acquired the lock.");

        // 2. Second instance starts but must NOT acquire while the first holds it.
        await second.StartAsync(CancellationToken.None);
        Task secondGate = second.WaitForAcquisitionAsync(CancellationToken.None);
        Task settled = await Task.WhenAny(secondGate, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.NotSame(secondGate, settled);
        Assert.False(secondGate.IsCompleted, "second instance acquired the lock while the first still held it");
        _output.WriteLine("Second instance correctly parked at the gate.");

        // 3. First releases → second acquires within a poll cycle.
        await first.StopAsync(CancellationToken.None);
        _output.WriteLine("First instance released the lock.");
        await secondGate.WaitAsync(TimeSpan.FromSeconds(5));
        _output.WriteLine("Second instance acquired the lock after the first released.");

        await second.StopAsync(CancellationToken.None);
        Assert.False(lifetime.StopCalled, "the host should never be asked to stop in the happy path");
    }

    private PostgresSingleInstanceLock CreateWorker(ILoggerFactory loggerFactory, IHostApplicationLifetime lifetime)
    {
        // Both workers point at the same test database → same lock key → they contend.
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CardanoContext"] = _db!.DbContext.Database.GetConnectionString(),
            ["Sync:SingleInstanceLock:PollSeconds"] = "1",
            ["Sync:SingleInstanceLock:HealthCheckSeconds"] = "1",
        }).Build();

        return new PostgresSingleInstanceLock(
            config,
            loggerFactory.CreateLogger<PostgresSingleInstanceLock>(),
            lifetime);
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
