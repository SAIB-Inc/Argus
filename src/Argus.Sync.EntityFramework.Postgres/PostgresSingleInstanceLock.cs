using System.Data;
using System.Text;
using Argus.Sync.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Argus.Sync.EntityFramework.Postgres;

/// <summary>
/// <see cref="BackgroundService"/> that holds a Postgres <em>session-level advisory lock</em>
/// for the process's lifetime, guaranteeing a single active Argus indexer per database. A
/// second instance — e.g. a new deployment overlapping the old one — parks at the gate until
/// the first releases the lock, instead of running concurrently and corrupting state/data.
/// </summary>
/// <remarks>
/// <para>The lock is keyed by a process-stable hash of the database schema, so distinct
/// schemas in one database do not block each other. Acquisition polls the non-blocking
/// <c>pg_try_advisory_lock</c> so the wait is observable in logs and responsive to shutdown.</para>
/// <para>After acquiring, the worker holds a dedicated connection open and periodically probes
/// it; advisory locks are released automatically when their owning backend dies, so if the lock
/// connection drops another instance could take over — the worker therefore stops the whole host
/// (<see cref="IHostApplicationLifetime.StopApplication"/>) rather than keep running unprotected.</para>
/// <para>Requires a session-pinned connection: behind PgBouncer use session-pooling mode, not
/// transaction-pooling, or the session advisory lock will not persist across statements.</para>
/// </remarks>
public sealed partial class PostgresSingleInstanceLock : BackgroundService, ISingleInstanceLock
{
    private readonly string _connectionString;
    private readonly long _key;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _healthInterval;
    private readonly ILogger<PostgresSingleInstanceLock> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TaskCompletionSource _acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NpgsqlConnection? _connection;

    /// <summary>Creates the lock worker from configuration (connection string, schema, intervals).</summary>
    public PostgresSingleInstanceLock(
        IConfiguration configuration,
        ILogger<PostgresSingleInstanceLock> logger,
        IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger;
        _lifetime = lifetime;
        string baseConnectionString = configuration.GetConnectionString("CardanoContext")
            ?? throw new InvalidOperationException("ConnectionStrings:CardanoContext is required for the single-instance lock.");
        // Dedicated, unpooled connection: the advisory lock lives and dies with this exact
        // backend, so closing it deterministically releases the lock (no reliance on pool
        // reset), and the named connection is identifiable in pg_stat_activity.
        _connectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = false,
            ApplicationName = "argus-single-instance-lock",
        }.ConnectionString;
        // Scope the lock to the schema so separate indexers sharing a database (different
        // schemas) don't block each other; same schema => same lock => mutual exclusion.
        string scope = configuration.GetConnectionString("CardanoContextSchema") ?? "argus";
        _key = StableKey(scope);
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Sync:SingleInstanceLock:PollSeconds", 2)));
        _healthInterval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Sync:SingleInstanceLock:HealthCheckSeconds", 5)));
    }

    /// <inheritdoc />
    public Task WaitForAcquisitionAsync(CancellationToken ct) => _acquired.Task.WaitAsync(ct);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _connection = new NpgsqlConnection(_connectionString);
            await _connection.OpenAsync(stoppingToken).ConfigureAwait(false);

            // 1. Acquire — poll the non-blocking try-lock so a deploy overlap surfaces in logs
            //    and stays responsive to shutdown, instead of sleeping in the server's lock queue.
            while (!await TryAcquireAsync(_connection, _key, stoppingToken).ConfigureAwait(false))
            {
                LogWaiting(_logger, _key);
                await Task.Delay(_pollInterval, stoppingToken).ConfigureAwait(false);
            }

            LogAcquired(_logger, _key);
            _ = _acquired.TrySetResult();

            // 2. Hold + monitor — if the lock connection dies the lock is gone and another
            //    instance could take over, so stop the host rather than run unprotected.
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_healthInterval, stoppingToken).ConfigureAwait(false);
                if (!await IsConnectionHealthyAsync(_connection, stoppingToken).ConfigureAwait(false))
                {
                    LogLost(_logger, _key);
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
            LogFailed(_logger, ex, _key);
            _ = _acquired.TrySetException(ex);
            _lifetime.StopApplication();
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop the run loop first, then release the lock and close the dedicated connection.
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_connection is { State: ConnectionState.Open })
        {
            try
            {
                await using NpgsqlCommand cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT pg_advisory_unlock(@k)";
                _ = cmd.Parameters.AddWithValue("k", _key);
                _ = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Closing the connection releases the session lock regardless.
            }
            await _connection.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _connection?.Dispose();
        base.Dispose();
    }

    private static async Task<bool> TryAcquireAsync(NpgsqlConnection conn, long key, CancellationToken ct)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock(@k)";
        _ = cmd.Parameters.AddWithValue("k", key);
        object? result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is true;
    }

    private static async Task<bool> IsConnectionHealthyAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (conn.State != ConnectionState.Open)
        {
            return false;
        }
        try
        {
            await using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            _ = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Deterministic 64-bit key (FNV-1a) over the scope string. Must be process-stable:
    /// <see cref="string.GetHashCode()"/> is randomized per process, so two instances would
    /// derive different keys and never contend.
    /// </summary>
    private static long StableKey(string scope)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (byte b in Encoding.UTF8.GetBytes(scope))
        {
            hash ^= b;
            hash *= prime;
        }
        return unchecked((long)hash);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Another Argus instance holds the single-instance lock ({Key}); waiting…")]
    private static partial void LogWaiting(ILogger logger, long key);

    [LoggerMessage(Level = LogLevel.Information, Message = "Single-instance lock acquired ({Key}); starting indexer.")]
    private static partial void LogAcquired(ILogger logger, long key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lost the single-instance lock connection ({Key}); stopping host to avoid two active writers.")]
    private static partial void LogLost(ILogger logger, long key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Single-instance lock acquisition failed ({Key}); stopping host.")]
    private static partial void LogFailed(ILogger logger, Exception ex, long key);
}
