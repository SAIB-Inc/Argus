using Argus.Sync.Data.Models;
using Argus.Sync.Example.Data;
using Argus.Sync.Extensions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCardanoIndexer<TestDbContext>(builder.Configuration);
builder.Services.AddReducers<TestDbContext, IReducerModel>(builder.Configuration);

var app = builder.Build();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}

app.Run();