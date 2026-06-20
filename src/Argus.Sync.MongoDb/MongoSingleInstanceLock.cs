using Argus.Sync.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Argus.Sync.MongoDb;

/// <summary>
/// <see cref="BackgroundService"/> that holds a MongoDB <em>lease lock</em> for the process's lifetime,
/// guaranteeing a single active Argus indexer per database+scope — the Mongo analogue of the Postgres
/// advisory-lock worker. A second instance (e.g. an overlapping deployment) parks at the gate until the
/// lease expires or is released, instead of running concurrently and corrupting state/data.
/// </summary>
/// <remarks>
/// <para>Mongo has no session-scoped server lock like Postgres' advisory lock, so exclusivity is a
/// time-bounded <em>lease</em>: a single document in the <c>ReducerLocks</c> collection holds the current
/// owner and an expiry instant. Acquisition is an atomic upsert that only succeeds when the lease is absent
/// or expired; the owner then renews the expiry on a timer well inside the lease window. If a renewal fails
/// (the lease lapsed and was taken, or Mongo is unreachable) the worker stops the whole host
/// (<see cref="IHostApplicationLifetime.StopApplication"/>) rather than keep writing unprotected.</para>
/// <para>The lease is keyed by a scope string so separate indexers sharing a database (different scopes) do
/// not block each other; same scope ⇒ same lease ⇒ mutual exclusion.</para>
/// </remarks>
public sealed partial class MongoSingleInstanceLock : BackgroundService, ISingleInstanceLock
{
    private readonly IMongoCollection<LockDocument> _locks;
    private readonly string _scope;
    private readonly string _holderId = Guid.NewGuid().ToString("N");
    private readonly TimeSpan _lease;
    private readonly TimeSpan _renewInterval;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<MongoSingleInstanceLock> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TaskCompletionSource _acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Creates the lock worker from configuration (Mongo client, database, scope, intervals).</summary>
    public MongoSingleInstanceLock(
        IMongoClient client,
        IConfiguration configuration,
        ILogger<MongoSingleInstanceLock> logger,
        IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger;
        _lifetime = lifetime;
        string databaseName = configuration["Mongo:Database"] ?? "argus";
        _locks = client.GetDatabase(databaseName).GetCollection<LockDocument>("ReducerLocks");
        // Scope the lease so separate indexers sharing a database (different scopes) don't block each other.
        _scope = configuration.GetConnectionString("CardanoContextSchema") ?? "argus";
        // Lease must exceed the renew interval so a healthy owner always renews before expiry.
        _lease = TimeSpan.FromSeconds(Math.Max(2, configuration.GetValue("Sync:SingleInstanceLock:LeaseSeconds", 30)));
        _renewInterval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Sync:SingleInstanceLock:RenewSeconds", 10)));
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Sync:SingleInstanceLock:PollSeconds", 2)));
    }

    /// <inheritdoc />
    public Task WaitForAcquisitionAsync(CancellationToken ct) => _acquired.Task.WaitAsync(ct);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 1. Acquire — poll the atomic upsert so a deploy overlap surfaces in logs and stays responsive
            //    to shutdown, instead of blocking until the holder releases.
            while (!await TryAcquireAsync(stoppingToken).ConfigureAwait(false))
            {
                LogWaiting(_logger, _scope);
                await Task.Delay(_pollInterval, stoppingToken).ConfigureAwait(false);
            }

            LogAcquired(_logger, _scope);
            _ = _acquired.TrySetResult();

            // 2. Hold + renew — extend the lease well inside its window. If a renewal fails the lease is gone
            //    and another instance could take over, so stop the host rather than run unprotected.
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_renewInterval, stoppingToken).ConfigureAwait(false);
                if (!await RenewAsync(stoppingToken).ConfigureAwait(false))
                {
                    LogLost(_logger, _scope);
                    _lifetime.StopApplication();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown before or after acquisition.
            _ = _acquired.TrySetCanceled(stoppingToken);
        }
        catch (Exception ex)
        {
            LogFailed(_logger, ex, _scope);
            _ = _acquired.TrySetException(ex);
            _lifetime.StopApplication();
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop the run loop first, then release the lease if we still own it (otherwise it expires on its own).
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            FilterDefinition<LockDocument> mine = Builders<LockDocument>.Filter.And(
                Builders<LockDocument>.Filter.Eq(x => x.Id, _scope),
                Builders<LockDocument>.Filter.Eq(x => x.Holder, _holderId));
            _ = await _locks.DeleteOneAsync(mine, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The lease expires on its own if release fails.
        }
    }

    private async Task<bool> TryAcquireAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        // Match only a free/expired lease for this scope; the upsert creates it when absent. An existing,
        // unexpired lease held by another instance fails the filter, so the upsert hits the duplicate _id.
        FilterDefinition<LockDocument> free = Builders<LockDocument>.Filter.And(
            Builders<LockDocument>.Filter.Eq(x => x.Id, _scope),
            Builders<LockDocument>.Filter.Lt(x => x.ExpiresAt, now));
        UpdateDefinition<LockDocument> take = Builders<LockDocument>.Update
            .SetOnInsert(x => x.Id, _scope)
            .Set(x => x.Holder, _holderId)
            .Set(x => x.ExpiresAt, now + _lease);
        try
        {
            _ = await _locks.FindOneAndUpdateAsync(
                free,
                take,
                new FindOneAndUpdateOptions<LockDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
                ct).ConfigureAwait(false);
            return true;
        }
        catch (MongoException ex) when (IsDuplicateKey(ex))
        {
            // Another instance holds an unexpired lease.
            return false;
        }
    }

    private async Task<bool> RenewAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        FilterDefinition<LockDocument> mine = Builders<LockDocument>.Filter.And(
            Builders<LockDocument>.Filter.Eq(x => x.Id, _scope),
            Builders<LockDocument>.Filter.Eq(x => x.Holder, _holderId));
        UpdateResult result = await _locks.UpdateOneAsync(
            mine,
            Builders<LockDocument>.Update.Set(x => x.ExpiresAt, now + _lease),
            cancellationToken: ct).ConfigureAwait(false);
        return result.MatchedCount > 0;
    }

    private static bool IsDuplicateKey(MongoException ex) => ex switch
    {
        MongoCommandException command => command.Code == 11000,
        MongoWriteException write => write.WriteError?.Category == ServerErrorCategory.DuplicateKey,
        _ => false,
    };

    /// <summary>The lease document: one per scope, naming the current holder and the expiry instant.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the MongoDB driver via reflection when reading the lease document.")]
    internal sealed class LockDocument
    {
        [BsonId]
        public string Id { get; set; } = default!;
        public string Holder { get; set; } = default!;
        public DateTime ExpiresAt { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Another Argus instance holds the Mongo single-instance lease ({Scope}); waiting…")]
    private static partial void LogWaiting(ILogger logger, string scope);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mongo single-instance lease acquired ({Scope}); starting indexer.")]
    private static partial void LogAcquired(ILogger logger, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lost the Mongo single-instance lease ({Scope}); stopping host to avoid two active writers.")]
    private static partial void LogLost(ILogger logger, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Mongo single-instance lease acquisition failed ({Scope}); stopping host.")]
    private static partial void LogFailed(ILogger logger, Exception ex, string scope);
}
