using Argus.Sync.Example.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.Infrastructure;

/// <summary>
/// Manages test database creation, configuration, and cleanup
/// </summary>
public class TestDatabaseManager : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDatabaseName;
    private readonly ServiceProvider _serviceProvider;
    private readonly TestDbContext _dbContext;

    public TestDbContext DbContext => _dbContext;
    public ServiceProvider ServiceProvider => _serviceProvider;
    public string DatabaseName => _testDatabaseName;

    public TestDatabaseManager(ITestOutputHelper output)
    {
        _output = output;
        _testDatabaseName = $"argus_test_{Guid.NewGuid():N}";
        
        // Setup test configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CardanoContext"] = $"Host=localhost;Database={_testDatabaseName};Username=postgres;Password=postgres;Port=4321",
                ["ConnectionStrings:CardanoContextSchema"] = "public"
            })
            .Build();

        // Setup DI container
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        // Add database context
        services.AddDbContextFactory<TestDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("CardanoContext")));
        
        // Add reducers
        services.AddTransient<Argus.Sync.Example.Reducers.BlockTestReducer>();
        services.AddTransient<Argus.Sync.Example.Reducers.TransactionTestReducer>();
        
        _serviceProvider = services.BuildServiceProvider();
        
        // Create test database and apply migrations
        var dbContextFactory = _serviceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        _dbContext = dbContextFactory.CreateDbContext();
        _dbContext.Database.EnsureCreated();
        
        _output.WriteLine($"Created test database: {_testDatabaseName}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Close all connections first
            await _dbContext.Database.CloseConnectionAsync();
            await _dbContext.DisposeAsync();
            
            // Dispose service provider to close any other connections
            await _serviceProvider.DisposeAsync();
            
            // Wait a moment for connections to fully close
            await Task.Delay(100);
            
            // Force terminate any remaining connections and drop database
            var connectionString = "Host=localhost;Database=postgres;Username=postgres;Password=postgres;Port=4321";
            using var connection = new Npgsql.NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            
            // First terminate any active connections to the test database
            using var terminateCommand = connection.CreateCommand();
            terminateCommand.CommandText = $@"
                SELECT pg_terminate_backend(pg_stat_activity.pid)
                FROM pg_stat_activity
                WHERE pg_stat_activity.datname = '{_testDatabaseName}'
                  AND pid <> pg_backend_pid()";
            await terminateCommand.ExecuteNonQueryAsync();
            
            // Small delay to ensure connections are terminated
            await Task.Delay(50);
            
            // Now drop the database
            using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{_testDatabaseName}\"";
            await dropCommand.ExecuteNonQueryAsync();
            _output.WriteLine($"Deleted test database: {_testDatabaseName}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error cleaning up database: {ex.Message}");
        }
        
        GC.SuppressFinalize(this);
    }
}