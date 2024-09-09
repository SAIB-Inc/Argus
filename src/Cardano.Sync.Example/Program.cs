using Cardano.Sync;
using Cardano.Sync.Data.Models;
using Cardano.Sync.Example.Data;
using Cardano.Sync.Reducers;
using Cardano.Sync.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCardanoIndexer<CardanoTestDbContext>(builder.Configuration);
builder.Services.AddSingleton<IReducer<IReducerModel>, BlockReducer<CardanoTestDbContext>>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();
