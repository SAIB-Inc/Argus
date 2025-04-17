using Argus.Sync.Data.Models;
using Argus.Sync.Example.Api;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCardanoIndexer<TestDbContext>(builder.Configuration);
builder.Services.AddReducers<TestDbContext, IReducerModel>(builder.Configuration);
builder.Services.AddScoped<SundaeSwapService>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapGet("/sundae/prices/latest", async (SundaeSwapService sundaeSwapService, int limit = 10, string? pair = null) =>
{
    var result = await sundaeSwapService.FetchPricesAsync(limit, pair);
    return Results.Ok(result);
});

app.Run();