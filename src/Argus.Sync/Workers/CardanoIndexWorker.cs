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
    private static readonly Dictionary<string, (ulong CurrentSlot, List<string> Dependencies, List<(ulong Slot, string Hash)> Points)> _reducerStates = [];
    private readonly ulong _maxRollbackSlots = Configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000UL);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PrepopulateReducerStatesAsync(stoppingToken);

        // Execute reducers
        await Task.WhenAny(
            Reducers.Select(
                reducer => StartReducerChainSyncAsync(reducer, stoppingToken)
            )
        );

        Environment.Exit(1);
    }

    private async Task PrepopulateReducerStatesAsync(CancellationToken stoppingToken)
    {
        // Pre-populate ReducerStates with registered reducers
        // and their dependencies
        Reducers.ToList().ForEach(reducer =>
        {
            string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
            List<string> dependencies = ReducerDependencyResolver.GetReducerDependencies(reducer.GetType())
                .Select(ArgusUtils.GetTypeNameWithoutGenerics)
                .ToList();
            _reducerStates.Add(reducerName, (0UL, dependencies, []));
        });

        // Get all registered reducers to fetch their currently saved slots
        // in the database
        IEnumerable<string> registeredReducers = _reducerStates.Select(e => e.Key);

        // Fetch current slots from database, we do this so we only need to fetch the slots once on sync start
        // the rest of the states we can update in-memory
        using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

        var allStates = await dbContext.ReducerStates.ToListAsync(stoppingToken);

        var lastRecordedReducerStates = allStates
            .Where(rs => registeredReducers.Contains(rs.Name))
            .GroupBy(rs => rs.Name)
            .ToDictionary(
                g => g.Key,
                g => (
                    g.Max(rs => rs.Slot),
                    g.OrderByDescending(rs => rs.Slot)
                        .Select(rs => (rs.Slot, rs.Hash))
                        .ToList()
                )
            );

        await dbContext.DisposeAsync();

        // Update ReducerStates with the current slots and intersection points
        _reducerStates.ToList().ForEach(reducerState =>
        {
            var (slot, points) = lastRecordedReducerStates.GetValueOrDefault(
                reducerState.Key,
                (0UL, [])
            );
            _reducerStates[reducerState.Key] = (slot, reducerState.Value.Dependencies, points);
        });
    }

    private async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        NextResponse? currentResponse = null;
        try
        {
            ICardanoChainProvider chainProvider = GetCardanoChainProvider();
            Point intersection = await GetReducerStartPoint(reducer, stoppingToken);

            await foreach (NextResponse response in chainProvider.StartChainSyncAsync(intersection, stoppingToken))
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
                "Something went wrong. Current response: {@Response} with cbor {@Cbor}",
                currentResponse?.Block.Hash(), Convert.ToHexString(currentResponse?.Block?.Raw!).ToLowerInvariant()
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

        // Let's check if the reducer can move forward
        await AwaitReducerDependenciesRollForwardAsync(reducerName, currentSlot, stoppingToken);

        // Log the new chain event rollforward
        Logger.LogInformation("[{Reducer}]: New Chain Event RollForward: Slot {Slot} Block: {Block}", reducerName, currentSlot, currentBlockNumber);

        // Execute the rollforward logic of this reducer
        Stopwatch reducerStopwatch = Stopwatch.StartNew();

        // Run the reducer's rollforward logic
        await reducer.RollForwardAsync(response.Block);

        var recentPoints = _reducerStates[reducerName].Points;
        recentPoints.Add((currentSlot, currentBlockHash));

        // Keeps the window size of the recent points
        int rollbackBuffer = GetRollbackBuffer(reducerName);
        if (recentPoints.Count > rollbackBuffer)
        {
            recentPoints.RemoveRange(0, recentPoints.Count - rollbackBuffer);
        }

        // Update database state
        await UpdateReducerStateAsync(reducerName, currentSlot, currentBlockHash, stoppingToken);

        // Stop the timer
        reducerStopwatch.Stop();

        // Log the time taken to process the rollforward
        Logger.LogInformation("Processed RollForwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);

        // Tag as standby again after processing rollforward
        _reducerStates[reducerName] = (currentSlot, _reducerStates[reducerName].Dependencies, recentPoints);

        // Stop the timer
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

        ulong currentSlot = await reducer.QueryTip() ?? 0UL;

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

        // Wait for dependencies to rollback
        await AwaitReducerDependenciesRollbackAsync(reducerName, rollbackSlot, stoppingToken);

        var recentPoints = _reducerStates[reducerName].Points;

        _reducerStates[reducerName] = (rollbackSlot, _reducerStates[reducerName].Dependencies, recentPoints);
        
        Stopwatch reducerStopwatch = new();
        reducerStopwatch.Start();

        Logger.LogInformation("[{Reducer}]: New Chain Event RollBack: Slot {Slot}", reducerName, rollbackSlot);
        await reducer.RollBackwardAsync(rollbackSlot);

        // Find the closest valid point from recent points
        var closestPoint = recentPoints
            .Where(p => p.Slot <= rollbackSlot)
            .OrderByDescending(p => p.Slot)
            .FirstOrDefault();

        // Update the recent points list to remove any points after the rollback
        recentPoints.RemoveAll(p => p.Slot > closestPoint.Slot);

        // Update database state
        await UpdateReducerStateAsync(reducerName, closestPoint.Slot, closestPoint.Hash, stoppingToken);

        reducerStopwatch.Stop();

        _reducerStates[reducerName] = (rollbackSlot, _reducerStates[reducerName].Dependencies, recentPoints);

        Logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);
    }

    private async Task UpdateReducerStateAsync(string reducerName, ulong slot, string hash, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

        int rollbackBuffer = GetRollbackBuffer(reducerName);

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

        if (existingStates.Count >= rollbackBuffer)
        {
            IEnumerable<ReducerState> statesToRemove = existingStates.Skip(rollbackBuffer - 1);
            dbContext.ReducerStates.RemoveRange(statesToRemove);
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private async Task AwaitReducerDependenciesRollForwardAsync(string reducerName, ulong currentSlot, CancellationToken stoppingToken)
    {
        while (true)
        {
            // Check for dependencies
            bool canRollForward = !_reducerStates[reducerName].Dependencies.Any(e => _reducerStates[e].CurrentSlot < currentSlot);

            // If this reducer can move forward, we break out of this loop
            if (canRollForward) break;

            // Otherwise we add a slight delay to recheck if the dependencies have moved forward
            Logger.LogInformation("Reducer {Reducer} is waiting for dependencies to move forward", reducerName);
            await Task.Delay(100, stoppingToken);
        }
    }

    private async Task AwaitReducerDependenciesRollbackAsync(string reducerName, ulong rollbackSlot, CancellationToken stoppingToken)
    {
        // Check if the reducer has dependencies that needs to rollback first
        while (true)
        {
            // Let's check if anything depends on this reducer, that means we need them to finish rollback first
            // @TODO: recheck logic
            bool isDependentsRollingBack = _reducerStates
                .Where(e => e.Value.Dependencies.Contains(reducerName))
                .Any(e => e.Value.CurrentSlot >= rollbackSlot);

            // If no dependents are rolling back, we can break out of this loop
            if (!isDependentsRollingBack) break;

            // Otherwise we wait
            Logger.LogInformation("Reducer {Reducer} is waiting for dependents to finish rollback", reducerName);
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

    private async Task<Point> GetReducerStartPoint(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
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
        ReducerState? latestState = await dbContext.ReducerStates
            .Where(rs => rs.Name == reducerName)
            .OrderByDescending(rs => rs.Slot)
            .FirstOrDefaultAsync(stoppingToken);

        if (latestState is not null)
        {
            configStartSlot = latestState.Slot;
            configStartHash = latestState.Hash;
        }

        await dbContext.DisposeAsync();
        return new Point(configStartHash, configStartSlot);
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
        return reducerSection.GetValue<int>("RollbackBuffer", 10);
    }
}