using System.Diagnostics;
using Cardano.Sync.Data;
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
    private readonly NodeClient _nodeClient = new();
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<CardanoIndexWorker<T>> _logger = logger;
    private readonly IDbContextFactory<T> _dbContextFactory = dbContextFactory;
    private readonly IEnumerable<IBlockReducer> _blockReducer = blockReducers;
    private readonly IEnumerable<ICoreReducer> _coreReducers = coreReducers;
    private readonly IEnumerable<IReducer> _reducers = reducers;
    private CardanoDbContext DbContext { get; set; } = null!;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DbContext = _dbContextFactory.CreateDbContext();

        var latestBlock = await DbContext.Blocks.OrderByDescending(b => b.Slot).FirstOrDefaultAsync(cancellationToken: stoppingToken);

        if (latestBlock is not null)
        {
            _configuration["CardanoIndexStartSlot"] = latestBlock.Slot.ToString();
            _configuration["CardanoIndexStartHash"] = latestBlock.Id;
        }

        var tip = await _nodeClient.ConnectAsync(_configuration.GetValue<string>("CardanoNodeSocketPath")!, _configuration.GetValue<ulong>("CardanoNetworkMagic"));
        _logger.Log(LogLevel.Information, "Connected to Cardano Node: {Tip}", tip);

        await _nodeClient.StartChainSyncAsync(new Point(
            _configuration.GetValue<ulong>("CardanoIndexStartSlot"),
            Hash.FromHex(_configuration.GetValue<string>("CardanoIndexStartHash")!)
        ));

        await GetChainSyncResponsesAsync(stoppingToken);
        await _nodeClient.DisconnectAsync();
    }


    private async Task GetChainSyncResponsesAsync(CancellationToken stoppingToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        void Handler(object? sender, ChainSyncNextResponseEventArgs e)
        {
            if (e.NextResponse.Action == NextResponseAction.Await) return;

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var response = e.NextResponse;
            _logger.Log(
                LogLevel.Information, "New Chain Event {Action}: {Slot} Block: {Block}",
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
                            reducerStopwatch.Start();
                            await reducer.RollForwardAsync(response);
                            reducerStopwatch.Stop();
                            _logger.Log(LogLevel.Information, "Processed RollForwardAsync[{}] in {ElapsedMilliseconds} ms", reducer.GetType(), reducerStopwatch.ElapsedMilliseconds);
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
                            _logger.Log(LogLevel.Information, "Processed RollBackwardAsync[{}] in {ElapsedMilliseconds} ms", reducer.GetType(), reducerStopwatch.ElapsedMilliseconds);
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

            Task.WhenAll(_coreReducers.Select(reducer => reducerAction(reducer, response))).Wait(stoppingToken);
            Task.WhenAll(_reducers.Select(reducer => reducerAction(reducer, response))).Wait(stoppingToken);
            Task.WhenAll(_blockReducer.Select(reducer => reducerAction(reducer, response))).Wait(stoppingToken);

            stopwatch.Stop();

            _logger.Log(
                LogLevel.Information,
                "Processed Chain Event {Action}: {Slot} Block: {Block} in {ElapsedMilliseconds} ms, Mem: {MemoryUsage} MB",
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

        _nodeClient.ChainSyncNextResponse += Handler;
        _nodeClient.Disconnected += DisconnectedHandler;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(100, stoppingToken);
            }
        }
        finally
        {
            _nodeClient.ChainSyncNextResponse -= Handler;
            _nodeClient.Disconnected -= DisconnectedHandler;
        }
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