using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
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

    private readonly PeriodicTimer _dashboardTimer = new(TimeSpan.FromSeconds(configuration.GetValue("Dashboard:RefreshIntervalSeconds", 5)));
    private readonly PeriodicTimer _dbSyncTimer = new(TimeSpan.FromSeconds(configuration.GetValue("Database:DbSyncIntervalSeconds", 10)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        _ = Task.Run(InitDashboardAsync, stoppingToken);
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

    private async Task InitDashboardAsync()
    {
        _ = Task.Run(StartSyncProgressTrackerAsync);
        await Task.CompletedTask;
    }

    private async Task StartSyncProgressTrackerAsync()
    {
        await Task.Delay(1000);

        ICardanoChainProvider chainProvider = GetCardanoChainProvider();
        await AnsiConsole.Progress()
            .Columns(
            [
                new TaskDescriptionColumn(),
                    new ProgressBarColumn()
                    {
                        CompletedStyle = Color.GreenYellow
                    },
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

                while (await _dashboardTimer.WaitForNextTickAsync())
                {
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
        while (!stoppingToken.IsCancellationRequested && await _dbSyncTimer.WaitForNextTickAsync(stoppingToken))
        {
            await UpdateReducerStatesAsync(stoppingToken);
        }
    }
}