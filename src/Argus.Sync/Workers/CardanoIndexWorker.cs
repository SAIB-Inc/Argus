using System.Diagnostics;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PallasDotnet.Models;
using Point = PallasDotnet.Models.Point;
namespace Argus.Sync.Workers;

public class CriticalNodeException(string message) : Exception(message) { }

public class CardanoIndexWorker<T>(
    IConfiguration Configuration,
    ILogger<CardanoIndexWorker<T>> Logger,
    IDbContextFactory<T> DbContextFactory,
    IEnumerable<IReducer<IReducerModel>> Reducers
) : BackgroundService where T : CardanoDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            Reducers.Select(
                reducer => StartReducerChainSyncAsync(reducer, stoppingToken)
            )
        );
    }

    private async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        /*
            Support these features:
                Support Reducer Dependencies
                Massrollback prevention
        */
        ICardanoChainProvider chainProvider = GetCardanoChainProvider();
        Point intersection = GetReducerStartPoint(reducer);

        await foreach (NextResponse response in chainProvider.StartChainSyncAsync(intersection, stoppingToken))
        {
            if (response.Action == NextResponseAction.Await) continue;

            Task responseTask = response.Action switch
            {
                NextResponseAction.RollForward => ProcessRollForwardAsync(response, reducer),
                NextResponseAction.RollBack => ProcessRollBackAsync(response, reducer),
                _ => throw new CriticalNodeException("Next response error received."),
            };

            await responseTask;
        }
    }

    private async Task ProcessRollForwardAsync(NextResponse response, IReducer<IReducerModel> reducer)
    {
        Block? block = CborSerializer.Deserialize<Block>(response.BlockCbor) ?? throw new CriticalNodeException("Block deserialization failed.");

        // Log the block slot
        Logger.Log(
            LogLevel.Information,
            "[{Reducer}]: Processing Block Slot {Slot}",
            GetTypeNameWithoutGenerics(reducer.GetType()),
            block.Slot()
        );
    }

    private async Task ProcessRollBackAsync(NextResponse response, IReducer<IReducerModel> reducer)
    {
    }


    private async Task AwaitReducerDependenciesAsync(
        ulong slot,
        IReducer<IReducerModel> reducer,
        CancellationToken cancellationToken
    )
    {
        // Retrieve and prepare dependency names
        var dependencyTypes = ReducerDependencyResolver.GetReducerDependencies(reducer.GetType());
        var dependencyNames = dependencyTypes
            .Select(GetTypeNameWithoutGenerics)
            .ToList();

        while (!cancellationToken.IsCancellationRequested)
        {
            // Initialize the database context
            await using T dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
            // Fetch all relevant reducer states in a single query
            var currentStates = await dbContext.ReducerStates
                .AsNoTracking()
                .Where(rs => dependencyNames.Contains(rs.Name))
                .ToListAsync(cancellationToken);

            // Identify missing dependencies
            var missingDependencies = dependencyNames
                .Except(currentStates.Select(rs => rs.Name))
                .ToList();

            // Identify dependencies with outdated slots
            var outdatedDependencies = currentStates
                .Where(rs => rs.Slot < slot)
                .ToList();

            // Check if all dependencies are satisfied
            if (!missingDependencies.Any() && !outdatedDependencies.Any())
            {
                break; // All dependencies are met; exit the loop
            }

            // Log missing dependencies
            missingDependencies.ForEach(missing =>
            {
                Logger.Log(
                    LogLevel.Information,
                    "[{Reducer}]: Waiting for missing dependency {Dependency} to reach Slot {RequiredSlot}",
                    GetTypeNameWithoutGenerics(reducer.GetType()),
                    missing,
                    slot);
            });

            // Log dependencies with outdated slots
            outdatedDependencies.ForEach(outdated =>
            {
                Logger.Log(
                    LogLevel.Information,
                    "[{Reducer}]: Waiting for dependency {Dependency} to reach Slot {RequiredSlot} (current: {CurrentSlot})",
                    GetTypeNameWithoutGenerics(reducer.GetType()),
                    outdated.Name,
                    slot,
                    outdated.Slot);
            });

            // Dispose of the database context
            await dbContext.DisposeAsync();

            // Wait before rechecking dependencies
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }


    public async Task PreventMassrollbackAsync(
        IReducer<IReducerModel> reducer,
        ulong requestedRollBackSlot,
        ILogger logger
    )
    {
        string reducerName = GetTypeNameWithoutGenerics(reducer.GetType());

        // Create DbContext
        await using T dbContext = await DbContextFactory.CreateDbContextAsync();

        ulong reducerCurrentSlot = await dbContext.ReducerStates
            .AsNoTracking()
            .Where(rs => rs.Name == reducerName)
            .Select(rs => rs.Slot)
            .FirstOrDefaultAsync();

        // Only execute mass rollback prevention logic if the reducer has processed at least one block
        if (reducerCurrentSlot > 0)
        {
            ulong maxAdditionalRollbackSlots = 100 * 20;
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

    private Point GetReducerStartPoint(IReducer<IReducerModel> reducer)
    {
        string reducerName = GetTypeNameWithoutGenerics(reducer.GetType());

        // Get default start slot and hash from global configuration
        ulong defaultStartSlot = Configuration.GetValue<ulong?>("CardanoIndexStart:Slot")
            ?? throw new InvalidOperationException("Default StartSlot is not specified in the configuration.");
        string defaultStartHash = Configuration.GetValue<string>("CardanoIndexStart:Hash")
            ?? throw new InvalidOperationException("Default StartHash is not specified in the configuration.");

        // Get the configuration section for the specific reducer
        var reducerSection = Configuration.GetSection($"CardanoIndexReducers:{reducerName}");

        // Retrieve the StartSlot and StartHash for the reducer, or use defaults if not specified
        ulong startSlot = reducerSection.GetValue<ulong?>("StartSlot") ?? defaultStartSlot;
        string startHash = reducerSection.GetValue<string>("StartHash") ?? defaultStartHash;

        return new Point(startSlot, startHash);
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
                throw new NotImplementedException("gRPC connection type is not yet implemented."),

            _ => throw new InvalidOperationException("Invalid ConnectionType specified.")
        };
    }

    private static string GetTypeNameWithoutGenerics(Type type)
    {
        string typeName = type.Name;
        int genericCharIndex = typeName.IndexOf('`');
        if (genericCharIndex != -1)
        {
            typeName = typeName[..genericCharIndex];
        }
        return typeName;
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