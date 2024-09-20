using System.Diagnostics;
using System.Drawing;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PallasDotnet.Models;
using Point = PallasDotnet.Models.Point;
namespace Argus.Sync.Workers;

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
        await Task.WhenAll(
            reducers.Select(
                reducer => StartReducerChainSyncAsync(reducer, stoppingToken)
            )
        );
    }

    protected async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        /*
            Support these features:
                Support Reducer Dependencies
                Massrollback prevention
        */
        ICardanoChainProvider chainProvider = GetCardanoChainProvider();
        Point intersection =  GetReducerStartPoint(reducer);

        await foreach (NextResponse response in chainProvider.StartChainSyncAsync(intersection, stoppingToken))
        {
            if (response.Action == NextResponseAction.Await) continue;

            Task responseTask = response.Action switch
            {
                NextResponseAction.RollForward => ProcessRollForwardAsync(response, reducer),
                NextResponseAction.RollBack =>  ProcessRollBackAsync(response, reducer),
                _ => throw new CriticalNodeException("Next response error received."),
            };

            await responseTask;
        }
    }

    protected async Task ProcessRollForwardAsync(NextResponse response, IReducer<IReducerModel> reducer)
    {
    }

    protected async Task ProcessRollBackAsync(NextResponse response, IReducer<IReducerModel> reducer)
    {
    }

    protected Point GetReducerStartPoint(IReducer<IReducerModel> reducer)
    {
        string reducerName = GetTypeNameWithoutGenerics(reducer.GetType());

        // Get default start slot and hash from global configuration
        ulong defaultStartSlot = configuration.GetValue<ulong?>("CardanoIndexStart:Slot")
            ?? throw new InvalidOperationException("Default StartSlot is not specified in the configuration.");
        string defaultStartHash = configuration.GetValue<string>("CardanoIndexStart:Hash")
            ?? throw new InvalidOperationException("Default StartHash is not specified in the configuration.");

        // Get the configuration section for the specific reducer
        var reducerSection = configuration.GetSection($"CardanoIndexReducers:{reducerName}");

        // Retrieve the StartSlot and StartHash for the reducer, or use defaults if not specified
        ulong startSlot = reducerSection.GetValue<ulong?>("StartSlot") ?? defaultStartSlot;
        string startHash = reducerSection.GetValue<string>("StartHash") ?? defaultStartHash;

        return new Point(startSlot, startHash);
    }

    protected ICardanoChainProvider GetCardanoChainProvider()
    {
        var config = configuration.GetSection("CardanoNodeConnection");
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