using Argus.Sync.Data.Models;
using Argus.Sync.Example.Data;
using Argus.Sync.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCardanoIndexer<CardanoTestDbContext>(builder.Configuration);
builder.Services.AddReducers<CardanoTestDbContext, IReducerModel>([]);
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();
