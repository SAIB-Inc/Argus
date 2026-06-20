using Argus.Sync.EntityFramework.Postgres;
using Argus.Sync.Example.Data;
using Argus.Sync.Example.Services;
using Argus.Sync.Extensions;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddCardanoPostgresIndexer<TestDbContext>(builder.Configuration);
builder.Services.AddReducers(builder.Configuration);
builder.Services.AddHostedService<LiveSmokeMonitor>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

if (app.Configuration.GetValue("Example:Database:ApplyMigrations", true))
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    TestDbContext dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
    await dbContext.Database.MigrateAsync();
}

await app.RunAsync();
