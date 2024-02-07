using System.Diagnostics;
using Cardano.Sync.Data;
using Cardano.Sync.Data.Models;
using Cardano.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PallasDotnet;
using PallasDotnet.Models;

namespace Cardano.Sync.Workers;

public class CardanoIndexWorker<T>(
    IConfiguration configuration,
    ILogger<CardanoIndexWorker<T>> logger,
    IDbContextFactory<T> dbContextFactory,
    IEnumerable<IBlockReducer> blockReducers,
    IEnumerable<ICoreReducer> coreReducers,
    IEnumerable<IReducer> reducers
) : BackgroundService where T : CardanoDbContext
{
    // private readonly NodeClient _nodeClient = new();
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<CardanoIndexWorker<T>> _logger = logger;
    private readonly IDbContextFactory<T> _dbContextFactory = dbContextFactory;
    private readonly IEnumerable<IBlockReducer> _blockReducer = blockReducers;
    private readonly IEnumerable<ICoreReducer> _coreReducers = coreReducers;
    private readonly IEnumerable<IReducer> _reducers = reducers;
    private CardanoDbContext DbContext { get; set; } = null!;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IList<IReducer> reducers = [.. _blockReducer, .. _coreReducers, .. _reducers];

        // ChainSync all reducers in parallel Task.WhenAll
        await Task.WhenAll(reducers.Select(reducer => ChainSyncReducerAsync(reducer, stoppingToken)));

        // var latestBlock = await DbContext.Blocks.OrderByDescending(b => b.Slot).FirstOrDefaultAsync(cancellationToken: stoppingToken);

        // if (latestBlock is not null)
        // {
        //     _configuration["CardanoIndexStartSlot"] = latestBlock.Slot.ToString();
        //     _configuration["CardanoIndexStartHash"] = latestBlock.Id;
        // }

        // var tip = await _nodeClient.ConnectAsync(_configuration.GetValue<string>("CardanoNodeSocketPath")!, _configuration.GetValue<ulong>("CardanoNetworkMagic"));
        // _logger.Log(LogLevel.Information, "Connected to Cardano Node: {Tip}", tip);

        // await _nodeClient.StartChainSyncAsync(new Point(
        //     _configuration.GetValue<ulong>("CardanoIndexStartSlot"),
        //     Hash.FromHex(_configuration.GetValue<string>("CardanoIndexStartHash")!)
        // ));

        // await GetChainSyncResponsesAsync(stoppingToken);
        // await _nodeClient.DisconnectAsync();
    }

    private async Task ChainSyncReducerAsync(IReducer reducer, CancellationToken stoppingToken)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var nodeClient = new NodeClient();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        void Handler(object? sender, ChainSyncNextResponseEventArgs e)
        {
            if (e.NextResponse.Action == NextResponseAction.Await) return;

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var response = e.NextResponse;
            _logger.Log(
                LogLevel.Information, "[{reducer}]: New Chain Event {Action}: {Slot} Block: {Block}",
                CardanoIndexWorker<T>.GetTypeNameWithoutGenerics(reducer.GetType()),
                response.Action,
                response.Block.Slot,
                response.Block.Number
            );

            var actionMethodMap = new Dictionary<NextResponseAction, Func<IReducer, NextResponse, Task>>
            {
                { NextResponseAction.RollForward, async (reducer, response) =>
                    {
                        try
                        {
                            var reducerStopwatch = new Stopwatch();

                            var reducerDependencies = ReducerDependencyResolver.GetReducerDependencies(reducer.GetType());

                            while (true)
                            {
                                var canProceed = true;
                                foreach (var dependency in reducerDependencies)
                                {
                                    var dependencyName = GetTypeNameWithoutGenerics(dependency);
                                    var dependencyState = await dbContext.ReducerStates.AsNoTracking().FirstOrDefaultAsync(rs => rs.Name == dependencyName, stoppingToken); // Use cancellation token here

                                    if (dependencyState == null || dependencyState.Slot < response.Block.Slot)
                                    {
                                        _logger.Log(LogLevel.Information, "[{Reducer}]: Waiting for dependency {Dependency} Slot {depdencySlot} < {currentSlot}", 
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
                            _logger.Log(LogLevel.Information, "Processed RollForwardAsync[{}] in {ElapsedMilliseconds} ms", GetTypeNameWithoutGenerics(reducer.GetType()), reducerStopwatch.ElapsedMilliseconds);
                        }
                        catch(Exception ex)
                        {
                            _logger.Log(LogLevel.Error, ex, "Error in RollForwardAsync");
                            Environment.Exit(1);
                        }
                    }
                },
                {
                    NextResponseAction.RollBack, async (reducer, response) =>
                    {
                        try
                        {
                            var reducerStopwatch = new Stopwatch();
                            reducerStopwatch.Start();
                            await reducer.RollBackwardAsync(response);
                            reducerStopwatch.Stop();
                            _logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{}] in {ElapsedMilliseconds} ms", CardanoIndexWorker<T>.GetTypeNameWithoutGenerics(reducer.GetType()), reducerStopwatch.ElapsedMilliseconds);
                        }
                        catch(Exception ex)
                        {
                            _logger.Log(LogLevel.Error, ex, "Error in RollBackwardAsync");
                            Environment.Exit(1);
                        }
                    }
                }
            };

            var reducerAction = actionMethodMap[response.Action];

            reducerAction(reducer, response).Wait(stoppingToken);

            Task.Run(async () =>
            {
                var reducerState = await dbContext.ReducerStates.FirstOrDefaultAsync(rs => rs.Name == GetTypeNameWithoutGenerics(reducer.GetType()));

                if (reducerState is null)
                {
                    dbContext.ReducerStates.Add(new()
                    {
                        Name = GetTypeNameWithoutGenerics(reducer.GetType()),
                        Slot = response.Block.Slot
                    });
                }
                else
                {
                    reducerState.Slot = response.Block.Slot;
                }

                await dbContext.SaveChangesAsync();
            }, stoppingToken).Wait(stoppingToken);


            stopwatch.Stop();

            _logger.Log(
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
        var latestBlock = await dbContext.Blocks.OrderByDescending(b => b.Slot).FirstOrDefaultAsync(cancellationToken: stoppingToken);

        if (latestBlock is not null)
        {
            _configuration["CardanoIndexStartSlot"] = latestBlock.Slot.ToString();
            _configuration["CardanoIndexStartHash"] = latestBlock.Id;
        }

        var tip = await nodeClient.ConnectAsync(_configuration.GetValue<string>("CardanoNodeSocketPath")!, _configuration.GetValue<ulong>("CardanoNetworkMagic"));
        await nodeClient.StartChainSyncAsync(new Point(
            _configuration.GetValue<ulong>("CardanoIndexStartSlot"),
            Hash.FromHex(_configuration.GetValue<string>("CardanoIndexStartHash")!)
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
        var typeName = type.Name;
        var genericCharIndex = typeName.IndexOf('`');
        if (genericCharIndex != -1)
        {
            typeName = typeName[..genericCharIndex];
        }
        return typeName;
    }


    // private async Task GetChainSyncResponsesAsync(CancellationToken stoppingToken)
    // {
    //     var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

    //     void Handler(object? sender, ChainSyncNextResponseEventArgs e)
    //     {
    //         if (e.NextResponse.Action == NextResponseAction.Await) return;

    //         var stopwatch = new Stopwatch();
    //         stopwatch.Start();

    //         var response = e.NextResponse;
    //         _logger.Log(
    //             LogLevel.Information, "New Chain Event {Action}: {Slot} Block: {Block}",
    //             response.Action,
    //             response.Block.Slot,
    //             response.Block.Number
    //         );

    //         var actionMethodMap = new Dictionary<NextResponseAction, Func<IReducer, NextResponse, Task>>
    //         {
    //             { NextResponseAction.RollForward, async (reducer, response) =>
    //                 {
    //                     try
    //                     {
    //                         var reducerStopwatch = new Stopwatch();
    //                         reducerStopwatch.Start();
    //                         await reducer.RollForwardAsync(response);
    //                         reducerStopwatch.Stop();
    //                         _logger.Log(LogLevel.Information, "Processed RollForwardAsync[{}] in {ElapsedMilliseconds} ms", reducer.GetType(), reducerStopwatch.ElapsedMilliseconds);
    //                     }
    //                     catch(Exception ex)
    //                     {
    //                         _logger.Log(LogLevel.Error, ex, "Error in RollForwardAsync");
    //                         Environment.Exit(1);
    //                     }
    //                 }
    //             },
    //             {
    //                 NextResponseAction.RollBack, async (reducer, response) =>
    //                 {
    //                     try
    //                     {
    //                         var reducerStopwatch = new Stopwatch();
    //                         reducerStopwatch.Start();
    //                         await reducer.RollBackwardAsync(response);
    //                         reducerStopwatch.Stop();
    //                         _logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{}] in {ElapsedMilliseconds} ms", reducer.GetType(), reducerStopwatch.ElapsedMilliseconds);
    //                     }
    //                     catch(Exception ex)
    //                     {
    //                         _logger.Log(LogLevel.Error, ex, "Error in RollBackwardAsync");
    //                         Environment.Exit(1);
    //                     }
    //                 }
    //             }
    //         };

    //         var reducerAction = actionMethodMap[response.Action];

    //         Task.WhenAll(_coreReducers.Select(reducer => reducerAction(reducer, response))).Wait(stoppingToken);
    //         Task.WhenAll(_reducers.Select(reducer => reducerAction(reducer, response))).Wait(stoppingToken);
    //         Task.WhenAll(_blockReducer.Select(reducer => reducerAction(reducer, response))).Wait(stoppingToken);

    //         stopwatch.Stop();

    //         _logger.Log(
    //             LogLevel.Information,
    //             "Processed Chain Event {Action}: {Slot} Block: {Block} in {ElapsedMilliseconds} ms, Mem: {MemoryUsage} MB",
    //             response.Action,
    //             response.Block.Slot,
    //             response.Block.Number,
    //             stopwatch.ElapsedMilliseconds,
    //             Math.Round(GetCurrentMemoryUsageInMB(), 2)
    //         );
    //     }

    //     void DisconnectedHandler(object? sender, EventArgs e)
    //     {
    //         linkedCts.Cancel();
    //     }

    //     _nodeClient.ChainSyncNextResponse += Handler;
    //     _nodeClient.Disconnected += DisconnectedHandler;

    //     try
    //     {
    //         while (!stoppingToken.IsCancellationRequested)
    //         {
    //             await Task.Delay(100, stoppingToken);
    //         }
    //     }
    //     finally
    //     {
    //         _nodeClient.ChainSyncNextResponse -= Handler;
    //         _nodeClient.Disconnected -= DisconnectedHandler;
    //     }
    // }

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