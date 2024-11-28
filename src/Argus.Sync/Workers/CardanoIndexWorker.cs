using System.Diagnostics;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace Argus.Sync.Workers;

public class CriticalNodeException(string message) : Exception(message) { }

public class CardanoIndexWorker<T>(
    IConfiguration Configuration,
    ILogger<CardanoIndexWorker<T>> Logger,
    IDbContextFactory<T> DbContextFactory,
    IEnumerable<IReducer<IReducerModel>> Reducers
) : BackgroundService where T : CardanoDbContext
{
    private static Dictionary<string, bool> reducerRollbackStatus = [];
    private static Dictionary<string, (ulong CurrentSlot,bool IsComplete)> reducerRollforwardStatus = [];
    private static Dictionary<string, HashSet<string>> dependentReducers = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (IReducer<IReducerModel> reducer in Reducers)
        {
            string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());

            reducerRollbackStatus.TryAdd(reducerName, true);

            List<string> dependencies = ReducerDependencyResolver.GetReducerDependencies(reducer.GetType())
                .Select(ArgusUtils.GetTypeNameWithoutGenerics)
                .ToList();

            foreach (string dependency in dependencies)
            {
                if (!dependentReducers.ContainsKey(dependency))
                {
                    dependentReducers[dependency] = [];
                }
                dependentReducers[dependency].Add(reducerName);
            }
        }

        await Task.WhenAll(
            Reducers.Select(
                reducer => StartReducerChainSyncAsync(reducer, stoppingToken)
            )
        );
    }

    private async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        ICardanoChainProvider chainProvider = GetCardanoChainProvider();
        Point intersection = await GetReducerStartPoint(reducer, stoppingToken);

        await foreach (NextResponse response in chainProvider.StartChainSyncAsync(intersection, stoppingToken))
        {
            Task responseTask = response.Action switch
            {
                NextResponseAction.RollForward => ProcessRollForwardAsync(response, reducer, stoppingToken),
                NextResponseAction.RollBack => ProcessRollBackAsync(response, reducer, stoppingToken),
                _ => throw new CriticalNodeException("Next response error received."),
            };

            await responseTask;
        }
    }

    private async Task ProcessRollForwardAsync(NextResponse response, IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        ulong slot = response.Block.Slot();
        string blockHash = response.Block.Hash();

        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
        
        // Log the new chain event rollforward
        Logger.Log(
            LogLevel.Information,
            "[{Reducer}]: New Chain Event RollForward: Slot {Slot} Block: {Block}",
            reducerName,
            slot,
            response.Block.Number()
        );
        

        
        if (reducerRollforwardStatus.TryGetValue(reducerName, out var reducerState))
        {
            reducerRollforwardStatus[reducerName] = (slot, IsComplete: false);
        }
        else
        {
            reducerRollforwardStatus.Add(reducerName, (slot, false));
        }
        
        // Await reducer dependencies
        await AwaitReducerDependenciesRollForwardAsync(slot, reducer, stoppingToken);

        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        // Process the rollforward
        Stopwatch reducerStopwatch = Stopwatch.StartNew();
        await reducer.RollForwardAsync(response.Block);
        reducerStopwatch.Stop();

        // Log the time taken to process the rollforward
        Logger.Log(
            LogLevel.Information,
            "Processed RollForwardAsync[{Reducer}] in {ElapsedMilliseconds} ms",
            ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType()),
            reducerStopwatch.ElapsedMilliseconds
        );

        Task.Run(async () =>
        {
            await UpdateReducerStateAsync(reducer, slot, blockHash, stoppingToken);
        }, stoppingToken).Wait(stoppingToken);

        transaction.Complete();

        reducerRollforwardStatus[reducerName] = (slot, true);
        
        stopwatch.Stop();

        Logger.Log(
            LogLevel.Information,
            "[{Reducer}]: Processed Chain Event RollForward: Slot {Slot} Block: {Block} in {ElapsedMilliseconds} ms, Mem: {MemoryUsage} MB",
            ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType()),
            slot,
            response.Block.Number(),
            stopwatch.ElapsedMilliseconds,
            Math.Round(GetCurrentMemoryUsageInMB(), 2)
        );
    }

    private async Task ProcessRollBackAsync(NextResponse response, IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

        Logger.Log(
            LogLevel.Information,
            "[{Reducer}]: New Chain Event RollBack: Slot {response.Block.Slot()}",
            ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType()),
            response.Block?.Slot()
        );

        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
        ulong currentSlot = await dbContext.ReducerStates
            .AsNoTracking()
            .Where(rs => rs.Name == reducerName)
            .Select(rs => rs.Slot)
            .FirstOrDefaultAsync(stoppingToken);

        reducerRollbackStatus[reducerName] = true;

        if (currentSlot == 0)
        {
            reducerRollbackStatus[reducerName] = false;
            return;
        }

        await PreventMassrollbackAsync(reducer, response.Block!.Slot(), Logger);
        await AwaitReducerDependenciesRollbackAsync(currentSlot, response.Block!.Slot(), reducer, stoppingToken);

        ulong requestedRollbackSlot = response.Block!.Slot();
        ulong rollBackSlot = 0;

        if (response.RollBackType == RollBackType.Exclusive)
        {
            rollBackSlot = requestedRollbackSlot + 1;
        }
        else if (response.RollBackType == RollBackType.Inclusive)
        {
            rollBackSlot = requestedRollbackSlot;
        }

        Stopwatch reducerStopwatch = new();
        reducerStopwatch.Start();
        if (rollBackSlot != 0)
        {
            await reducer.RollBackwardAsync(rollBackSlot);
        }
        reducerStopwatch.Stop();
        reducerRollbackStatus[reducerName] = false;

        Logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);
    }

    private async Task UpdateReducerStateAsync(IReducer<IReducerModel> reducer, ulong slot, string hash, CancellationToken stoppingToken)
    {
        try
        {
            await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
            string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
            var reducerState = await dbContext.ReducerStates
                .FirstOrDefaultAsync(rs => rs.Name == reducerName, stoppingToken);

            if (reducerState == null)
            {
                reducerState = new ReducerState
                {
                    Name = reducerName,
                    Slot = slot,
                    Hash = hash
                };
                dbContext.ReducerStates.Add(reducerState);
            }
            else
            {
                reducerState.Slot = slot;
                reducerState.Hash = hash;
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating ReducerState for {Reducer}", ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType()));
        }
    }

    private async Task AwaitReducerDependenciesRollForwardAsync(
        ulong slot,
        IReducer<IReducerModel> reducer,
        CancellationToken cancellationToken
    )
    {
        var dependencyTypes = ReducerDependencyResolver.GetReducerDependencies(reducer.GetType());
        var dependencyNames = dependencyTypes
            .Select(ArgusUtils.GetTypeNameWithoutGenerics)
            .ToList();

        if (!dependencyNames.Any())
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            bool anyDependencyRollingBack = dependencyNames
                .Any(depName => reducerRollbackStatus.TryGetValue(depName, out bool status) && status);

            if (!anyDependencyRollingBack)
            {
                await using T dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
                var currentStates = await dbContext.ReducerStates
                    .AsNoTracking()
                    .Where(rs => dependencyNames.Contains(rs.Name))
                    .ToListAsync(cancellationToken);

                var missingDependencies = dependencyNames
                    .Except(currentStates.Select(rs => rs.Name))
                    .ToList();

                var outdatedDependencies = currentStates
                    .Where(rs => rs.Slot < slot)
                    .ToList();

                var hasCurrentlyProcessingDependency = reducerRollforwardStatus.ToList()
                    .Where(rs => dependencyNames.Contains(rs.Key))
                    .Any(rs => rs.Value.CurrentSlot == slot && !rs.Value.IsComplete);

                await dbContext.DisposeAsync();

                if (!missingDependencies.Any() && !outdatedDependencies.Any() && !hasCurrentlyProcessingDependency)
                {
                    return;
                }

                missingDependencies.ForEach(missing =>
                {
                    Logger.Log(
                        LogLevel.Information,
                        "[{Reducer}]: Waiting for missing dependency {Dependency} to reach Slot {RequiredSlot}",
                        ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType()),
                        missing,
                        slot);
                });

                outdatedDependencies.ForEach(outdated =>
                {
                    Logger.Log(
                        LogLevel.Information,
                        "[{Reducer}]: Waiting for dependency {Dependency} to reach Slot {RequiredSlot} (current: {CurrentSlot})",
                        ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType()),
                        outdated.Name,
                        slot,
                        outdated.Slot);
                });
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async Task AwaitReducerDependenciesRollbackAsync(
        ulong currentSlot,
        ulong rollBackSlot,
        IReducer<IReducerModel> reducer,
        CancellationToken cancellationToken
    )
    {
        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());

        if (dependentReducers.TryGetValue(reducerName, out var dependents) && dependents.Any())
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using T dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
                var dependentStates = await dbContext.ReducerStates
                    .AsNoTracking()
                    .Where(rs => dependents.Contains(rs.Name))
                    .ToListAsync(cancellationToken);

                var dependentsNotRolledBack = dependentStates
                    .Where(rs => rs.Slot > rollBackSlot)
                    .ToList();

                if (!dependentsNotRolledBack.Any())
                {
                    Logger.Log(
                        LogLevel.Information,
                        "[{Reducer}]: All dependent reducers have rolled back successfully",
                        reducerName
                    );
                    break;
                }

                foreach (var dependent in dependentsNotRolledBack)
                {
                    Logger.Log(
                        LogLevel.Information,
                        "[{Reducer}]: Waiting for {Dependent} to roll back to Slot {RequiredSlot} (current: {CurrentSlot})",
                        reducerName,
                        dependent.Name,
                        rollBackSlot,
                        dependent.Slot
                    );
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }

    public async Task PreventMassrollbackAsync(
        IReducer<IReducerModel> reducer,
        ulong requestedRollBackSlot,
        ILogger logger
    )
    {
        string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());

        // Create DbContext
        await using T dbContext = await DbContextFactory.CreateDbContextAsync();

        ulong reducerCurrentSlot = await reducer.QueryTip();

        // Only execute mass rollback prevention logic if the reducer has processed at least one block
        if (reducerCurrentSlot > 0)
        {
            ulong maxAdditionalRollbackSlots = Configuration.GetValue<ulong?>("CardanoNodeConnection:MaxRollbackSlots")
                ?? throw new InvalidOperationException("MaxRollbackSlots is not specified in the configuration.");

            if (requestedRollBackSlot > reducerCurrentSlot)
            {
                return;
            }

            if (reducerCurrentSlot - requestedRollBackSlot > maxAdditionalRollbackSlots)
            {
                logger.Log(
                    LogLevel.Error,
                    "PreventMassrollbackAsync[{Reducer}] Requested RollBack Slot {RequestedSlot} is more than {MaxRollback} slots behind current slot {CurrentSlot}.",
                    reducerName,
                    requestedRollBackSlot,
                    maxAdditionalRollbackSlots,
                    reducerCurrentSlot
                );
                throw new CriticalNodeException("Rollback, Critical Error, Aborting");
            }
        }

        await dbContext.DisposeAsync();
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
        var reducerSection = Configuration.GetSection($"CardanoIndexReducers:{reducerName}");

        // Retrieve the StartSlot and StartHash for the reducer, or use defaults if not specified
        ulong configStartSlot = reducerSection.GetValue<ulong?>("StartSlot") ?? defaultStartSlot;
        string configStartHash = reducerSection.GetValue<string>("StartHash") ?? defaultStartHash;

        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
        ReducerState? reducerState = await dbContext.ReducerStates
                  .FirstOrDefaultAsync(rs => rs.Name == ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType()), stoppingToken);

        if (reducerState is not null)
        {
            configStartSlot = reducerState.Slot;
            configStartHash = reducerState.Hash;
        }

        await dbContext.DisposeAsync();
        return new Point(configStartHash, configStartSlot);
    }

    private ICardanoChainProvider GetCardanoChainProvider()
    {
        var config = Configuration.GetSection("CardanoNodeConnection");
        var connectionType = config.GetValue<string>("ConnectionType")
            ?? throw new InvalidOperationException("ConnectionType is not specified in the configuration.");
        var networkMagic = config.GetValue<ulong?>("NetworkMagic")
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
}