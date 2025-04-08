<div align="center">
 <h1 style="font-size: 5em;">Argus | Cardano Blockchain Indexer for .NET</h1>
</div>  

<div align="center" style="margin: 25px;">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="/assets/darkmodeAsset.png">
    <source media="(prefers-color-scheme: light)" srcset="/assets/lightmodeAsset.png">
    <img alt="Argus Logo" >
  </picture>
</div>

<div align="center">
  <img src="https://img.shields.io/github/forks/SAIB-Inc/Argus.svg?style=social" style="display: inline-block;">
  <img src="https://img.shields.io/github/stars/SAIB-Inc/Argus.svg?style=social" style="display: inline-block;">
  <img src="https://img.shields.io/badge/C%23-purple.svg" style="display: inline-block;">

  ![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)
  ![Contributors](https://img.shields.io/github/contributors/SAIB-Inc/Argus.svg?label=Contributors)
  ![Issues](https://img.shields.io/github/issues/SAIB-Inc/Argus.svg?label=Open%20Issues)
  ![Issues Closed](https://img.shields.io/github/issues-closed/SAIB-Inc/Argus.svg?label=Closed%20Issues)
  <a href="https://www.nuget.org/packages/Argus.Sync" style="display: inline-block; text-decoration: none; border: none;">
    <img src="https://img.shields.io/nuget/v/Argus.Sync.svg" alt="NuGet">
  </a>
</div>

## 📖 Overview

Argus is a .NET library that simplifies interactions with the Cardano blockchain by providing an efficient and easy to use indexing framework.
Initially supporting PostgreSQL as the database backend, it processes block data into structured, queryable formats.
This tool is designed for robust enterprise integration, with plans to introduce additional database backends in the future to broaden its applicability and flexibility.

🎥 **Video Tutorial**: For a detailed explanation, setup tutorial, and demo, be sure to check out [this video](https://x.com/clarkalesna/status/1859042521856532883)!

## 📑 Table of Contents

- [Key Features](#-key-features)
- [Quick Start](#-quick-start)
- [Installation](#-installation)
- [Configuration](#-configuration)
- [Usage Example](#-usage-example)
- [Dashboard Modes](#-dashboard-modes)
- [Rollback Support](#-rollback-support)
- [Technology Stack](#-technology-stack)
- [Roadmap](#-roadmap)
- [Contributing](#-contributing)
- [License](#-license)

## ✨ Key Features

- **Indexing**: Communicate with the Cardano blockchain to filter and retrieve required information, making it easy for users to store relevant data in a database.
- **Power**: Integrates with C# tools like LINQ, ASP.NET, and Entity Framework, enhancing the development experience for building and updating dApps.
- **C# Data Structures**: Serialize and deserialize Cardano's CBOR data into C# data types using Chrysalis.Cbor, allowing users to seamlessly utilize blockchain data.
- **Multiple Connection Types**: Support for Unix Socket and gRPC connections to Cardano nodes.
- **Advanced Logging**: Two logging modes - TUI (Terminal User Interface) for visual debugging and Plain Text for standard logging output.
- **Comprehensive Dashboard**: Full dashboard mode with real-time statistics or simplified sync progress tracking.
- **Rollback Support**: Enhanced rollback functionality with configurable options per reducer.
- **Resource Monitoring**: Built-in CPU and memory usage tracking for performance optimization.
- **Efficiency**: Boosts productivity by allowing users to create secure and powerful Cardano dApps or update existing ones easier and faster.
- **Customizable**: Create custom reducers tailored to your specific needs. You may also utilize the provided general reducers.

## 🚀 Quick Start

```bash
# Install via NuGet
dotnet add package Argus.Sync --version 0.3.0-alpha

# Add database dependencies
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# Set up your database
dotnet ef migrations add InitialMigration
dotnet ef database update

# Run your reducer
dotnet run -c Release
```

## 📦 Installation

To use Argus in your .NET project:

1. Install Argus via NuGet:  

    ```bash
    dotnet add package Argus.Sync --version 0.3.0-alpha
    ```

2. Install database dependencies:

    ```bash
    dotnet add package Microsoft.EntityFrameworkCore.Design
    dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
    ```

3. Run your PostgreSQL server instance.

## ⚙️ Configuration

Configure your appsettings.json file with the following sections:

### Database Connection

```json
"ConnectionStrings": {
  "CardanoContext": "Host=localhost;Database=dbName;Username=yourUsername;Password=yourPassword;Port=yourPort",
  "CardanoContextSchema": "cardanoindexer"
}
```

### Argus Connection Types

```json
"CardanoNodeConnection": {
  "ConnectionType": "UnixSocket", // Supported types: "UnixSocket", "gRPC"
  "UnixSocket": {
    "Path": "yourPath/node.socket"
  },
  "gRPC": {
    "Endpoint": "https://yourEndpoint",
    "ApiKey": "yourKey"
  },
  "NetworkMagic": 764824073,
  "MaxRollbackSlots": 1000,
  "RollbackBuffer": 10,
  "Slot": 139522569,
  "Hash": "3fd9925888302fca267c580d8fe6ebc923380d0b984523a1dfbefe88ef089b66"
}
```

### Reducer Starting Intersection

```json
"CardanoIndexStart": {
  "Slot": 139522569,
  "Hash": "3fd9925888302fca267c580d8fe6ebc923380d0b984523a1dfbefe88ef089b66"
},
"CardanoIndexReducers": {
  "TestReducer": {
    "StartSlot": 139522569,
    "StartHash": "3fd9925888302fca267c580d8fe6ebc923380d0b984523a1dfbefe88ef089b66"
  }
}
```

### Sync and Dashboard Configuration

```json
"Sync": {
  "Rollback": {
    "Enabled": false,
    "RollbackHash": "20a81db38339bf6ee9b1d7e22b22c0ac4d887d332bbf4f3005db4848cd647743",
    "RollbackSlot": 57371845,
    "Reducers": {
      "BlockTestReducer": {
        "Enabled": true,
        "RollbackHash": "20a81db38339bf6ee9b1d7e22b22c0ac4d887d332bbf4f3005db4848cd647743",
        "RollbackSlot": 57371845
      },
      "TransactionTestReducer": {
        "Enabled": false,
        "RollbackHash": "20a81db38339bf6ee9b1d7e22b22c0ac4d887d332bbf4f3005db4848cd647743",
        "RollbackSlot": 57371845
      }
    }
  },
  "Dashboard": {
    "TuiMode": true,
    "RefreshInterval": 5000,
    "DisplayType": "sync" // Options: "sync" or "full"
  },
  "State": {
    "ReducerStateSyncInterval": 5000
  }
}
```

### Smart Contract Related Info (Optional)
Argus includes general reducers. Add the corresponding configuration lines for the dApp reducers you plan to use:

```json
"JPGStoreMarketplaceV1ValidatorScriptHash": "c727443d77df6cff95dca383994f4c3024d03ff56b02ecc22b0f3f65", 
"SplashScriptHash": "9dee0659686c3ab807895c929e3284c11222affd710b09be690f924d", 
"MinswapScriptHash": "ea07b733d932129c378af627436e7cbc2ef0bf96e0036bb51b3bde6b", 
"SundaeSwapScriptHash": "e0302560ced2fdcbfcb2602697df970cd0d6a38f94b32703f51c312b"
```

### Create your models and DbContext

You can create custom models or use the provided general reducers:

Entity Class:  

```csharp
//TxBySlot.cs
public record TxBySlot(
  string Hash,
  ulong Slot,
  uint Index,
  byte[] RawCbor
) : IReducerModel;
```

Context Object:  

```csharp
//TxBySlotDbContext.cs
public interface ITxBySlotDbContext
{
  DbSet<TxBySlot> TxBySlot { get; }
}

public class TxBySlotDbContext
(
  DbContextOptions options,
  IConfiguration configuration
) : CardanoDbContext(options, configuration), ITxBySlotDbContext
{
  public DbSet<TxBySlot> TxBySlot => Set<TxBySlot>();

  override protected void OnModelCreating(ModelBuilder modelBuilder)
  {
      base.OnModelCreating(modelBuilder);

      modelBuilder.Entity<TxBySlot>(entity =>
      {
          entity.HasKey(e => new { e.Hash, e.Index, e.Slot });
      });
  }
}
```

### Migrate and update your database

```bash
dotnet ef migrations add <migrationName> 
dotnet ef database update
```

## 📝 Usage Example

```csharp
//Program.cs
builder.Services.AddCardanoIndexer<CardanoTestDbContext>(builder.Configuration); 
builder.Services.AddReducers<CardanoTestDbContext, IReducerModel>([typeof(TxBySlotReducer<>)]); 
```

```csharp
//TxBySlotReducer.cs
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Core;
using Chrysalis.Cbor;
using Chrysalis.Utils;
using Microsoft.EntityFrameworkCore;
using Block = Chrysalis.Cardano.Core.Block;

namespace Argus.Sync.Reducers;

public class TxBySlotReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<TxBySlot> where T : CardanoDbContext, ITxBySlotDbContext
{
    public async Task RollForwardAsync(Block block)
    {
      //Implement your rollforward logic
    }

    public async Task RollBackwardAsync(ulong slot)
    {
      //Implement your rollbackward logic
    }
}
```

## 📊 Dashboard Modes

Argus provides two types of dashboard for monitoring synchronization progress:

### Sync Progress Mode

Shows a simplified progress bar for each reducer with completion percentage and estimated time remaining. Enable with:

```json
"Sync": {
  "Dashboard": {
    "TuiMode": true,
    "DisplayType": "sync"
  }
}
```

### Full Dashboard Mode

Displays comprehensive information including:
- Overall sync progress
- Per-reducer sync progress
- System resource usage (CPU, memory)
- Thread and handle counts

Enable with:

```json
"Sync": {
  "Dashboard": {
    "TuiMode": true,
    "DisplayType": "full"
  }
}
```

For production environments or CI/CD pipelines, you can disable the TUI mode:

```json
"Sync": {
  "Dashboard": {
    "TuiMode": false
  }
}
```

## 🔄 Rollback Support

Argus provides enhanced rollback functionality, with two main operational modes:

### Standard Rollback

During normal operation, Argus will automatically handle chain reorganizations up to the configured `MaxRollbackSlots` depth. This ensures data consistency in case of blockchain forks or reorganizations.

### Dedicated Rollback Mode

For situations requiring explicit rollback to a specific point:

1. Enable rollback mode in your configuration:

```json
"Sync": {
  "Rollback": {
    "Enabled": true,
    "RollbackHash": "your_target_block_hash",
    "RollbackSlot": your_target_slot_number
  }
}
```

2. You can configure different rollback points for specific reducers:

```json
"Sync": {
  "Rollback": {
    "Reducers": {
      "YourReducerName": {
        "Enabled": true,
        "RollbackHash": "reducer_specific_hash",
        "RollbackSlot": reducer_specific_slot
      }
    }
  }
}
```

When rollback mode is enabled, Argus will:
1. Roll back to the specified point
2. Update reducer states
3. Complete the operation and terminate
4. You must then disable rollback mode to resume normal forward syncing

## ⚙️ Technology Stack

Argus leverages several key technologies:

- **Chrysalis.Cbor**: High-performance CBOR serialization/deserialization for Cardano data structures
- **Chrysalis.Network**: Native C# implementation of Ouroboros mini-protocols for Cardano node communication
- **Entity Framework Core**: ORM for database operations
- **Spectre.Console**: Rich terminal user interface for the dashboard visualizations

## 🛣️ Roadmap

- [x] **Expand Comprehensive Library Enhancement**: 
  - Expose blockchain data based upon the CDDL fields.
  - Implement common general reducers and common use-case dApp reducers.
  - Simplify installation and usage for developers through NuGet and a comprehensive tutorial.
- [ ] **Stability Enhancement and Performance Optimization**: 
  - Improve the stability and performance of Argus.
  - Create a performance report detailing benchmarks, optimizations, and testing results.
- [ ] **Official Website**: 
  - Expand and enhance Argus documentation by creating a website detailing its features and use cases.
  - Have a reviewer confirm the accuracy of the website and assess the clarity and ease of use of the tutorial.
- [ ] **Official Launch and Community Outreach**: 
  - Officially launch the Argus website and extensive documentation.
  - Hold community engagement events such as webinars.

## 🤝 Contributing

We welcome contributions! Please feel free to submit pull requests or open issues to improve Argus.

## 📄 License

Argus is licensed under the Apache 2.0 License - see the [LICENSE](LICENSE) file for details.