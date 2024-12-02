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

namespace Argus.Sync.Workers;

public class CriticalNodeException(string message) : Exception(message) { }

public class CardanoIndexWorker<T>(
    IConfiguration Configuration,
    ILogger<CardanoIndexWorker<T>> Logger,
    IDbContextFactory<T> DbContextFactory,
    IEnumerable<IReducer<IReducerModel>> Reducers
) : BackgroundService where T : CardanoDbContext
{
    private static readonly Dictionary<string, (ulong CurrentSlot, List<string> Dependencies)> _reducerStates = [];
    private readonly ulong _maxRollbackSlots = Configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000UL);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pre-populate ReducerStates with registered reducers
        // and their dependencies
        Reducers.ToList().ForEach(reducer =>
        {
            string reducerName = ArgusUtils.GetTypeNameWithoutGenerics(reducer.GetType());
            List<string> dependencies = ReducerDependencyResolver.GetReducerDependencies(reducer.GetType())
                .Select(ArgusUtils.GetTypeNameWithoutGenerics)
                .ToList();
            _reducerStates.Add(reducerName, (0, dependencies));
        });

        // Get all registered reducers to fetch their currently saved slots
        // in the database
        IEnumerable<string> registeredReducers = _reducerStates.Select(e => e.Key);

        // Fetch current slots from database, we do this so we only need to fetch the slots once on sync start
        // the rest of the states we can update in-memory
        using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);
        Dictionary<string, ulong> lastRecordedReducerStates = await dbContext.ReducerStates
            .Where(rs => registeredReducers.Contains(rs.Name))
            .ToDictionaryAsync(rs => rs.Name, rs => rs.Slot, stoppingToken);
        await dbContext.DisposeAsync();

        // Update ReducerStates with the current slots
        _reducerStates.ToList().ForEach(reducerState =>
        {
            ulong lastRecordedSlot = lastRecordedReducerStates.GetValueOrDefault<string, ulong>(reducerState.Key, 0);
            _reducerStates[reducerState.Key] = (lastRecordedSlot, reducerState.Value.Dependencies);
        });

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

        // Log the new chain event rollforward
        Logger.LogInformation("[{Reducer}]: New Chain Event RollForward: Slot {Slot} Block: {Block}", reducerName, currentSlot, currentBlockNumber);

        // Execute the rollforward logic of this reducer
        Stopwatch reducerStopwatch = Stopwatch.StartNew();

        // Run the reducer's rollforward logic
        await reducer.RollForwardAsync(response.Block);

        // Update database state
        await UpdateReducerStateAsync(reducerName, currentSlot, currentBlockHash, stoppingToken);

        // Stop the timer
        reducerStopwatch.Stop();

        // Log the time taken to process the rollforward
        Logger.LogInformation("Processed RollForwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);

        // Tag as standby again after processing rollforward
        _reducerStates[reducerName] = (currentSlot, _reducerStates[reducerName].Dependencies);

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
        Logger.LogInformation("[{Reducer}]: New Chain Event RollBack: Slot {response.Block.Slot()}", reducerName, response.Block?.Slot());

        // Get the currently saved state, this could be zero if it has not been updated
        // yet in the database
        ulong currentSlot = _reducerStates[reducerName].CurrentSlot;

        // if it's zero, we do not need to rollback
        if (currentSlot == 0) return;

        // Once we're sure we can rollback, we can proceed executing the rollback function
        ulong rollbackSlot = response.RollBackType switch
        {
            RollBackType.Exclusive => response.Block!.Slot() + 1,
            RollBackType.Inclusive => response.Block!.Slot(),
            _ => 0
        };

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

        Stopwatch reducerStopwatch = new();
        reducerStopwatch.Start();

        if (rollbackSlot > 0) await reducer.RollBackwardAsync(rollbackSlot);

        // Update database state
        await UpdateReducerStateAsync(reducerName, rollbackSlot, string.Empty, stoppingToken);

        reducerStopwatch.Stop();

        _reducerStates[reducerName] = (rollbackSlot, _reducerStates[reducerName].Dependencies);

        Logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{Reducer}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);
    }

    private bool CanRollback(ulong currentSlot, ulong rollbackSlot)
    {
        long slotDifference = (long)currentSlot - (long)rollbackSlot;
        return currentSlot == 0 || rollbackSlot > currentSlot || slotDifference <= (long)_maxRollbackSlots;
    }

    private async Task UpdateReducerStateAsync(string reducerName, ulong slot, string hash, CancellationToken stoppingToken)
    {
        await using T dbContext = await DbContextFactory.CreateDbContextAsync(stoppingToken);

        ReducerState? reducerState = await dbContext.ReducerStates
            .FirstOrDefaultAsync(rs => rs.Name == reducerName, stoppingToken);

        if (reducerState == null)
        {
            reducerState = new ReducerState { Name = reducerName, Slot = slot, Hash = hash };
            dbContext.ReducerStates.Add(reducerState);
        }
        else
        {
            reducerState.Slot = slot;
            reducerState.Hash = hash;
        }

        await dbContext.SaveChangesAsync(stoppingToken);
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
}