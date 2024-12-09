using Argus.Sync.Data.Models;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCardanoIndexer<TestDbContext>(builder.Configuration);
builder.Services.AddReducers<TestDbContext, IReducerModel>([typeof(BlockTestReducer)]);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();


app.Run();