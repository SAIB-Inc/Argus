using Argus.Sync.Data.Models;
using Argus.Sync.Example.Api;
using Argus.Sync.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCardanoIndexer<AppDbContext>(builder.Configuration);
builder.Services.AddReducers<AppDbContext, IReducerModel>(builder.Configuration);
builder.Services.AddScoped<SundaeSwapService>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapGet("/sundae/prices", async (SundaeSwapService sundaeSwapService, int limit = 10, string? pair = null) =>
{
    var result = await sundaeSwapService.FetchPricesAsync(limit, pair);
    return Results.Ok(result);
});

app.Run();