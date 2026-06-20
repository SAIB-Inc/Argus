using Argus.Sync.Example.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.Infrastructure;

/// <summary>
/// Manages test database creation, configuration, and cleanup
/// </summary>
public class TestDatabaseManager : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;

    public TestDbContext DbContext { get; }
    public ServiceProvider ServiceProvider { get; }
    public string DatabaseName { get; }

    public TestDatabaseManager(ITestOutputHelper output)
    {
        _output = output;
        DatabaseName = $"argus_test_{Guid.NewGuid():N}";

        // Setup test configuration
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CardanoContext"] = $"Host=localhost;Database={DatabaseName};Username=postgres;Password=postgres;Port=4321",
                ["ConnectionStrings:CardanoContextSchema"] = "public"
            })
            .Build();

        // Setup DI container
        ServiceCollection services = new();
        _ = services.AddSingleton<IConfiguration>(configuration);
        _ = services.AddLogging(builder =>
        {
            _ = builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Warning)
                   .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        });

        // Add database context
        _ = services.AddDbContextFactory<TestDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("CardanoContext")));

        // Add reducers
        _ = services.AddTransient<Example.Reducers.BlockTestReducer>();
        _ = services.AddTransient<Example.Reducers.TransactionTestReducer>();

        ServiceProvider = services.BuildServiceProvider();

        // Create test database and apply migrations
        IDbContextFactory<TestDbContext> dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        DbContext = dbContextFactory.CreateDbContext();
        _ = DbContext.Database.EnsureCreated();

        _output.WriteLine($"Created test database: {DatabaseName}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Close all connections first
            await DbContext.Database.CloseConnectionAsync();
            await DbContext.DisposeAsync();

            // Dispose service provider to close any other connections
            await ServiceProvider.DisposeAsync();

            // Wait a moment for connections to fully close
            await Task.Delay(100);

            // Force terminate any remaining connections and drop database
            string connectionString = "Host=localhost;Database=postgres;Username=postgres;Password=postgres;Port=4321";
            using NpgsqlConnection connection = new(connectionString);
            await connection.OpenAsync();

            // First terminate any active connections to the test database
            using NpgsqlCommand terminateCommand = connection.CreateCommand();
            terminateCommand.CommandText = @"
                SELECT pg_terminate_backend(pg_stat_activity.pid)
                FROM pg_stat_activity
                WHERE pg_stat_activity.datname = @dbName
                  AND pid <> pg_backend_pid()";
            _ = terminateCommand.Parameters.AddWithValue("@dbName", DatabaseName);
            _ = await terminateCommand.ExecuteNonQueryAsync();

            // Small delay to ensure connections are terminated
            await Task.Delay(50);

            // Now drop the database using a helper to build the safe SQL command.
            // DROP DATABASE does not support parameterized database names in PostgreSQL,
            // but DatabaseName is a GUID generated internally (not user input), so this is safe.
            await DropDatabaseAsync(connection, DatabaseName);
            _output.WriteLine($"Deleted test database: {DatabaseName}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error cleaning up database: {ex.Message}");
        }

        GC.SuppressFinalize(this);
    }

    private static async Task DropDatabaseAsync(NpgsqlConnection connection, string databaseName)
    {
        // DROP DATABASE cannot use parameters or run inside PL/pgSQL functions.
        // DatabaseName is an internally-generated GUID (not user input), so interpolation is safe.
        using NpgsqlCommand dropCommand = connection.CreateCommand();
#pragma warning disable CA2100 // DatabaseName is a GUID generated in constructor, not user input
        dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
#pragma warning restore CA2100
        _ = await dropCommand.ExecuteNonQueryAsync();
    }
}