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
    private readonly ulong _maxRollbackSlots = Configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000UL);
    private static readonly ConcurrentDictionary<string, HashSet<string>> _dependencyGraph = new();
    private int RollbackBuffer => GetRollbackBuffer("default");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Create dependency graph
        CreateDependencyGraph(Reducers);

        HashSet<string> activeReducerNames = [.. GetActiveReducers()];
        IEnumerable<IReducer<IReducerModel>> reducersToRun = Reducers;
        if (activeReducerNames.Any())
        {
            reducersToRun = Reducers.Where(r =>
            {
                string name = ArgusUtils.GetTypeNameWithoutGenerics(r.GetType());
                return activeReducerNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            })
            .ToList();
        }

        if (reducersToRun.Any())
        {
            // Execute reducers
            await Task.WhenAny(
                reducersToRun.Select(
                    reducer => StartReducerChainSyncAsync(reducer, stoppingToken)
                )
            );
        }

        Environment.Exit(1);
    }

    private async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        // Get the initial start intersection for the reducer
        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
        Point? startIntersection = await GetIntersectionAsync(reducerName, 1, stoppingToken);

        if (startIntersection is null)
        {
            Logger.LogError("Failed to get the initial intersection for {Reducer}", reducerName);
            throw new CriticalNodeException("Critical Error, Aborting");
        }

        NextResponse? currentResponse = null;
        try
        {
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
            string action = currentResponse?.Action switch
            {
                NextResponseAction.RollForward => "RollForward",
                NextResponseAction.RollBack => "RollBack",
                _ => "Unknown"
            };

            Logger.LogError(
                ex,
                "[{Reducer}][{Action}] Something went wrong. Block: {BlockHash} Slot: {Slot}",
                reducerName, action, currentResponse?.Block.Hash(), currentResponse?.Block.Slot()
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

        // Let's check if the reducer can move forward
        await AwaitReducerDependenciesRollForwardAsync(reducerName, currentSlot, stoppingToken);

        // Log the new chain event rollforward
        Logger.LogInformation("[{Reducer}]: New Chain Event RollForward: Slot {Slot} Block: {Block}", reducerName, currentSlot, currentBlockNumber);

        Stopwatch reducerStopwatch = Stopwatch.StartNew();

        // Run the reducer's rollforward logic
        await reducer.RollForwardAsync(response.Block);

        // Update database state
        await UpdateReducerStateAsync(reducerName, currentSlot, currentBlockHash, stoppingToken);


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

        ulong currentSlot = await GetCurrentSlotAsync(reducerName, stoppingToken);

        // Once we're sure we can rollback, we can proceed executing the rollback function
        ulong rollbackSlot = response.RollBackType switch
        {
            RollBackType.Exclusive => (response.Block!.Slot() ?? 0UL) + 1UL,
            RollBackType.Inclusive => response.Block!.Slot() ?? 0UL,
            _ => 0
        };

        PreventMassRollback(currentSlot, rollbackSlot, reducerName, stoppingToken);

        await AwaitReducerDependenciesRollbackAsync(reducerName, rollbackSlot, stoppingToken);

        Stopwatch reducerStopwatch = new();
        reducerStopwatch.Start();

        Logger.LogInformation("[{Reducer}]: New Chain Event RollBack: Slot {Slot}", reducerName, rollbackSlot);

        // Run the reducer's rollback logic
        await reducer.RollBackwardAsync(rollbackSlot);

        // Update database state
        await RemoveReducerStateAsync(reducerName, rollbackSlot, stoppingToken);

        reducerStopwatch.Stop();

        Logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);
    }

    private async Task UpdateReducerStateAsync(string reducerName, ulong slot, string hash, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

        ReducerState newState = new()
        {
            Name = reducerName,
            Slot = slot,
            Hash = hash
        };

        dbContext.ReducerStates.Add(newState);

        // Remove old entries, keeping only the newest 10
        IQueryable<ReducerState> oldStates = dbContext.ReducerStates
            .AsNoTracking()
            .Where(rs => rs.Name == reducerName)
            .OrderByDescending(rs => rs.Slot)
            .Skip(RollbackBuffer);

        dbContext.ReducerStates.RemoveRange(oldStates);

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private async Task RemoveReducerStateAsync(string reducerName, ulong slot, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

        IQueryable<ReducerState> removeQuery = dbContext.ReducerStates
            .AsNoTracking()
            .Where(rs => rs.Name == reducerName && rs.Slot >= slot);

        dbContext.RemoveRange(removeQuery);
        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private async Task AwaitReducerDependenciesRollForwardAsync(string reducerName, ulong currentSlot, CancellationToken stoppingToken)
    {
        while (true)
        {
            // Check for dependencies
            IEnumerable<string> dependencies = GetReducerDependencies(reducerName);

            if (!dependencies.Any()) break;

            // Get the latest slot for each dependency
            using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
            var dependencyStates = await dbContext.ReducerStates
                .AsNoTracking()
                .GroupBy(rs => rs.Name)
                .Select(g => new { Name = g.Key, MaxSlot = g.Max(x => x.Slot) })
                .ToListAsync(stoppingToken);

            bool hasLaggingDependencies = dependencyStates
                .Where(x => dependencies.Contains(x.Name))
                .Select(x => x.MaxSlot)
                .Union(dependencies
                    .Except(dependencyStates.Select(x => x.Name))
                    .Select(_ => 0UL))
                .Any(maxSlot => maxSlot < currentSlot);
            await dbContext.DisposeAsync();

            // If this reducer can move forward, we break out of this loop
            if (!hasLaggingDependencies) break;

            // Otherwise we add a slight delay to recheck if the dependencies have moved forward
            Logger.LogInformation("Reducer {Reducer} is waiting for dependencies to move forward to {RollforwardSlot}", reducerName, currentSlot);
            await Task.Delay(20_000, stoppingToken);
        }
    }

    private async Task AwaitReducerDependenciesRollbackAsync(string reducerName, ulong rollbackSlot, CancellationToken stoppingToken)
    {
        // Check if the reducer has dependencies that needs to rollback first
        while (true)
        {
            // Let's check if anything depends on this reducer, that means we need them to finish rollback first
            IEnumerable<string> dependents = GetReducerDependents(reducerName);

            if (!dependents.Any()) break;

            using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
            bool anyChildAhead = await dbContext.ReducerStates
                .AsNoTracking()
                .Where(rs => dependents.Contains(rs.Name))
                .GroupBy(rs => rs.Name)
                .AnyAsync(g => g.Max(x => x.Slot) > rollbackSlot, stoppingToken);
            await dbContext.DisposeAsync();

            // If no dependents are rolling back, we can break out of this loop
            if (!anyChildAhead) break;

            // Otherwise we wait
            Logger.LogInformation("Reducer {Reducer} is waiting for dependents to finish rollback to {RollbackSlot}", reducerName, rollbackSlot);
            await Task.Delay(20_000, stoppingToken);
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

    private async Task<ulong> GetCurrentSlotAsync(string reducerName, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
        ulong? slot = await dbContext.ReducerStates
                .AsNoTracking()
                .Where(rs => rs.Name == reducerName)
                .OrderByDescending(rs => rs.Slot)
                .Select(rs => rs.Slot)
                .FirstOrDefaultAsync(stoppingToken);
        return slot ?? GetConfiguredReducerIntersection(reducerName).Slot;
    }

    private async Task<Point?> GetIntersectionAsync(string reducerName, int offset, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
        ReducerState? reducerState = await dbContext.ReducerStates
                .AsNoTracking()
                .Where(rs => rs.Name == reducerName)
                .OrderByDescending(rs => rs.Slot)
                .Skip(offset)
                .FirstOrDefaultAsync(stoppingToken);

        return reducerState is not null
            ? new(reducerState.Hash, reducerState.Slot)
            : GetConfiguredReducerIntersection(reducerName);
    }

    private static IEnumerable<string> GetReducerDependencies(string reducerName)
    {
        // Fetch dependencies from the dependency graph
        return _dependencyGraph.TryGetValue(reducerName, out HashSet<string>? dependencies)
            ? dependencies
            : Enumerable.Empty<string>();
    }

    private static IEnumerable<string> GetReducerDependents(string reducerName)
    {
        HashSet<string> visited = [];
        Stack<string> stack = new();

        // Push initial dependents of the provided reducer
        foreach (string? dependent in _dependencyGraph
            .Where(kv => kv.Value.Contains(reducerName))
            .Select(kv => kv.Key))
        {
            stack.Push(dependent);
        }

        // Traverse the graph to find all dependents
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            if (visited.Add(current))
            {
                yield return current;

                // Add dependents of the current node to the stack
                foreach (string? dependent in _dependencyGraph
                    .Where(kv => kv.Value.Contains(current))
                    .Select(kv => kv.Key))
                {
                    if (!visited.Contains(dependent))
                    {
                        stack.Push(dependent);
                    }
                }
            }
        }
    }

    private static void CreateDependencyGraph(IEnumerable<IReducer<IReducerModel>> reducers)
    {
        foreach (IReducer<IReducerModel> reducer in reducers)
        {
            string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
            IEnumerable<string> dependencies = ReducerDependencyResolver
                .GetReducerDependencies(reducer.GetType())
                .Select(ArgusUtils.GetTypeNameWithoutGenerics);

            _dependencyGraph[reducerName] = [.. dependencies];
        }
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

    private IEnumerable<string> GetActiveReducers() =>
        Configuration.GetSection("CardanoIndexReducers:ActiveReducers").Get<IEnumerable<string>>() ?? [];
}