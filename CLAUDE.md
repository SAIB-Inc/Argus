# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Argus is a .NET library for indexing and processing Cardano blockchain data. It processes blockchain data into structured, queryable formats stored in a database, making blockchain data easier to work with for .NET developers.

## Build and Run Commands

### Building the Solution
```bash
dotnet build
```

### Running the Example Project
```bash
cd src/Argus.Sync.Example
dotnet run
```

### Running Tests
```bash
dotnet test
```

### Running Specific Tests
```bash
dotnet test --filter "FullyQualifiedName~Argus.Sync.Tests.YourTestClassName"
```

### Creating Database Migrations
```bash
# From the project directory that needs migrations (e.g., Argus.Sync.Example)
dotnet ef migrations add MigrationName
```

### Applying Migrations
```bash
# From the project directory that needs migrations (e.g., Argus.Sync.Example)
dotnet ef database update
```

### Building for Release
```bash
dotnet build --configuration Release
```

### Creating NuGet Package
```bash
cd src/Argus.Sync
dotnet pack --configuration Release
```

## Architecture Overview

Argus consists of several core components:

1. **Chain Providers** - Connect to the Cardano blockchain through:
   - N2CProvider (Unix Socket connection)
   - U5CProvider (gRPC connection)
   - N2NProvider (Native TCP connection)

2. **Reducers** - Process and transform blockchain data:
   - Implement `IReducer<T>` interface
   - Define `RollForwardAsync` method to process new blocks
   - Define `RollBackwardAsync` method to handle chain reorganizations

3. **CardanoIndexWorker** - Manages the synchronization process:
   - Coordinates multiple reducers
   - Handles block processing and rollbacks
   - Provides monitoring capabilities

4. **Database Context** - Inherits from `CardanoDbContext`:
   - Configure models for Entity Framework
   - Define relationships and constraints
   - Handles database operations

## Key Concepts

### Reducer Development
Reducers are the central component that determine what blockchain data to extract and how to store it.

A basic reducer:
1. Extends `IReducer<T>` where T is your model type implementing `IReducerModel`
2. Takes an `IDbContextFactory<YourDbContext>` in the constructor
3. Implements `RollForwardAsync(Block block)` to process new blocks
4. Implements `RollBackwardAsync(ulong slot)` to handle rollbacks

### Configuration System
Configuration is managed through appsettings.json files with these key sections:

1. **ConnectionStrings** - Database connection settings
2. **CardanoNodeConnection** - Blockchain connection settings
3. **CardanoIndexReducers** - Reducer-specific configuration
4. **Sync** - Dashboard and monitoring settings

### Database Setup
Argus uses Entity Framework Core with PostgreSQL. The setup involves:

1. Creating models that implement `IReducerModel`
2. Creating a database context that extends `CardanoDbContext`
3. Configuring entity relationships in the `OnModelCreating` method
4. Generating and applying migrations

### Handling Rollbacks
Argus has built-in support for blockchain reorganizations:

1. Standard rollbacks happen automatically during normal operation
2. Dedicated rollback mode allows rolling back to a specific point

## Testing Infrastructure

Argus includes comprehensive end-to-end testing infrastructure that validates the complete sync workflow with real Cardano blockchain data.

### Test Data Management

**Unified Block Storage**: All test blocks are stored in `TestData/Blocks/` using slot-based naming:
```
TestData/
└── Blocks/
    ├── 82916704.cbor
    ├── 82916750.cbor
    ├── 82916813.cbor
    └── ... (up to 100 real Conway era blocks)
```

**BlockTestDataLoader**: Unified loader that can:
- Load single blocks by slot: `LoadSingleBlockAsync(slot)`
- Load consecutive blocks: `LoadConsecutiveBlocksAsync(startSlot, count)`
- Load slot ranges: `LoadSlotRangeAsync(fromSlot, toSlot)`
- Discover available blocks: `GetAvailableSlots()`

### Mock Chain Sync Provider

**MockChainSyncProvider** simulates proper Ouroboros mini-protocol behavior:
1. **Initial Rollback**: Sends `NextResponseAction.RollBack` to establish intersection
2. **Block Processing**: Yields `NextResponseAction.RollForward` with real block data
3. **Real CBOR Data**: Uses actual era-tagged blocks downloaded from Cardano mainnet

### Test Structure

**Organized Test Architecture**:
```
Tests/
├── Infrastructure/
│   ├── TestDatabaseManager.cs     # Database setup/cleanup
│   └── BlockTestDataLoader.cs     # Unified block loading
├── Mocks/
│   └── MockChainSyncProvider.cs   # Ouroboros protocol simulation
├── EndToEnd/
│   ├── SingleBlockRollForwardRollbackTest.cs    # Single block tests
│   └── MultipleBlocksPartialRollbackTest.cs     # Multiple block tests
├── DataGeneration/
│   ├── BlockCborDownloadTest.cs           # Download single blocks
│   ├── MultipleBlockCborDownloadTest.cs   # Download 100 consecutive blocks
│   └── OuroborosProtocolObservationTest.cs # Protocol behavior analysis
└── TestData/
    └── Blocks/                            # Real blockchain data
```

### Key Test Commands

