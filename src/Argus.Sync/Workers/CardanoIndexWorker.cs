using System.Collections.Concurrent;
using System.Diagnostics;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cardano.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Argus.Sync.Workers;

public class CriticalNodeException(string message) : Exception(message) { }

public class CardanoIndexWorker<T>(
    IConfiguration Configuration,
    ILogger<CardanoIndexWorker<T>> Logger,
    IDbContextFactory<T> DbContextFactory,
    IEnumerable<IReducer<IReducerModel>> Reducers
) : BackgroundService where T : CardanoDbContext
{
    private static readonly ConcurrentDictionary<string, ReducerRuntimeState> _reducerStates = [];
    private readonly ulong _maxRollbackSlots = Configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000UL);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Execute reducers
        await Task.WhenAny(
            Reducers.Select(
                reducer => StartReducerChainSyncAsync(reducer, stoppingToken)
            )
        );

        Environment.Exit(1);
    }

    private async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        NextResponse? currentResponse = null;
        try
        {
            ICardanoChainProvider chainProvider = GetCardanoChainProvider();

            IEnumerable<Point> intersections = await GetReducerStartPoint(reducer, stoppingToken);

            List<string> dependencies = [.. ReducerDependencyResolver.GetReducerDependencies(reducer.GetType()).Select(ArgusUtils.GetTypeNameWithoutGenerics)];
            Point startPoint = intersections.First();
            string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());

            _reducerStates.AddOrUpdate(reducerName, new ReducerRuntimeState
            {
                Name = reducerName,
                Dependencies = dependencies,
                Intersections = intersections.ToList(),
                RollbackBuffer = GetRollbackBuffer(reducerName)
            }, (k, v) => v);

            await foreach (NextResponse response in chainProvider.StartChainSyncAsync(startPoint, stoppingToken))
            {
                currentResponse = response;  // Store the current response
                Task responseTask = response.Action switch
                {
                    NextResponseAction.RollForward => ProcessRollForwardAsync(response, reducer, stoppingToken),
                    NextResponseAction.RollBack => ProcessRollBackAsync(response, reducer, stoppingToken),
                    _ => throw new CriticalNodeException($"Next response error received. {response}"),
                };

                await responseTask;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Something went wrong. Block: {BlockHash} Slot: {Slot}",
                currentResponse?.Block.Hash(), currentResponse?.Block.Slot()
            );
            throw new CriticalNodeException($"Critical Error, Aborting");
        }
    }

    private async Task ProcessRollForwardAsync(NextResponse response, IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        ulong currentSlot = response.Block.Slot();
        ulong currentBlockNumber = response.Block.Number();
        string currentBlockHash = response.Block.Hash();
        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
        ReducerRuntimeState reducerState = _reducerStates[reducerName];

        // Let's check if the reducer can move forward
        await AwaitReducerDependenciesRollForwardAsync(reducerState, currentSlot, stoppingToken);

        // Log the new chain event rollforward
        Logger.LogInformation("[{Reducer}]: New Chain Event RollForward: Slot {Slot} Block: {Block}", reducerName, currentSlot, currentBlockNumber);

        Stopwatch reducerStopwatch = Stopwatch.StartNew();

        // Run the reducer's rollforward logic
        await reducer.RollForwardAsync(response.Block);

        // Update database state
        await UpdateReducerStateAsync(reducerName, currentSlot, currentBlockHash, stoppingToken);

        // Tag as standby again after processing rollforward
        reducerState.AddIntersection(new Point(currentBlockHash, currentSlot));

        // Stop the timer
        reducerStopwatch.Stop();

        // Log the time taken to process the rollforward
        Logger.LogInformation("Processed RollForwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);

        stopwatch.Stop();

        Logger.LogInformation(
            "[{Reducer}]: Processed Chain Event RollForward: Slot {Slot} Block: {Block} in {ElapsedMilliseconds} ms, Mem: {MemoryUsage} MB",
            reducerName,
            currentSlot,
            currentBlockNumber,
            stopwatch.ElapsedMilliseconds,
            Math.Round(GetCurrentMemoryUsageInMB(), 2)
        );
    }

    private async Task ProcessRollBackAsync(NextResponse response, IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
        ReducerRuntimeState reducerState = _reducerStates[reducerName];

        ulong currentSlot = reducerState.CurrentSlot;

        // if it's zero, we do not need to rollback
        if (currentSlot == 0) return;

        // Once we're sure we can rollback, we can proceed executing the rollback function
        ulong rollbackSlot = response.RollBackType switch
        {
            RollBackType.Exclusive => response.Block!.Slot() + 1,
            RollBackType.Inclusive => response.Block!.Slot(),
            _ => 0
        };

        PreventMassRollback(currentSlot, rollbackSlot, reducerName, stoppingToken);

        List<Point> recentIntersections = reducerState.Intersections;

        // Wait for dependencies to rollback
        bool hasDependents = _reducerStates
            .Select(e => e.Value.Dependencies)
            .Any(reducerState.HasDependents);

        if (hasDependents)
            reducerState.RemoveIntersections(rollbackSlot);

        await AwaitReducerDependenciesRollbackAsync(reducerState, rollbackSlot, stoppingToken);

        Stopwatch reducerStopwatch = new();
        reducerStopwatch.Start();

        Logger.LogInformation("[{Reducer}]: New Chain Event RollBack: Slot {Slot}", reducerName, rollbackSlot);

        // Run the reducer's rollback logic
        await reducer.RollBackwardAsync(rollbackSlot);

        // Update database state
        await RemoveReducerStateAsync(reducerName, rollbackSlot, stoppingToken);

        // Update local state to match database
        reducerState.RemoveIntersections(rollbackSlot);

        reducerStopwatch.Stop();

        Logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);
    }

    private async Task UpdateReducerStateAsync(string reducerName, ulong slot, string hash, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
        ReducerRuntimeState reducerState = _reducerStates[reducerName];

        ReducerState? stateExisting = await dbContext.ReducerStates
            .FirstOrDefaultAsync(rs => rs.Name == reducerName && rs.Slot == slot, stoppingToken);

        if (stateExisting == null)
        {
            ReducerState newState = new()
            {
                Name = reducerName,
                Slot = slot,
                Hash = hash
            };
            dbContext.ReducerStates.Add(newState);
        }

        List<ReducerState> existingStates = await dbContext.ReducerStates
            .Where(rs => rs.Name == reducerName)
            .OrderByDescending(rs => rs.Slot)
            .ToListAsync(stoppingToken);

        int rollbackBuffer = reducerState.RollbackBuffer;
        if (existingStates.Count >= rollbackBuffer)
        {
            IEnumerable<ReducerState> statesToRemove = existingStates.Skip(rollbackBuffer);
            dbContext.ReducerStates.RemoveRange(statesToRemove);
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private async Task RemoveReducerStateAsync(string reducerName, ulong slot, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

        IEnumerable<ReducerState> states = await dbContext.ReducerStates
            .AsNoTracking()
            .Where(rs => rs.Name == reducerName && rs.Slot >= slot)
            .ToListAsync(stoppingToken);

        if (states.Any())
        {
            dbContext.ReducerStates.RemoveRange(states);
            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }

    private async Task AwaitReducerDependenciesRollForwardAsync(ReducerRuntimeState reducerState, ulong currentSlot, CancellationToken stoppingToken)
    {
        while (true)
        {
            // Check for dependencies
            bool canRollForward = !reducerState.Dependencies.Any(e => _reducerStates[e].CurrentSlot < currentSlot);

            // If this reducer can move forward, we break out of this loop
            if (canRollForward) break;

            // Otherwise we add a slight delay to recheck if the dependencies have moved forward
            Logger.LogInformation("Reducer {Reducer} is waiting for dependencies to move forward", reducerState.Name);
            await Task.Delay(100, stoppingToken);
        }
    }

    private async Task AwaitReducerDependenciesRollbackAsync(ReducerRuntimeState reducerState, ulong rollbackSlot, CancellationToken stoppingToken)
    {
        // Check if the reducer has dependencies that needs to rollback first
        while (true)
        {
            // Let's check if anything depends on this reducer, that means we need them to finish rollback first
            // @TODO: recheck logic
            bool isDependentsRollingBack = _reducerStates
                .Select(e => e)
                .Where(e => reducerState.HasDependents(e.Value.Dependencies))
                .Any(e => e.Value.CurrentSlot > rollbackSlot);

            // If no dependents are rolling back, we can break out of this loop
            if (!isDependentsRollingBack) break;

            // Otherwise we wait
            Logger.LogInformation("Reducer {Reducer} is waiting for dependents to finish rollback", reducerState.Name);
            await Task.Delay(100, stoppingToken);
        }
    }

    private void PreventMassRollback(ulong currentSlot, ulong rollbackSlot, string reducerName, CancellationToken stoppingToken)
    {
        if (!CanRollback(currentSlot, rollbackSlot))
        {
            Logger.LogError(
                "PreventMassrollbackAsync[{Reducer}] Requested RollBack Slot {RequestedSlot} is more than {MaxRollback} slots behind current slot {CurrentSlot}.",
                reducerName,
                rollbackSlot,
                _maxRollbackSlots,
                currentSlot
            );

            // We need to force the app to crash, there is something wrong if we're rolling back
            // too far than expected
            throw new CriticalNodeException("Rollback, Critical Error, Aborting");
        }
    }

    private bool CanRollback(ulong currentSlot, ulong rollbackSlot)
    {
        long slotDifference = (long)currentSlot - (long)rollbackSlot;
        return currentSlot == 0 || rollbackSlot > currentSlot || slotDifference <= (long)_maxRollbackSlots;
    }

    private async Task<IEnumerable<Point>> GetReducerStartPoint(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());

        // Get default start slot and hash from global configuration
        ulong defaultStartSlot = Configuration.GetValue<ulong?>("CardanoIndexStart:Slot")
            ?? throw new InvalidOperationException("Default StartSlot is not specified in the configuration.");
        string defaultStartHash = Configuration.GetValue<string>("CardanoIndexStart:Hash")
            ?? throw new InvalidOperationException("Default StartHash is not specified in the configuration.");

        // Get the configuration section for the specific reducer
        IConfigurationSection reducerSection = Configuration.GetSection($"CardanoIndexReducers:{reducerName}");

        // Retrieve the StartSlot and StartHash for the reducer, or use defaults if not specified
        ulong configStartSlot = reducerSection.GetValue<ulong?>("StartSlot") ?? defaultStartSlot;
        string configStartHash = reducerSection.GetValue<string>("StartHash") ?? defaultStartHash;

        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
        IEnumerable<ReducerState> latestState = await dbContext.ReducerStates
            .Where(rs => rs.Name == reducerName)
            .OrderByDescending(rs => rs.Slot)
            .ToListAsync();

        await dbContext.DisposeAsync();

        if (latestState.Any())
        {
            configStartSlot = latestState.First().Slot;
            configStartHash = latestState.First().Hash;
            return latestState.Select(rs => new Point(rs.Hash, rs.Slot));
        }
        else
        {
            return [new Point(configStartHash, configStartSlot)];
        }
    }

    private ICardanoChainProvider GetCardanoChainProvider()
    {
        IConfigurationSection config = Configuration.GetSection("CardanoNodeConnection");
        string connectionType = config.GetValue<string>("ConnectionType")
            ?? throw new InvalidOperationException("ConnectionType is not specified in the configuration.");
        ulong networkMagic = config.GetValue<ulong?>("NetworkMagic")
            ?? throw new InvalidOperationException("NetworkMagic is not specified in the configuration.");

        return connectionType switch
        {
            "UnixSocket" =>
                new N2CProvider(
                    networkMagic,
                    config.GetValue<string>("UnixSocket:Path")
                    ?? throw new InvalidOperationException("UnixSocket:Path is not specified in the configuration.")
                ),

            "TCP" =>
                throw new NotImplementedException("TCP connection type is not yet implemented."),

            "gRPC" =>
                new U5CProvider(
                    config.GetSection("gRPC").GetValue<string>("Endpoint")
                        ?? throw new InvalidOperationException("gRPC:Endpoint is not specified in the configuration."),
                    new Dictionary<string, string>
                    {
                        { "dmtr-api-key", config.GetSection("gRPC").GetValue<string>("ApiKey")
                            ?? throw new InvalidOperationException("gRPC:ApiKey is not specified in the configuration.") }
                    }
                ),
            _ => throw new InvalidOperationException("Invalid ConnectionType specified.")
        };
    }

    private static double GetCurrentMemoryUsageInMB()
    {
        Process currentProcess = Process.GetCurrentProcess();

        // Getting the physical memory usage of the current process in bytes
        long memoryUsed = currentProcess.WorkingSet64;

        // Convert to megabytes for easier reading
        double memoryUsedMb = memoryUsed / 1024.0 / 1024.0;

        return memoryUsedMb;
    }

    private int GetRollbackBuffer(string reducerName)
    {
        IConfigurationSection reducerSection = Configuration.GetSection($"CardanoIndexReducers:{reducerName}");
        return reducerSection.GetValue("RollbackBuffer", 10);
    }
}