# Cardano.Sync

Cardano.Sync is a .NET library that simplifies interactions with the Cardano blockchain by providing an efficient indexing framework. Initially supporting PostgreSQL as the database backend, it processes block data into structured, queryable formats. This tool is designed for robust enterprise integration, with plans to introduce additional database backends in the future to broaden its applicability and flexibility.

```cs
builder.Services.AddCardanoIndexer<CardanoTestDbContext>(builder.Configuration);
builder.Services.AddSingleton<IReducer, TestReducer>();
```

```cs
using Cardano.Sync.Example.Data;
using Cardano.Sync.Reducers;
using PallasDotnet.Models;

namespace Cardano.Sync.Example.Reducers;

public class MyReducer : IReducer
{
    private readonly ILogger<MyReducer> _logger;
    
    public MyReducer(ILogger<MyReducer> logger)
    {
        _logger = logger;
    }

    public async Task RollForwardAsync(NextResponse response)
    {
        _logger.LogInformation("Processing new block at slot {slot}", response.Block.Slot);
        // Implement your logic here
    }

    public async Task RollBackwardAsync(NextResponse response)
    {
        _logger.LogInformation("Rollback at slot {slot}", response.Block.Slot);
        // Implement rollback logic here
    }
}
```