**Download Real Block Data**:
```bash
# Download single block (requires Cardano node at /tmp/node.socket)
dotnet test --filter "FullyQualifiedName~DataGeneration.BlockCborDownloadTest"

# Download 100 consecutive blocks
dotnet test --filter "FullyQualifiedName~DataGeneration.MultipleBlockCborDownloadTest"
```

**Run End-to-End Tests**:
```bash
# Run all end-to-end tests
dotnet test --filter "FullyQualifiedName~EndToEnd"

# Run specific test with detailed output
dotnet test --filter "FullyQualifiedName~SingleBlockRollForwardRollbackTest" --logger "console;verbosity=detailed"
```

**Observe Protocol Behavior**:
```bash
# Observe real Ouroboros protocol messages
dotnet test --filter "FullyQualifiedName~DataGeneration.OuroborosProtocolObservationTest" --logger "console;verbosity=detailed"
```

### Ouroboros Protocol Insights

Based on real protocol observation, the Cardano chain sync follows this pattern:

1. **MessageRollBackward**: Establishes intersection point
   - Contains slot and hash of the rollback point
   - Always sent first after finding intersection

2. **MessageRollForward**: Delivers actual blocks
   - Contains era-tagged CBOR block data (starts with `D818`)
   - Includes current chain tip information
   - Block transaction counts can be 0 (empty blocks are normal)
   - Block sizes vary significantly (869 bytes to 6+ KB depending on transactions)

### Test Database Configuration

Tests use isolated PostgreSQL databases with automatic cleanup:
- **Connection**: `Host=localhost;Database=argus_test_{guid};Username=postgres;Password=postgres;Port=4321`
- **Cleanup**: Automatic database deletion after each test via `IAsyncLifetime`
- **Schema**: Uses same models as production (`BlockTest`, `TransactionTest`)

### Conway Era Block Format

Real blocks use Conway era (era 7) format:
- **Era Tag**: CBOR tag 24 (`D818`)
- **Era Identifier**: `8207` (array with era 7)
- **Block Data**: Contains header, transactions, witnesses, auxiliary data
- **Slot Gaps**: Normal - not every slot has a block due to Cardano's slot lottery

## Rollback Semantics and Chain Provider Behavior

Argus implements sophisticated rollback handling that varies by chain provider type, following the Ouroboros protocol specifications.

### Chain Provider Rollback Types

**N2CProvider (Unix Socket - Standard Ouroboros)**:
- Uses `RollBackType.Exclusive`
- Rollback point represents the last valid common block to preserve
- Protocol: "Keep this block and everything before it"

**U5CProvider (gRPC/UTxO RPC)**:
- `Undo` operations: `RollBackType.Inclusive` (roll back including the specified block)
- `Reset` operations: `RollBackType.Exclusive` (roll back to the specified point)
- Dual semantics based on UTxO RPC action types

**N2NProvider (Native TCP)**:
- Not implemented (throws `NotImplementedException`)

### Rollback Slot Calculation

The `CardanoIndexWorker` calculates rollback slots based on the rollback type:

```csharp
ulong rollbackSlot = response.RollBackType switch
{
    RollBackType.Exclusive => response.Block.Header().HeaderBody().Slot() + 1UL,
    RollBackType.Inclusive => response.Block.Header().HeaderBody().Slot(),
    _ => 0
};
```

**Exclusive Rollback (N2C Standard)**:
- Rollback point: slot X
- Removes: blocks with slot ≥ (X + 1) 
- Preserves: blocks with slot ≤ X
- **Meaning**: The rollback point itself remains valid

**Inclusive Rollback (U5C Undo)**:
- Rollback point: slot X
- Removes: blocks with slot ≥ X
- Preserves: blocks with slot < X  
- **Meaning**: The rollback point itself is also rolled back

### Safety Mechanisms

**Rollback Depth Limiting**:
- Maximum rollback depth: configurable (default 10,000 slots)
- Prevents excessive rollbacks that could destabilize the index

**State Consistency**:
- Maintains `LatestIntersections` for recovery
- Updates rollback buffer (default 10 recent intersection points)
- Ensures database state matches chain state after rollbacks

### Reducer Implementation Guidelines

When implementing `RollBackwardAsync(ulong slot)` in reducers:

```csharp
// Standard implementation - removes blocks >= rollbackSlot
await context.Blocks.Where(b => b.Slot >= rollbackSlot).ExecuteDeleteAsync();
await context.Transactions.Where(t => t.Slot >= rollbackSlot).ExecuteDeleteAsync();
```

The worker handles the rollback type semantics, so reducers receive the correct slot boundary for their deletion logic.

### Best Practices for Test Development

1. **Use Real Data**: Always prefer real blockchain data over mocked data
2. **Chain Sync Simulation**: Use `MockChainSyncProvider` for proper protocol simulation
3. **Database Isolation**: Each test gets its own database with cleanup
4. **Flexible Block Loading**: Use `BlockTestDataLoader` for different test scenarios
5. **Protocol Compliance**: Follow Ouroboros mini-protocol (rollback → rollforward)
6. **Rollback Testing**: Test both exclusive and inclusive rollback scenarios when applicable