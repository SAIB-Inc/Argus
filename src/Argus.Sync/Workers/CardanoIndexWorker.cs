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
        await InitializeStateAsync(stoppingToken);
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
            // Get the initial start intersection for the reducer
            string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
            ReducerRuntimeState reducerState = _reducerStates[reducerName];
            Point startIntersection = reducerState.StartIntersection();

            // Start the chain sync
            ICardanoChainProvider chainProvider = GetCardanoChainProvider();
            await foreach (NextResponse response in chainProvider.StartChainSyncAsync(startIntersection, stoppingToken))
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

        ulong currentSlot = response.Block.Slot() ?? 0UL;
        ulong currentBlockNumber = response.Block.Number() ?? 0UL;
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
        if (currentSlot == 0)
        {
            reducerState.IsRollingBack = false;
            return;
        }

        reducerState.IsRollingBack = true;

        // Once we're sure we can rollback, we can proceed executing the rollback function
        ulong rollbackSlot = response.RollBackType switch
        {
            RollBackType.Exclusive => (response.Block!.Slot() ?? 0UL) + 1UL,
            RollBackType.Inclusive => response.Block!.Slot() ?? 0UL,
            _ => 0
        };

        PreventMassRollback(currentSlot, rollbackSlot, reducerName, stoppingToken);

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
        reducerState.IsRollingBack = false;

        reducerStopwatch.Stop();

        Logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);
    }

    private async Task UpdateReducerStateAsync(string reducerName, ulong slot, string hash, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
        ReducerRuntimeState reducerState = _reducerStates[reducerName];

        ReducerState newState = new()
        {
            Name = reducerName,
            Slot = slot,
            Hash = hash
        };

        IQueryable<ReducerState> removeQuery = dbContext.ReducerStates
            .Where(rs => rs.Name == reducerName)
            .OrderByDescending(rs => rs.Slot)
            .Skip(reducerState.RollbackBuffer);

        dbContext.ReducerStates.Add(newState);
        dbContext.ReducerStates.RemoveRange(removeQuery);

        await dbContext.SaveChangesAsync(stoppingToken);
        await dbContext.DisposeAsync();
    }

    private async Task RemoveReducerStateAsync(string reducerName, ulong slot, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

        IQueryable<ReducerState> removeQuery = dbContext.ReducerStates
            .Where(rs => rs.Name == reducerName && rs.Slot >= slot);

        dbContext.RemoveRange(removeQuery);
        await dbContext.SaveChangesAsync(stoppingToken);
        await dbContext.DisposeAsync();
    }

    private async Task AwaitReducerDependenciesRollForwardAsync(ReducerRuntimeState reducerState, ulong currentSlot, CancellationToken stoppingToken)
    {
        while (true)
        {
            // Check for dependencies
            bool canRollForward = !reducerState.Dependencies.Any(e => _reducerStates[e].CurrentSlot < currentSlot || _reducerStates[e].IsRollingBack);

            // If this reducer can move forward, we break out of this loop
            if (canRollForward) break;

            // Otherwise we add a slight delay to recheck if the dependencies have moved forward
            Logger.LogInformation("Reducer {Reducer} is waiting for dependencies to move forward to {RollforwardSlot}", reducerState.Name, currentSlot);
            await Task.Delay(100, stoppingToken);
        }
    }

    private async Task AwaitReducerDependenciesRollbackAsync(ReducerRuntimeState reducerState, ulong rollbackSlot, CancellationToken stoppingToken)
    {
        // Check if the reducer has dependencies that needs to rollback first
        while (true)
        {
            // Let's check if anything depends on this reducer, that means we need them to finish rollback first
            bool isDependentsRollingBack = IsDependentsRollingBack(reducerState.Name, rollbackSlot);

            // If no dependents are rolling back, we can break out of this loop
            if (!isDependentsRollingBack) break;

            // Otherwise we wait
            Logger.LogInformation("Reducer {Reducer} is waiting for dependents to finish rollback to {RollbackSlot}", reducerState.Name, rollbackSlot);
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

    private Point GetConfiguredReducerIntersection(string reducerName)
    {
        // Get default start slot and hash from global configuration
        ulong defaultStartSlot = Configuration.GetValue<ulong?>("CardanoNodeConnection:Slot")
            ?? throw new InvalidOperationException("Default StartSlot is not specified in the configuration.");
        string defaultStartHash = Configuration.GetValue<string>("CardanoNodeConnection:Hash")
            ?? throw new InvalidOperationException("Default StartHash is not specified in the configuration.");

        // Get the configuration section for the specific reducer
        IConfigurationSection reducerSection = Configuration.GetSection($"CardanoIndexReducers:{reducerName}");

        // Retrieve the StartSlot and StartHash for the reducer, or use defaults if not specified
        ulong configStartSlot = reducerSection.GetValue<ulong?>("StartSlot") ?? defaultStartSlot;
        string configStartHash = reducerSection.GetValue<string>("StartHash") ?? defaultStartHash;

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

    private async Task InitializeStateAsync(CancellationToken stoppingToken)
    {
        // Get all the reducer names
        Dictionary<string, IReducer<IReducerModel>> reducerDict = Reducers
            .Select(e => new { Name = ArgusUtils.GetTypeNameWithoutGenerics(e.GetType()), Reducer = e })
            .ToDictionary(e => e.Name, e => e.Reducer);

        HashSet<string> reducerNames = [.. reducerDict.Keys];

        // Get all the reducer states from the database
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
        Dictionary<string, IEnumerable<Point>> latestStates = await dbContext.ReducerStates
            .Where(rs => reducerNames.Contains(rs.Name))
            .GroupBy(rs => rs.Name)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Select(e => new Point(e.Hash, e.Slot))
            );
        await dbContext.DisposeAsync();

        // Initialize the reducer states
        reducerNames.ToList().ForEach(e =>
        {
            List<string> dependencies = ReducerDependencyResolver.GetReducerDependencies(reducerDict[e].GetType())
                .Select(ArgusUtils.GetTypeNameWithoutGenerics)
                .ToList();
            List<Point> intersections = [];

            if (latestStates.TryGetValue(e, out IEnumerable<Point>? latestIntersections))
                intersections = latestIntersections.ToList();

            Point startIntersection = GetConfiguredReducerIntersection(e);

            ReducerRuntimeState reducerState = new()
            {
                Name = e,
                Dependencies = dependencies,
                RollbackBuffer = GetRollbackBuffer(e),
                InitialIntersection = startIntersection,
                IsRollingBack = true,
            };

            intersections.ForEach(reducerState.AddIntersection);

            _reducerStates.TryAdd(e, reducerState);
        });
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

    private int GetRollbackBuffer(string reducerName) =>
        Configuration.GetValue<int?>($"CardanoIndexReducers:{reducerName}:RollbackBuffer")
            ?? Configuration.GetValue("CardanoNodeConnection:RollbackBuffer", 10);

    private static IEnumerable<string> GetImmediateDependents(string reducerName)
    {
        // Check all reducers to see who directly depends on `reducerName`.
        return _reducerStates
            .Where(kv => kv.Value.Dependencies.Contains(reducerName))
            .Select(kv => kv.Key);
    }

    private static IEnumerable<string> GetAllDependents(string reducerName)
    {
        HashSet<string> visited = [];
        Stack<string> stack = new(GetImmediateDependents(reducerName));

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (visited.Add(current))
            {
                yield return current;

                // Push immediate dependents of the current node
                foreach (string dependent in GetImmediateDependents(current))
                {
                    if (!visited.Contains(dependent))
                        stack.Push(dependent);
                }
            }
        }
    }

    private static bool IsDependentsRollingBack(string reducerName, ulong rollbackSlot)
    {
        foreach (string dependentName in GetAllDependents(reducerName))
        {
            if (_reducerStates.TryGetValue(dependentName, out ReducerRuntimeState? dependentState))
            {
                if (dependentState.CurrentSlot > rollbackSlot || dependentState.IsRollingBack)
                {
                    return true;
                }
            }
        }

        return false;
    }

}