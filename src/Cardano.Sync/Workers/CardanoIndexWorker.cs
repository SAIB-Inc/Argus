using System.Diagnostics;
using System.Text.Json;
using Cardano.Sync.Data;
using Cardano.Sync.Data.Models;
using Cardano.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PallasDotnet;
using PallasDotnet.Models;
using BlockEntity = Cardano.Sync.Data.Models.Block;
namespace Cardano.Sync.Workers;

public class CriticalNodeException(string message) : Exception(message) { }

public class CardanoIndexWorker<T>(
    IConfiguration configuration,
    ILogger<CardanoIndexWorker<T>> logger,
    IDbContextFactory<T> dbContextFactory,
    IEnumerable<IReducer<IReducerModel>> reducers
) : BackgroundService where T : CardanoDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IList<IReducer<IReducerModel>> combinedReducers = [.. reducers];
        Console.WriteLine(JsonSerializer.Serialize(combinedReducers));
        await Task.WhenAll(combinedReducers.Select(reducer => ChainSyncReducerAsync(reducer, stoppingToken)));
    }

    private async Task ChainSyncReducerAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        using T dbContext = dbContextFactory.CreateDbContext();
        NodeClient nodeClient = new();
        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        void Handler(object? sender, ChainSyncNextResponseEventArgs e)
        {
            if (e.NextResponse.Action == NextResponseAction.Await) return;

            Stopwatch stopwatch = new();
            stopwatch.Start();

            NextResponse response = e.NextResponse;
            logger.Log(
                LogLevel.Information, "[{reducer}]: New Chain Event {Action}: {Slot} Block: {Block}",
                CardanoIndexWorker<T>.GetTypeNameWithoutGenerics(reducer.GetType()),
                response.Action,
                response.Block.Slot,
                response.Block.Number
            );

            Dictionary<NextResponseAction, Func<IReducer<IReducerModel>, NextResponse, Task>> actionMethodMap = new()
            {
                { NextResponseAction.RollForward, async (reducer, response) =>
                    {
                        try
                        {
                            Stopwatch reducerStopwatch = new();

                            Type[] reducerDependencies = ReducerDependencyResolver.GetReducerDependencies(reducer.GetType());

                            while (true)
                            {
                                bool canProceed = true;
                                foreach (Type dependency in reducerDependencies)
                                {
                                    string dependencyName = GetTypeNameWithoutGenerics(dependency);
                                    ReducerState? dependencyState = await dbContext.ReducerStates.AsNoTracking().FirstOrDefaultAsync(rs => rs.Name == dependencyName, stoppingToken); // Use cancellation token here

                                    if (dependencyState == null || dependencyState.Slot < response.Block.Slot)
                                    {
                                        logger.Log(LogLevel.Information, "[{Reducer}]: Waiting for dependency {Dependency} Slot {depdencySlot} < {currentSlot}",
                                            GetTypeNameWithoutGenerics(reducer.GetType()),
                                            dependencyName,
                                            dependencyState?.Slot,
                                            response.Block.Slot
                                        );
                                        canProceed = false;
                                        break; // Break as soon as one dependency is not ready
                                    }
                                }

                                if (canProceed) break; // Exit the loop if all dependencies are caught up

                                await Task.Delay(1000, stoppingToken); // Wait for a bit before checking again
                            }

                            await reducer.RollForwardAsync(response);
                            reducerStopwatch.Stop();
                            logger.Log(LogLevel.Information, "Processed RollForwardAsync[{}] in {ElapsedMilliseconds} ms", GetTypeNameWithoutGenerics(reducer.GetType()), reducerStopwatch.ElapsedMilliseconds);
                        }
                        catch(Exception ex)
                        {
                            logger.Log(LogLevel.Error, ex, "Error in RollForwardAsync");
                            Environment.Exit(1);
                        }
                    }
                },
                {
                    NextResponseAction.RollBack, async (reducer, response) =>
                    {
                        try
                        {
                            string reducerName = GetTypeNameWithoutGenerics(reducer.GetType());
                            ulong reducerCurrentSlot = dbContext.ReducerStates
                                .AsNoTracking()
                                .Where(rs => rs.Name == reducerName)
                                .Select(rs => rs.Slot)
                                .FirstOrDefault();

                            // Only execute mass rollback prevention logic if the reducer has processed at least one block
                            if (reducerCurrentSlot > 0)
                            {
                                ulong maxAdditionalRollbackSlots = 100 * 20;
                                ulong requestedRollBackSlot = response.Block.Slot;
                                if (reducerCurrentSlot - requestedRollBackSlot > maxAdditionalRollbackSlots)
                                {
                                    logger.Log(
                                        LogLevel.Error,
                                        "RollBackwardAsync[{}] Requested RollBack Slot {requestedRollBackSlot} is more than {maxAdditionalRollbackSlots} slots behind current slot {reducerCurrentSlot}.",
                                        reducerName,
                                        requestedRollBackSlot,
                                        maxAdditionalRollbackSlots,
                                        reducerCurrentSlot
                                    );

                                    throw new CriticalNodeException("Rollback, Critical Error, Aborting");
                                }
                            }

                            Stopwatch reducerStopwatch = new();
                            reducerStopwatch.Start();
                            await reducer.RollBackwardAsync(response);
                            reducerStopwatch.Stop();
                            logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{}] in {ElapsedMilliseconds} ms", reducerName, reducerStopwatch.ElapsedMilliseconds);
                        }
                        catch(Exception ex)
                        {
                            logger.Log(LogLevel.Error, ex, "Error in RollBackwardAsync");
                            Environment.Exit(1);
                        }
                    }
                }
            };

            Func<IReducer<IReducerModel>, NextResponse, Task> reducerAction = actionMethodMap[response.Action];

            reducerAction(reducer, response).Wait(stoppingToken);

            Task.Run(async () =>
            {
                ReducerState? reducerState = await dbContext.ReducerStates.FirstOrDefaultAsync(rs => rs.Name == GetTypeNameWithoutGenerics(reducer.GetType()));

                if (reducerState is null)
                {
                    dbContext.ReducerStates.Add(new()
                    {
                        Name = GetTypeNameWithoutGenerics(reducer.GetType()),
                        Slot = response.Block.Slot,
                        Hash = response.Block.Hash.ToHex()
                    });
                }
                else
                {
                    reducerState.Slot = response.Block.Slot;
                    reducerState.Hash = response.Block.Hash.ToHex();
                }

                await dbContext.SaveChangesAsync();
            }, stoppingToken).Wait(stoppingToken);


            stopwatch.Stop();

            logger.Log(
                LogLevel.Information,
                "[{reducer}]: Processed Chain Event {Action}: {Slot} Block: {Block} in {ElapsedMilliseconds} ms, Mem: {MemoryUsage} MB",
                CardanoIndexWorker<T>.GetTypeNameWithoutGenerics(reducer.GetType()),
                response.Action,
                response.Block.Slot,
                response.Block.Number,
                stopwatch.ElapsedMilliseconds,
                Math.Round(GetCurrentMemoryUsageInMB(), 2)
            );
        }

        void DisconnectedHandler(object? sender, EventArgs e)
        {
            linkedCts.Cancel();
        }

        nodeClient.ChainSyncNextResponse += Handler;
        nodeClient.Disconnected += DisconnectedHandler;

        // Reducer specific start slot and hash
        ulong startSlot = configuration.GetValue<ulong>($"CardanoIndexStartSlot_{GetTypeNameWithoutGenerics(reducer.GetType())}");
        string? startHash = configuration.GetValue<string>($"CardanoIndexStartHash_{GetTypeNameWithoutGenerics(reducer.GetType())}");

        // Fallback to global start slot and hash
        if (startSlot == 0 && startHash is null)
        {
            startSlot = configuration.GetValue<ulong>("CardanoIndexStartSlot");
            startHash = configuration.GetValue<string>("CardanoIndexStartHash");
        }

        // Use educer state from database if available
        ReducerState? reducerState = await dbContext.ReducerStates
            .FirstOrDefaultAsync(rs => rs.Name == GetTypeNameWithoutGenerics(reducer.GetType()), cancellationToken: stoppingToken);

        if (reducerState is not null)
        {
            startSlot = reducerState.Slot;
            startHash = reducerState.Hash;
        }

        Point tip = await nodeClient.ConnectAsync(configuration.GetValue<string>("CardanoNodeSocketPath")!, configuration.GetValue<ulong>("CardanoNetworkMagic"));
        await nodeClient.StartChainSyncAsync(new(
            startSlot,
            Hash.FromHex(startHash!)
        ));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(100, stoppingToken);
            }
        }
        finally
        {
            nodeClient.ChainSyncNextResponse -= Handler;
            nodeClient.Disconnected -= DisconnectedHandler;
        }
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

    public static double GetCurrentMemoryUsageInMB()
    {
        Process currentProcess = Process.GetCurrentProcess();

        // Getting the physical memory usage of the current process in bytes
        long memoryUsed = currentProcess.WorkingSet64;

        // Convert to megabytes for easier reading
        double memoryUsedMb = memoryUsed / 1024.0 / 1024.0;

        return memoryUsedMb;
    }
}