using System.Collections;
using System.Collections.Concurrent;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using NextResponse = Argus.Sync.Data.Models.NextResponse;

namespace Argus.Sync.Workers;

public class CardanoIndexWorker<T>(
    IConfiguration configuration,
    ILogger<CardanoIndexWorker<T>> logger,
    IDbContextFactory<T> dbContextFactory,
    IEnumerable<IReducer<IReducerModel>> reducers
) : BackgroundService where T : CardanoDbContext
{
    private readonly ConcurrentDictionary<string, ReducerState> _reducerStates = [];

    private readonly long _maxRollbackSlots = configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000);
    private readonly int _rollbackBuffer = configuration.GetValue("CardanoNodeConnection:RollbackBuffer", 10);
    private readonly ulong _networkMagic = configuration.GetValue("CardanoNodeConnection:NetworkMagic", 2UL);
    private readonly string _connectionType = configuration.GetValue<string>("CardanoNodeConnection:ConnectionType") ?? throw new Exception("Connection type not configured.");
    private readonly string? _socketPath = configuration.GetValue<string?>("CardanoNodeConnection:UnixSocket:Path");
    private readonly string? _gRPCEndpoint = configuration.GetValue<string?>("CardanoNodeConnection:gRPC:Endpoint");
    private readonly string? _apiKey = configuration.GetValue<string?>("CardanoNodeConnection:gRPC:ApiKey");
    private readonly string _defaultStartHash = configuration.GetValue<string>("CardanoNodeConnection:Hash") ?? throw new Exception("Default start hash not configured.");
    private readonly ulong _defaultStartSlot = configuration.GetValue<ulong?>("CardanoNodeConnection:Slot") ?? throw new Exception("Default start slot not configured.");
    private Point CurrentTip = new(string.Empty, 0);


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        _ = Task.Run(StartLogger, stoppingToken);
        _ = Task.Run(async () => await StartReducerStateSync(stoppingToken), stoppingToken);
        await Task.WhenAny(reducers.Select(reducer => StartReducerChainSyncAsync(reducer, stoppingToken)));

        Environment.Exit(0);
    }

    private async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
        ReducerState reducerState = await GetReducerStateAsync(reducerName, stoppingToken);

        if (reducerState is null)
        {
            logger.LogError("Failed to get the initial intersection for {Reducer}", reducerName);
            throw new Exception($"Failed to determine chainsync intersection for {reducerName}");
        }

        _reducerStates[reducerName] = reducerState;

        ICardanoChainProvider chainProvider = GetCardanoChainProvider();
        await foreach (NextResponse nextResponse in chainProvider.StartChainSyncAsync(reducerState.LatestIntersections, stoppingToken))
        {
            Task reducerTask = nextResponse.Action switch
            {
                NextResponseAction.RollForward => ProcessRollforwardAsync(reducer, nextResponse),
                NextResponseAction.RollBack => ProcessRollbackAsync(reducer, nextResponse),
                _ => throw new Exception($"Next response error received. {nextResponse}"),
            };

            await reducerTask;
        }
    }

    private async Task ProcessRollforwardAsync(IReducer<IReducerModel> reducer, NextResponse response)
    {
        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
        await reducer.RollForwardAsync(response.Block);

        Point recentIntersection = new(response.Block.Header().Hash(), response.Block.HeaderBody().Slot());
        IEnumerable<Point> latestIntersections = UpdateLatestIntersections(_reducerStates[reducerName].LatestIntersections, recentIntersection);
        _reducerStates[reducerName] = _reducerStates[reducerName] with
        {
            LatestIntersections = latestIntersections
        };
    }

    private async Task ProcessRollbackAsync(IReducer<IReducerModel> reducer, NextResponse response)
    {
        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
        ulong rollbackSlot = response.RollBackType switch
        {
            RollBackType.Exclusive => response.Block.HeaderBody().Slot() + 1UL,
            RollBackType.Inclusive => response.Block.HeaderBody().Slot(),
            _ => 0
        };

        long rollbackDepth = (long)_reducerStates[reducerName].LatestSlot - (long)rollbackSlot;

        if (rollbackDepth >= _maxRollbackSlots)
        {
            throw new Exception($"Requested RollBack Slot {rollbackSlot} is more than {_maxRollbackSlots} slots behind current slot {_reducerStates[reducerName].LatestSlot}.");
        }

        await reducer.RollBackwardAsync(rollbackSlot);

        IEnumerable<Point> latestIntersections = _reducerStates[reducerName].LatestIntersections;
        latestIntersections = latestIntersections.Where(i => i.Slot < rollbackSlot);
        _reducerStates[reducerName] = _reducerStates[reducerName] with
        {
            LatestIntersections = latestIntersections
        };
    }

    private ICardanoChainProvider GetCardanoChainProvider() => _connectionType switch
    {
        "UnixSocket" => new N2CProvider(_socketPath ?? throw new InvalidOperationException("Socket path is not configured.")),
        "TCP" => throw new NotImplementedException("TCP connection type is not yet implemented."),
        "gRPC" => new U5CProvider(
            _gRPCEndpoint ?? throw new Exception("gRPC endpoint is not configured."),
            new Dictionary<string, string>
            {
                { "dmtr-api-key", _apiKey ?? throw new Exception("Demeter API key is missing") }
            }
        ),
        _ => throw new Exception("Invalid chain provider")
    };

    private async Task<ReducerState> GetReducerStateAsync(string reducerName, CancellationToken stoppingToken)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync(stoppingToken);

        ReducerState? state = await dbContext.ReducerStates
            .AsNoTracking()
            .Where(state => state.Name == reducerName)
            .FirstOrDefaultAsync(cancellationToken: stoppingToken);

        ReducerState initialState = GetDefaultReducerState(reducerName);

        return state ?? initialState;
    }

    private ReducerState GetDefaultReducerState(string reducerName)
    {
        IConfigurationSection reducerSection = configuration.GetSection($"CardanoIndexReducers:{reducerName}");
        ulong configStartSlot = reducerSection.GetValue<ulong?>("StartSlot") ?? _defaultStartSlot;
        string configStartHash = reducerSection.GetValue<string>("StartHash") ?? _defaultStartHash;
        Point defaultIntersection = new(configStartHash, configStartSlot);
        List<Point> latestIntersections = [defaultIntersection];
        ReducerState initialState = new(reducerName, DateTimeOffset.UtcNow)
        {
            StartIntersection = defaultIntersection,
            LatestIntersections = latestIntersections
        };

        return initialState;
    }

    private async Task UpdateReducerStatesAsync(CancellationToken stoppingToken)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync(stoppingToken);

        IEnumerable<ReducerState> newStates = _reducerStates.Values;
        IEnumerable<string> reducerNames = newStates.Select(ns => ns.Name);
        IEnumerable<ReducerState> reducerStates = await dbContext.ReducerStates
            .Where(rs => reducerNames.Contains(rs.Name))
            .ToListAsync(cancellationToken: stoppingToken);

        foreach (ReducerState newState in newStates)
        {
            ReducerState? existingState = reducerStates.FirstOrDefault(rs => rs.Name == newState.Name);
            if (existingState is not null)
            {
                existingState.LatestIntersections = newState.LatestIntersections;
            }
            else
            {
                dbContext.ReducerStates.Add(newState);
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private IEnumerable<Point> UpdateLatestIntersections(IEnumerable<Point> latestIntersections, Point newIntersection)
    {
        latestIntersections = latestIntersections.OrderByDescending(i => i.Slot);
        if (latestIntersections.Count() >= _rollbackBuffer)
        {
            latestIntersections = latestIntersections.SkipLast(1);
        }
        else
        {
            latestIntersections = latestIntersections.Append(newIntersection);
        }

        return latestIntersections;
    }

    private async Task StartLogger()
    {
        ICardanoChainProvider chainProvider = GetCardanoChainProvider();
        await Task.Delay(1000);

        await AnsiConsole.Progress()
            .Columns(
            [
                new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn(),
            ])
            .StartAsync(async ctx =>
            {
                List<ProgressTask> tasks = [.. reducers.Select(reducer =>
                    {
                        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
                        return ctx.AddTask(reducerName);
                    })];

                while (!ctx.IsFinished)
                {
                    await Task.Delay(1000);
                    CurrentTip = await chainProvider.GetTipAsync();

                    foreach (ProgressTask task in tasks)
                    {
                        ReducerState state = _reducerStates[task.Description];
                        ulong startSlot = state.StartIntersection.Slot;
                        ulong currentSlot = state.LatestIntersections.MaxBy(p => p.Slot)?.Slot ?? 0UL;
                        ulong tipSlot = CurrentTip.Slot;

                        if (tipSlot <= startSlot)
                        {
                            task.Value = 100.0;
                        }
                        else
                        {
                            ulong totalSlotsToSync = tipSlot - startSlot;
                            ulong totalSlotsSynced = currentSlot >= startSlot ? currentSlot - startSlot : 0;
                            double progress = (double)totalSlotsSynced / totalSlotsToSync * 100.0;
                            task.Value = (double)totalSlotsSynced / totalSlotsToSync * 100.0;
                        }
                    }
                }
            }
        );
    }

    private async Task StartReducerStateSync(CancellationToken stoppingToken)
    {
        await Task.Delay(5000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
            await UpdateReducerStatesAsync(stoppingToken);
        }
    }
}

// public class CriticalNodeException(string message) : Exception(message) { }

// public class CardanoIndexWorker<T>(
//     IConfiguration Configuration,
//     ILogger<CardanoIndexWorker<T>> Logger,
//     IDbContextFactory<T> DbContextFactory,
//     IEnumerable<IReducer<IReducerModel>> Reducers
// ) : BackgroundService where T : CardanoDbContext
// {
// private readonly ulong _maxRollbackSlots = Configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000UL);
// private static readonly ConcurrentDictionary<string, HashSet<string>> _dependencyGraph = new();
// private int RollbackBuffer => GetRollbackBuffer("default");

//     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//     {
//         // Create dependency graph
//         // CreateDependencyGraph(Reducers);

//         HashSet<string> activeReducerNames = [.. GetActiveReducers()];
// IEnumerable<IReducer<IReducerModel>> reducersToRun = Reducers;
//         if (activeReducerNames.Count != 0)
//         {
//             reducersToRun = [.. Reducers.Where(r =>
//             {
//                 string name = ArgusUtils.GetTypeNameWithoutGenerics(r.GetType());
//                 return activeReducerNames.Contains(name, StringComparer.OrdinalIgnoreCase);
//             })];
//         }

//         if (reducersToRun.Any())
//         {
//             // Execute reducers
/* The above code is using the `Task.WhenAny` method in C# to asynchronously start multiple
reducer chains and wait for any one of them to complete. The `reducersToRun` collection
contains the reducers to be executed, and the `StartReducerChainSyncAsync` method is
called for each reducer to start its chain of operations asynchronously. The code will
wait for the first reducer chain to complete before continuing execution. */
// await Task.WhenAny(
//     reducersToRun.Select(
//         reducer => StartReducerChainSyncAsync(reducer, stoppingToken)
//     )
// );
//         }

//         Environment.Exit(1);
//     }

// private async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
// {
// // Get the initial start intersection for the reducer
// string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
// Logger.LogInformation("Starting Chain Sync for {Reducer}", reducerName);
// Point? startIntersection = await GetRollbackIntersectionAsync(reducerName, 1, stoppingToken);

// if (startIntersection is null)
// {
//     Logger.LogError("Failed to get the initial intersection for {Reducer}", reducerName);
//     throw new CriticalNodeException("Critical Error, Aborting");
// }

//     NextResponse? currentResponse = null;
//     string action = string.Empty;
//     try
//     {
//         // Start the chain sync
//         ICardanoChainProvider chainProvider = GetCardanoChainProvider();
// await foreach (NextResponse response in chainProvider.StartChainSyncAsync(startIntersection, stoppingToken))
// {

//     currentResponse = response;
//     action = currentResponse?.Action switch
//     {
//         NextResponseAction.RollForward => "RollForward",
//         NextResponseAction.RollBack => "RollBack",
//         _ => "Unknown"
//     };

//     Task responseTask = response.Action switch
//     {
//         NextResponseAction.RollForward => ProcessRollForwardAsync(response, reducer, stoppingToken),
//         NextResponseAction.RollBack => ProcessRollBackAsync(response, reducer, stoppingToken),
//         _ => throw new CriticalNodeException($"Next response error received. {response}"),
//     };

//     Stopwatch stopwatch = Stopwatch.StartNew();
//     await responseTask;
//     stopwatch.Stop();

//     Logger.LogInformation(
//         "[{Reducer}]: Processed Chain Event {Action}: Slot {Slot} Block: {Block} in {ElapsedMilliseconds} ms, Mem: {MemoryUsage} MB",
//         action,
//         reducerName,
//         currentResponse?.Block.Header().Slot(),
//         currentResponse?.Block.Header().Number(),
//         stopwatch.ElapsedMilliseconds,
//         Math.Round(GetCurrentMemoryUsageInMB(), 2)
//     );
// }
//     }
//     catch (Exception ex)
//     {

//         Logger.LogError(
//             ex,
//             "[{Reducer}][{Action}] Something went wrong. Block: {BlockHash} Slot: {Slot}",
//             reducerName, action, currentResponse?.Block.Header().Hash(), currentResponse?.Block.Header().Slot()
//         );

//         throw new CriticalNodeException($"Critical Error, Aborting");
//     }
// }

//     private async Task ProcessRollForwardAsync(NextResponse response, IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
//     {
//         ulong currentSlot = response.Block.Header().Slot();
//         ulong currentBlockNumber = response.Block.Header().Number();
//         string? currentBlockHash = response.Block.Header().Hash();
//         string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());

//         // Let's check if the reducer can move forward
//         await AwaitReducerDependenciesRollForwardAsync(reducerName, currentSlot, stoppingToken);

//         // Log the new chain event rollforward
//         Logger.LogInformation("[{Reducer}]: New Chain Event RollForward: Slot {Slot} Block: {Block}", reducerName, currentSlot, currentBlockNumber);

//         Stopwatch reducerStopwatch = Stopwatch.StartNew();
//         await reducer.RollForwardAsync(response.Block);
//         reducerStopwatch.Stop();

//         // Update database state
//         await UpdateReducerStateAsync(reducerName, currentSlot, currentBlockHash, stoppingToken);

//         // Log the time taken to process the rollforward
//         Logger.LogInformation("Processed RollForwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);
//     }

//     private async Task ProcessRollBackAsync(NextResponse response, IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
//     {
//         string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());

//         ulong currentSlot = await GetCurrentSlotAsync(reducerName, stoppingToken);

//         // Once we're sure we can rollback, we can proceed executing the rollback function
// ulong rollbackSlot = response.RollBackType switch
// {
//     RollBackType.Exclusive => response.Block.Header().Slot() + 1UL,
//     RollBackType.Inclusive => response.Block.Header().Slot(),
//     _ => 0
// };

//         PreventMassRollback(currentSlot, rollbackSlot, reducerName, stoppingToken);

//         await AwaitReducerDependenciesRollbackAsync(reducerName, rollbackSlot, stoppingToken);

//         Logger.LogInformation("[{Reducer}]: New Chain Event RollBack: Slot {Slot}", reducerName, rollbackSlot);

//         Stopwatch reducerStopwatch = Stopwatch.StartNew();
//         await reducer.RollBackwardAsync(rollbackSlot);
//         reducerStopwatch.Stop();

//         // Update database state
//         await RemoveReducerStateAsync(reducerName, rollbackSlot, stoppingToken);


//         Logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);
//     }

// private async Task UpdateReducerStateAsync(string reducerName, ulong slot, string hash, CancellationToken stoppingToken)
// {
//     await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

//     ReducerState newState = new()
//     {
//         Name = reducerName,
//         Slot = slot,
//         Hash = hash
//     };

//     dbContext.ReducerStates.Add(newState);

//     // Remove old entries, keeping only the newest 10
//     IQueryable<ReducerState> oldStates = dbContext.ReducerStates
//         .AsNoTracking()
//         .Where(rs => rs.Name == reducerName)
//         .OrderByDescending(rs => rs.Slot)
//         .Skip(RollbackBuffer);

//     dbContext.ReducerStates.RemoveRange(oldStates);

//     await dbContext.SaveChangesAsync(stoppingToken);
// }

//     private async Task RemoveReducerStateAsync(string reducerName, ulong slot, CancellationToken stoppingToken)
//     {
//         await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

//         IQueryable<ReducerState> removeQuery = dbContext.ReducerStates
//             .AsNoTracking()
//             .Where(rs => rs.Name == reducerName && rs.Slot >= slot);

//         dbContext.RemoveRange(removeQuery);
//         await dbContext.SaveChangesAsync(stoppingToken);
//     }

//     private async Task AwaitReducerDependenciesRollForwardAsync(string reducerName, ulong currentSlot, CancellationToken stoppingToken)
//     {
//         while (true)
//         {
//             // Check for dependencies
//             IEnumerable<string> dependencies = GetReducerDependencies(reducerName);

//             if (!dependencies.Any()) break;

//             // Get the latest slot for each dependency
//             using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
//             var dependencyStates = await dbContext.ReducerStates
//                 .AsNoTracking()
//                 .GroupBy(rs => rs.Name)
//                 .Select(g => new { Name = g.Key, MaxSlot = g.Max(x => x.Slot) })
//                 .ToListAsync(stoppingToken);

//             bool hasLaggingDependencies = dependencyStates
//                 .Where(x => dependencies.Contains(x.Name))
//                 .Select(x => x.MaxSlot)
//                 .Union(dependencies
//                     .Except(dependencyStates.Select(x => x.Name))
//                     .Select(_ => 0UL))
//                 .Any(maxSlot => maxSlot < currentSlot);
//             await dbContext.DisposeAsync();

//             // If this reducer can move forward, we break out of this loop
//             if (!hasLaggingDependencies) break;

//             // Otherwise we add a slight delay to recheck if the dependencies have moved forward
//             Logger.LogInformation("Reducer {Reducer} is waiting for dependencies to move forward to {RollforwardSlot}", reducerName, currentSlot);
//             await Task.Delay(5_000, stoppingToken);
//         }
//     }

//     private async Task AwaitReducerDependenciesRollbackAsync(string reducerName, ulong rollbackSlot, CancellationToken stoppingToken)
//     {
//         // Check if the reducer has dependencies that needs to rollback first
//         while (true)
//         {
//             // Let's check if anything depends on this reducer, that means we need them to finish rollback first
//             IEnumerable<string> dependents = GetReducerDependents(reducerName);

//             if (!dependents.Any()) break;

//             using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
//             bool anyChildAhead = await dbContext.ReducerStates
//                 .AsNoTracking()
//                 .Where(rs => dependents.Contains(rs.Name))
//                 .AnyAsync(rs => rs.Slot > rollbackSlot, stoppingToken);
//             await dbContext.DisposeAsync();

//             // If no dependents are rolling back, we can break out of this loop
//             if (!anyChildAhead) break;

//             // Otherwise we wait
//             Logger.LogInformation("Reducer {Reducer} is waiting for dependents to finish rollback to {RollbackSlot}", reducerName, rollbackSlot);
//             await Task.Delay(5_000, stoppingToken);
//         }
//     }

//     private void PreventMassRollback(ulong currentSlot, ulong rollbackSlot, string reducerName, CancellationToken stoppingToken)
//     {
//         if (!CanRollback(currentSlot, rollbackSlot))
//         {
//             Logger.LogError(
//                 "PreventMassrollbackAsync[{Reducer}] Requested RollBack Slot {RequestedSlot} is more than {MaxRollback} slots behind current slot {CurrentSlot}.",
//                 reducerName,
//                 rollbackSlot,
//                 _maxRollbackSlots,
//                 currentSlot
//             );

//             // We need to force the app to crash, there is something wrong if we're rolling back
//             // too far than expected
//             throw new CriticalNodeException("Rollback, Critical Error, Aborting");
//         }
//     }

//     private bool CanRollback(ulong currentSlot, ulong rollbackSlot)
//     {
//         long slotDifference = (long)currentSlot - (long)rollbackSlot;
//         return currentSlot == 0 || rollbackSlot > currentSlot || slotDifference <= (long)_maxRollbackSlots;
//     }

// private Point GetConfiguredReducerIntersection(string reducerName)
// {
//     // Get default start slot and hash from global configuration
//     ulong defaultStartSlot = Configuration.GetValue<ulong?>("CardanoNodeConnection:Slot")
//         ?? throw new InvalidOperationException("Default StartSlot is not specified in the configuration.");
//     string defaultStartHash = Configuration.GetValue<string>("CardanoNodeConnection:Hash")
//         ?? throw new InvalidOperationException("Default StartHash is not specified in the configuration.");

//     // Get the configuration section for the specific reducer
//     IConfigurationSection reducerSection = Configuration.GetSection($"CardanoIndexReducers:{reducerName}");

//     // Retrieve the StartSlot and StartHash for the reducer, or use defaults if not specified
//     ulong configStartSlot = reducerSection.GetValue<ulong?>("StartSlot") ?? defaultStartSlot;
//     string configStartHash = reducerSection.GetValue<string>("StartHash") ?? defaultStartHash;

//     Logger.LogInformation("Using configured intersection for {Reducer}: Slot {Slot} Hash {Hash}", reducerName, configStartSlot, configStartHash);
//     return new Point(configStartHash, configStartSlot);
// }

// private ICardanoChainProvider GetCardanoChainProvider()
// {
//     IConfigurationSection config = Configuration.GetSection("CardanoNodeConnection");
//     string connectionType = config.GetValue<string>("ConnectionType")
//         ?? throw new InvalidOperationException("ConnectionType is not specified in the configuration.");
//     ulong networkMagic = config.GetValue<ulong?>("NetworkMagic")
//         ?? throw new InvalidOperationException("NetworkMagic is not specified in the configuration.");

//     return connectionType switch
//     {
//         "UnixSocket" =>
//             new N2CProvider(
//                 config.GetValue<string>("UnixSocket:Path")
//                 ?? throw new InvalidOperationException("UnixSocket:Path is not specified in the configuration.")
//             ),

//         "TCP" =>
//             throw new NotImplementedException("TCP connection type is not yet implemented."),

//         "gRPC" =>
//             new U5CProvider(
//                 config.GetSection("gRPC").GetValue<string>("Endpoint")
//                     ?? throw new InvalidOperationException("gRPC:Endpoint is not specified in the configuration."),
//                 new Dictionary<string, string>
//                 {
//                     { "dmtr-api-key", config.GetSection("gRPC").GetValue<string>("ApiKey")
//                         ?? throw new InvalidOperationException("gRPC:ApiKey is not specified in the configuration.") }
//                 }
//             ),
//         _ => throw new InvalidOperationException("Invalid ConnectionType specified.")
//     };
// }

//     private async Task<ulong> GetCurrentSlotAsync(string reducerName, CancellationToken stoppingToken)
//     {
//         await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
//         ulong? slot = await dbContext.ReducerStates
//                 .AsNoTracking()
//                 .Where(rs => rs.Name == reducerName)
//                 .OrderByDescending(rs => rs.Slot)
//                 .Select(rs => rs.Slot)
//                 .FirstOrDefaultAsync(stoppingToken);
//         return slot ?? GetConfiguredReducerIntersection(reducerName).Slot;
//     }

// private async Task<Point?> GetRollbackIntersectionAsync(string reducerName, int requestedOffset, CancellationToken stoppingToken)
// {
//     await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

//     // First get all states for this reducer, ordered by slot descending
//     var states = await dbContext.ReducerStates
//         .AsNoTracking()
//         .Where(rs => rs.Name == reducerName)
//         .OrderByDescending(rs => rs.Slot)
//         .ToListAsync(stoppingToken);

//     // No states case - fall back to configuration
//     if (states.Count == 0)
//     {
//         return GetConfiguredReducerIntersection(reducerName);
//     }

//     // Single state case - use that state
//     if (states.Count == 1)
//     {
//         return new Point(states[0].Hash, states[0].Slot);
//     }

//     return new Point(states[1].Hash, states[1].Slot);
// }

//     private static IEnumerable<string> GetReducerDependencies(string reducerName)
//     {
//         // Fetch dependencies from the dependency graph
//         return _dependencyGraph.TryGetValue(reducerName, out HashSet<string>? dependencies)
//             ? dependencies
//             : Enumerable.Empty<string>();
//     }

//     private static IEnumerable<string> GetReducerDependents(string reducerName)
//     {
//         HashSet<string> visited = [];
//         Stack<string> stack = new();

//         // Push initial dependents of the provided reducer
//         foreach (string? dependent in _dependencyGraph
//             .Where(kv => kv.Value.Contains(reducerName))
//             .Select(kv => kv.Key))
//         {
//             stack.Push(dependent);
//         }

//         // Traverse the graph to find all dependents
//         while (stack.Count > 0)
//         {
//             string current = stack.Pop();
//             if (visited.Add(current))
//             {
//                 yield return current;

//                 // Add dependents of the current node to the stack
//                 foreach (string? dependent in _dependencyGraph
//                     .Where(kv => kv.Value.Contains(current))
//                     .Select(kv => kv.Key))
//                 {
//                     if (!visited.Contains(dependent))
//                     {
//                         stack.Push(dependent);
//                     }
//                 }
//             }
//         }
//     }

//     // private static void CreateDependencyGraph(IEnumerable<IReducer<IReducerModel>> reducers)
//     // {
//     //     foreach (IReducer<IReducerModel> reducer in reducers)
//     //     {
//     //         string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
//     //         IEnumerable<string> dependencies = ReducerDependencyResolver
//     //             .GetReducerDependencies(reducer.GetType())
//     //             .Select(ArgusUtils.GetTypeNameWithoutGenerics);

//     //         _dependencyGraph[reducerName] = [.. dependencies];
//     //     }
//     // }

//     private static double GetCurrentMemoryUsageInMB()
//     {
//         Process currentProcess = Process.GetCurrentProcess();

//         // Getting the physical memory usage of the current process in bytes
//         long memoryUsed = currentProcess.WorkingSet64;

//         // Convert to megabytes for easier reading
//         double memoryUsedMb = memoryUsed / 1024.0 / 1024.0;

//         return memoryUsedMb;
//     }

// private int GetRollbackBuffer(string reducerName) =>
//     Configuration.GetValue<int?>($"CardanoIndexReducers:{reducerName}:RollbackBuffer")
//         ?? Configuration.GetValue("CardanoNodeConnection:RollbackBuffer", 10);

//     private IEnumerable<string> GetActiveReducers() =>
//         Configuration.GetSection("CardanoIndexReducers:ActiveReducers").Get<IEnumerable<string>>() ?? [];
// }