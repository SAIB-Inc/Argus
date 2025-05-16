<div align="center">
  <img src="assets/argus.png" alt="Argus Logo" width="100%" />
</div>

<div align="center">
  <img src="https://img.shields.io/github/forks/SAIB-Inc/Argus.svg?style=social">
  <img src="https://img.shields.io/github/stars/SAIB-Inc/Argus.svg?style=social">
  <img src="https://img.shields.io/badge/C%23-purple.svg">

![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)
![Contributors](https://img.shields.io/github/contributors/SAIB-Inc/Argus.svg?label=Contributors)
![Issues](https://img.shields.io/github/issues/SAIB-Inc/Argus.svg?label=Open%20Issues)
![Issues Closed](https://img.shields.io/github/issues-closed/SAIB-Inc/Argus.svg?label=Closed%20Issues)
<a href="https://www.nuget.org/packages/Argus.Sync">
<img src="https://img.shields.io/nuget/v/Argus.Sync.svg" alt="NuGet">
</a>

</div>

## 📖 Overview

Argus is a .NET library that simplifies interactions with the Cardano blockchain by providing an efficient indexing framework. It processes block data into structured, queryable formats stored in a database, making blockchain data easier to work with for .NET developers. Initially supporting PostgreSQL as the database backend, Argus is designed for robust enterprise integration with plans to introduce additional database backends in the future.

🎥 **Video Tutorial**: For a detailed explanation and demo, check out [this video](https://x.com/clarkalesna/status/1859042521856532883)!

## 📑 Table of Contents

- [What is Cardano?](#-what-is-cardano)
- [What is Argus?](#-what-is-argus)
- [Core Concepts](#-core-concepts)
- [Key Features](#-key-features)
- [Installation and Setup](#-installation-and-setup)
- [Understanding Reducers](#-understanding-reducers)
- [Configuration](#-configuration)
- [Advanced Use Cases](#-advanced-use-cases)
- [Monitoring](#-monitoring)
- [Rollback Support](#-rollback-support)
- [Technology Stack](#-technology-stack)
- [Common Use Cases](#-common-use-cases)
- [Roadmap](#-roadmap)
- [Contributing](#-contributing)
- [License](#-license)

## 🔍 What is Argus?

Argus is a blockchain indexer - a tool that reads data from the blockchain, processes it according to defined rules, and stores it in a structured database. This makes blockchain data more accessible and queryable for application development.

```
┌────────────────┐     ┌─────────────┐     ┌────────────────┐
│                │     │             │     │                │
│ Cardano        │────▶│    Argus    │────▶│   Database     │
│ Blockchain     │     │  (Indexer)  │     │  (PostgreSQL)  │
│                │     │             │     │                │
└────────────────┘     └─────────────┘     └────────────────┘
       Raw Data          Processing          Structured Data
```

**Why use Argus?**

- **Simplified Blockchain Access**: Interact with Cardano blockchain data using familiar C# and .NET tools
- **Focused Data Processing**: Extract only the data your application needs
- **Database Integration**: Store processed blockchain data in a relational database for easy querying
- **Enterprise-Ready**: Built with reliability and performance in mind for business applications

## 🧩 Core Concepts

Argus is built around several key components that work together to index blockchain data:

```
┌─────────────────┐    ┌─────────────────┐    ┌──────────────────┐
│  Cardano Node   │──▶ │  Chain Provider │──▶ │CardanoIndexWorker│
└─────────────────┘    └─────────────────┘    └────────┬─────────┘
                                                      ┌─┴─┐
                                            ┌─────────┤   ├─────────┐
                                            ▼         │   │         ▼
                                     ┌────────────┐   │   │   ┌────────────┐
                                     │  Reducer 1 │◀──┘   └──▶│  Reducer 2 │
                                     └────┬───────┘           └────┬───────┘
                                          │                        │
                                          ▼                        ▼
                                    ┌─────────────────────────────────────┐
                                    │            Database                 │
                                    └─────────────────────────────────────┘
```

### Reducers

Reducers are the heart of Argus. A reducer is responsible for processing blockchain data and transforming it into application-specific models that are stored in your database.

Each reducer implements two main methods:

- **RollForwardAsync**: Processes new blocks as they arrive on the chain
- **RollBackwardAsync**: Handles blockchain reorganizations by reverting data changes

Reducers can be simple (e.g., storing all blocks) or complex (e.g., tracking specific smart contract interactions).

### Chain Providers

Chain providers connect Argus to the Cardano blockchain. Argus supports two connection types:

- **Unix Socket Connection** (N2CProvider): Direct connection to a local Cardano node
- **gRPC Connection** (U5CProvider): Remote connection to a Cardano node service

### Worker

The CardanoIndexWorker manages the blockchain synchronization process:

- Coordinates multiple reducers
- Connects to the Cardano node
- Handles block processing and rollbacks
- Provides monitoring and dashboard functionality

## ✨ Key Features

- **Customizable Reducers**: Define exactly how blockchain data should be processed and stored

  - Specify which data to extract from blocks and transactions
  - Create application-specific models that match your business requirements
  - Implement custom business logic for processing blockchain events

- **Flexible Connectivity Options**: Connect to Cardano in the way that suits you best

  - **Unix Socket**: Direct connection to a local Cardano node
  - **gRPC**: Remote connection to a Cardano node service

- **Robust Rollback Handling**: Ensure data consistency when blockchain reorganizations occur

  - Automatic handling of chain reorganizations (rollbacks)
  - Configurable rollback depth protection
  - Dedicated rollback mode for manual point-in-time recovery

- **Comprehensive Monitoring**: Keep track of indexing progress

  - Visual Terminal UI dashboard with real-time statistics
  - Plain text logging for production environments
  - Resource monitoring (CPU, memory) for performance optimization

- **Developer-Friendly Integration**: Built for .NET developers
  - Full Entity Framework Core integration
  - LINQ-compatible data access
  - Easy to incorporate into ASP.NET applications

## 📦 Installation and Setup

Setting up Argus involves the following steps:

```
┌────────────┐     ┌────────────────┐     ┌───────────────┐    ┌────────────────┐
│ 1. Install │────▶│ 2. Define Data │────▶│ 3. Implement  │───▶│ 4. Configure   │
│ Packages   │     │ Models & DB    │     │ Reducers      │    │ Application    │
└────────────┘     └────────────────┘     └───────────────┘    └────────────────┘
                                                                        │
                                                                        ▼
┌────────────────┐    ┌────────────────┐     ┌───────────────┐    ┌────────────────┐
│ 8. Monitor     │◀───│ 7. Run Your    │◀────│ 6. Apply DB   │◀───│ 5. Register    │
│ Synchronization│    │ Application    │     │ Migrations    │    │ Services       │
└────────────────┘    └────────────────┘     └───────────────┘    └────────────────┘
```

### 1. Install Required Packages

Add Argus and its dependencies to your .NET project:

```bash
dotnet add package Argus.Sync --version 0.3.1-alpha
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

### 2. Define Your Data Models

Create model classes that represent the blockchain data you want to store:

```csharp
// BlockInfo.cs
using Argus.Sync.Data.Models;

public record BlockInfo(
    string Hash,       // Block hash
    ulong Number,      // Block number
    ulong Slot,        // Block slot number
    DateTime CreatedAt // Timestamp for when the record was created
) : IReducerModel;
```

Then create a database context to manage these models:

```csharp
// MyDbContext.cs
using Argus.Sync.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

public interface IMyDbContext
{
    DbSet<BlockInfo> Blocks { get; }
}

public class MyDbContext(
    DbContextOptions options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration), IMyDbContext
{
    public DbSet<BlockInfo> Blocks => Set<BlockInfo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BlockInfo>(entity =>
        {
            entity.HasKey(e => e.Hash);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
        });
    }
}
```

### 3. Implement Your Reducers

Create reducers that process blockchain data according to your application's needs:

```csharp
// BlockReducer.cs
using Argus.Sync.Data;
using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Chrysalis.Cardano.Core;

public class BlockReducer(IDbContextFactory<MyDbContext> dbContextFactory)
    : IReducer<BlockInfo>
{
    public async Task RollForwardAsync(Block block)
    {
        // Extract block data
        string hash = block.Header().Hash();
        ulong number = block.Header().HeaderBody().BlockNumber();
        ulong slot = block.Header().HeaderBody().Slot();

        // Store in database
        using var db = dbContextFactory.CreateDbContext();
        db.Blocks.Add(new BlockInfo(hash, number, slot, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    public async Task RollBackwardAsync(ulong slot)
    {
        // Remove any blocks at or after the rollback slot
        using var db = dbContextFactory.CreateDbContext();
        db.Blocks.RemoveRange(
            db.Blocks.Where(b => b.Slot >= slot)
        );
        await db.SaveChangesAsync();
    }
}
```

### 4. Configure Your Application

Create an `appsettings.json` file with necessary configuration:

```json
{
  "ConnectionStrings": {
    "CardanoContext": "Host=localhost;Database=argus;Username=postgres;Password=password;Port=5432",
    "CardanoContextSchema": "cardanoindexer"
  },
  "CardanoNodeConnection": {
    "ConnectionType": "UnixSocket",
    "UnixSocket": {
      "Path": "/path/to/node.socket"
    },
    "NetworkMagic": 764824073, // Mainnet: 764824073, Testnet: 1097911063
    "MaxRollbackSlots": 1000,
    "RollbackBuffer": 10,
    "Slot": 139522569,
    "Hash": "3fd9925888302fca267c580d8fe6ebc923380d0b984523a1dfbefe88ef089b66"
  },
  "Sync": {
    "Dashboard": {
      "TuiMode": true,
      "RefreshInterval": 5000,
      "DisplayType": "sync"
    }
  }
}
```

### 5. Register Services

Register Argus services in your application:

```csharp
// Program.cs
using Argus.Sync.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        // Register the database context
        services.AddDbContextFactory<MyDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            options.UseNpgsql(
                configuration.GetConnectionString("CardanoContext"),
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    configuration.GetValue<string>("ConnectionStrings:CardanoContextSchema")
                )
            );
        });

        // Register Argus services
        services.AddCardanoIndexer<MyDbContext>(hostContext.Configuration);
        services.AddReducers<MyDbContext, IReducerModel>([typeof(BlockReducer)]);
    })
    .Build();
```

### 6. Create and Apply Database Migrations

Generate and apply Entity Framework migrations:

```bash
# Create the initial migration
dotnet ef migrations add InitialMigration

# Apply the migration to the database
dotnet ef database update
```

### 7. Run Your Application

Start your application to begin synchronizing with the blockchain:

```bash
dotnet run
```

### 8. Monitor Synchronization

Argus provides a built-in dashboard to monitor the synchronization process. The dashboard displays:

- Progress for each reducer
- System resource usage
- Estimated completion time

## 📊 Understanding Reducers

Reducers are the core component of Argus that determine what blockchain data to extract and how to store it. Each reducer performs a specific indexing task and implements the `IReducer<T>` interface.

### How Reducers Work

1. **Block Processing**: When a new block arrives, the `RollForwardAsync` method is called
2. **Data Extraction**: The reducer extracts relevant data from the block
3. **Database Storage**: The extracted data is stored in the database using Entity Framework
4. **Rollback Handling**: If a chain reorganization occurs, the `RollBackwardAsync` method ensures data consistency

### Types of Reducers

You can create various types of reducers based on your application needs:

- **Block Reducers**: Index basic block information
- **Transaction Reducers**: Track all or specific transactions
- **Smart Contract Reducers**: Monitor interactions with specific smart contracts
- **Address Reducers**: Track balance changes for specific addresses
- **Custom Business Logic Reducers**: Implement application-specific business rules

## ⚙️ Configuration

Argus requires several configuration settings in your `appsettings.json` file to connect to the Cardano blockchain, manage database connections, and control how the indexing process functions. Below are the key configuration sections:

### Database Connection

Configure your PostgreSQL database connection:

```json
"ConnectionStrings": {
  "CardanoContext": "Host=localhost;Database=dbName;Username=yourUsername;Password=yourPassword;Port=yourPort",
  "CardanoContextSchema": "cardanoindexer"
}
```

### Cardano Node Connection

Specify how Argus connects to the Cardano blockchain:

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
  "NetworkMagic": 764824073,  // Mainnet: 764824073, Testnet: 1097911063
  "MaxRollbackSlots": 1000,    // Maximum rollback depth for chain reorganizations
  "RollbackBuffer": 10,        // Buffer for rollback safety
  "Slot": 139522569,           // Starting slot for synchronization
  "Hash": "3fd9925888302fca267c580d8fe6ebc923380d0b984523a1dfbefe88ef089b66" // Starting block hash
}
```

### Reducer Configuration

Define starting points for your indexing process and individual reducers:

```json
"CardanoIndexStart": {
  "Slot": 139522569,           // Global starting slot for synchronization
  "Hash": "3fd9925888302fca267c580d8fe6ebc923380d0b984523a1dfbefe88ef089b66"
},
"CardanoIndexReducers": {
  "TestReducer": {             // Specific reducer configuration by name
    "StartSlot": 139522569,    // Override global start point for this reducer
    "StartHash": "3fd9925888302fca267c580d8fe6ebc923380d0b984523a1dfbefe88ef089b66"
  }
}
```

### Sync and Dashboard Settings

Control synchronization behavior and monitoring:

```json
"Sync": {
  "Rollback": {                // Manual rollback configuration
    "Enabled": false,          // Set to true to enable manual rollback mode
    "RollbackHash": "20a81db38339bf6ee9b1d7e22b22c0ac4d887d332bbf4f3005db4848cd647743",
    "RollbackSlot": 57371845,
    "Reducers": {              // Per-reducer rollback settings
      "BlockTestReducer": {
        "Enabled": true,
        "RollbackHash": "20a81db38339bf6ee9b1d7e22b22c0ac4d887d332bbf4f3005db4848cd647743",
        "RollbackSlot": 57371845
      }
    }
  },
  "Dashboard": {               // Monitoring dashboard settings
    "TuiMode": true,           // Enable/disable terminal UI
    "RefreshInterval": 5000,   // Refresh interval in milliseconds
    "DisplayType": "sync"      // Options: "sync" or "full"
  },
  "State": {
    "ReducerStateSyncInterval": 5000  // How often to update reducer state in database
  }
}
```

## 🧪 Advanced Use Cases

Argus can be used to create specialized reducers for different application needs. Let's explore some advanced examples:

### Smart Contract Tracking

Track interactions with specific smart contracts, such as a DEX (Decentralized Exchange):

```csharp
public class DexTradeReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration)
    : IReducer<DexTrade> where T : CardanoDbContext, IDexTradeDbContext
{
    private readonly string _dexScriptHash = configuration
        .GetValue<string>("DexScriptHash") ?? "";

    public async Task RollForwardAsync(Block block)
    {
        if (string.IsNullOrEmpty(_dexScriptHash))
            return;

        var transactions = block.TransactionBodies();
        ulong slot = block.Header().HeaderBody().Slot();

        using var dbContext = dbContextFactory.CreateDbContext();

        foreach (var tx in transactions)
        {
            // Look for transactions that interact with our DEX
            bool isDexInteraction = tx.OutputHasScriptHash(_dexScriptHash);
            if (!isDexInteraction)
                continue;

            // Extract and store DEX trade details
            string txHash = tx.Hash();
            var utxos = tx.GetUtxos();

            // Parse DEX-specific data from transaction
            var tradeDetails = ParseDexTrade(tx, utxos);
            if (tradeDetails != null)
            {
                dbContext.DexTrades.Add(new DexTrade(
                    txHash,
                    slot,
                    tradeDetails.FromToken,
                    tradeDetails.ToToken,
                    tradeDetails.FromAmount,
                    tradeDetails.ToAmount,
                    tradeDetails.Address
                ));
            }
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task RollBackwardAsync(ulong slot)
    {
        using var dbContext = dbContextFactory.CreateDbContext();

        dbContext.DexTrades.RemoveRange(
            dbContext.DexTrades
                .AsNoTracking()
                .Where(trade => trade.Slot >= slot)
        );

        await dbContext.SaveChangesAsync();
    }

    private TradeDetails? ParseDexTrade(TransactionBody tx, List<Utxo> utxos)
    {
        // DEX-specific logic to extract trade details from the transaction
        // This would analyze transaction inputs, outputs, and metadata
        // ...
        return null; // Simplified for example
    }
}
```

## 📊 Monitoring

Argus provides comprehensive monitoring options to keep track of indexing progress.

### Dashboard Options

Argus offers two dashboard modes to monitor synchronization progress:

#### Sync Progress Mode

Shows a simplified progress bar for each reducer with completion percentage and estimated time remaining.

```json
"Sync": {
  "Dashboard": {
    "TuiMode": true,
    "DisplayType": "sync"
  }
}
```

#### Full Dashboard Mode

Displays comprehensive information including:

- Overall sync progress
- Per-reducer sync progress
- System resource usage (CPU, memory)
- Thread and handle counts

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

Argus offers two rollback modes to maintain data consistency:

### Standard Rollback

During normal operation, Argus automatically handles chain reorganizations up to the configured `MaxRollbackSlots` depth.

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

2. Configure per-reducer rollback points:

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

You must then disable rollback mode to resume normal forward syncing.

## ⚙️ Technology Stack

Argus leverages several key technologies:

- **Chrysalis.Cbor**: High-performance CBOR serialization/deserialization for Cardano data structures
- **Chrysalis.Network**: Native C# implementation of Ouroboros mini-protocols for Cardano node communication
- **Entity Framework Core**: ORM for database operations
- **Spectre.Console**: Rich terminal user interface for dashboard visualizations

## 🔎 Common Use Cases

Argus can be utilized for various blockchain applications:

### 🏪 NFT Marketplace Backend

Track ownership, sales, and listings of NFTs on the Cardano blockchain.

```csharp
public class NFTSaleReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<NFTSale> where T : CardanoDbContext, INFTDbContext
{
    // Implementation to track NFT sales
    // ...
}
```

### 💱 DeFi Analytics Platform

Monitor liquidity pools, trading volume, and token prices across Cardano DeFi protocols.

```csharp
public class LiquidityPoolReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<LiquidityPool> where T : CardanoDbContext, IDeFiDbContext
{
    // Implementation to track liquidity pool changes
    // ...
}
```

### 👛 Wallet Tracking Service

Track balance changes and transaction history for specific addresses.

```csharp
public class AddressBalanceReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<AddressBalance> where T : CardanoDbContext, IWalletDbContext
{
    // Implementation to track address balances
    // ...
}
```

### 📊 Analytics Dashboard

Create a real-time analytics dashboard for on-chain metrics.

```csharp
public class BlockchainMetricsReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<BlockchainMetrics> where T : CardanoDbContext, IMetricsDbContext
{
    // Implementation to track various blockchain metrics
    // ...
}
```

## 🛣️ Roadmap

- [x] **Comprehensive Library Enhancement**:
  - Expose blockchain data based on CDDL fields
  - Implement common general reducers and DApp-specific reducers
  - Simplify installation and usage through NuGet and tutorials
- [x] **Stability and Performance Optimization**:
  - Improve stability and performance
  - Create performance reports with benchmarks and testing results
- [x] **Documentation Website**:
  - Expand documentation with detailed features and use cases
  - Create clear tutorials and examples
- [ ] **Community Engagement**:
  - Official launch of documentation website
  - Host community events such as webinars

## 🤝 Contributing

We welcome contributions from the community! Here's how you can help:

### Ways to Contribute

- **Bug Reports**: Create an issue describing the bug and how to reproduce it
- **Feature Requests**: Suggest new features or improvements
- **Documentation**: Help improve or translate documentation
- **Code Contributions**: Submit pull requests with bug fixes or new features

### Development Setup

1. Fork the repository
2. Clone your fork
   ```bash
   git clone https://github.com/yourusername/Argus.git
   ```
3. Create a new branch
   ```bash
   git checkout -b feature/your-feature-name
   ```
4. Make your changes
5. Submit a pull request

### Code Style

- Follow the existing code style and naming conventions
- Write unit tests for new functionality
- Update documentation as needed

## 📄 License

Argus is licensed under the Apache 2.0 License - see the [LICENSE](LICENSE) file for details.
