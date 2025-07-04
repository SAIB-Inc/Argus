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
   - Uses `IChainProviderFactory` for provider creation

4. **Chain Provider Factory** - Dynamic provider creation pattern:
   - `IChainProviderFactory` interface for provider injection
   - `ConfigurationChainProviderFactory` for production use
   - `MockChainProviderFactory` for testing scenarios
   - Enables testing CardanoIndexWorker without bypassing it

5. **Database Context** - Inherits from `CardanoDbContext`:
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

### Reducer Dependency System

Argus implements a sophisticated dependency system that optimizes chain connections and ensures proper processing order between reducers.

#### Core Concepts

**Dependency Declaration**:
```csharp
[ReducerDepends(typeof(BlockTestReducer))]
public class DependentTransactionReducer : IReducer<TransactionTest>
{
    // This reducer depends on BlockTestReducer
}
```

**Key Principles**:
1. **Single Dependency**: Each reducer can depend on exactly one other reducer (prevents diamond problem)
2. **Smart Connections**: Only root reducers (no dependencies) get chain connections
3. **Block Forwarding**: Dependent reducers receive blocks via forwarding from their parent
4. **Parallel Processing**: Multiple dependents of the same parent process blocks in parallel

#### How It Works

1. **Dependency Graph Building**:
   - CardanoIndexWorker builds a dependency graph on startup
   - Identifies root reducers (no dependencies) and dependent reducers
   - Validates no circular dependencies exist

2. **Connection Management**:
   ```
   BlockReducer (root) → Gets chain connection
   TransactionReducer (root) → Gets chain connection  
   DependentReducer (depends on Block) → No connection, receives via forwarding
   ChainedReducer (depends on Dependent) → No connection, receives via forwarding
   ```

3. **Block Forwarding Flow**:
   - Root reducer processes block from chain
   - After processing, forwards block to all its dependents in parallel
   - Each dependent processes and forwards to its dependents (recursive)

#### Start Point Logic

The system implements intelligent start point management to ensure dependent reducers don't waste resources processing old blocks.

**Automatic Adjustment**:
- If BlockReducer is at slot 1000 and DependentReducer is starting fresh (slot 0)
- DependentReducer automatically adjusts to start at slot 1000
- Uses actual intersection points with valid hashes (no empty strings)

**Topological Processing**:
- Dependencies are adjusted in order (A before B before C)
- Ensures each reducer starts at the optimal point based on its dependency

**Runtime Filtering**:
- `ShouldProcessBlock` checks if dependencies have processed a block
- Prevents dependents from processing blocks their dependencies haven't seen
- Dynamic adjustment if dependencies advance significantly

**Edge Cases Handled**:
1. **Bootstrap**: All reducers starting fresh - no adjustments needed
2. **Dependency Behind**: Dependent waits for dependency to catch up
3. **Invalid State**: Warnings for inconsistent states (dependent ahead of dependency)
4. **Chain Dependencies**: Proper handling of A→B→C→D chains

#### Configuration

**Declaring Dependencies**:
```csharp
// Single dependency
[ReducerDepends(typeof(BlockTestReducer))]
public class TokenReducer : IReducer<Token> { }

// Chain dependency
[ReducerDepends(typeof(TokenReducer))]
public class TokenStatsReducer : IReducer<TokenStats> { }
```

**Registering Reducers**:
```csharp
// All reducers registered normally - dependency resolution is automatic
services.AddReducers<DbContext, IReducerModel>();
```

#### Testing Dependencies

**Key Test Scenarios**:
1. **Connection Count**: Verify only root reducers create connections
2. **Execution Order**: Ensure proper forwarding sequence
3. **Start Point Adjustment**: Test automatic adjustment logic
4. **Rollback Cascading**: Verify rollbacks propagate through dependencies

**Test Example**:
```csharp
// DependencySystemTest verifies:
// - 3 providers created: 1 tip + 2 root reducers (not 4)
// - Blocks forward correctly through dependency chain
// - Start points adjust properly
```

#### Implementation Details

**Parallel Forwarding** (ForwardToDependentsAsync):
```csharp
var tasks = dependentNames
    .Where(name => ShouldProcessBlock(name, slot))
    .Select(name => ProcessDependentAsync(name, response, action));
await Task.WhenAll(tasks);
```

**State Management**:
- All reducers (root and dependent) maintain their own ReducerState
- States are persisted for recovery
- Dynamic adjustments are saved to database

**Performance Considerations**:
- Reduces connection overhead (fewer chain connections)
- Enables parallel processing of independent branches
- Minimal forwarding latency (in-memory)

#### Future Optimization: Catch-Up Mode

A planned optimization for dependents that are significantly behind:

**Concept**: If a dependent is >10,000 blocks behind its dependency:
1. Temporarily give it its own chain connection
2. Process independently until within ~100 blocks
3. Switch back to forwarding mode

**Benefits**:
- Faster catch-up for lagging dependents
- Less forwarding load on parent reducers
- Better resource utilization

**Challenges**:
- Managing mode transitions
- Ensuring consistency during switch
- Connection pool management

This feature can be added later without changing the core dependency architecture.

### Configuration System
Configuration is managed through appsettings.json files with these key sections:

1. **ConnectionStrings** - Database connection settings
2. **CardanoNodeConnection** - Blockchain connection settings
3. **CardanoIndexReducers** - Reducer-specific configuration
4. **Sync** - Dashboard and monitoring settings
   - `Sync:Worker:ExitOnCompletion` - Set to false for testing to disable Environment.Exit()
   - `Sync:State:ReducerStateSyncInterval` - State sync frequency (1000ms recommended for tests)

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

### Factory Pattern Testing

**MockChainProviderFactory** enables testing CardanoIndexWorker directly:
- Creates separate `MockChainSyncProvider` instances for each reducer
- Prevents concurrency issues by isolating provider state
- Allows external test control through trigger methods
- Tests complete pipeline: `MockProvider -> CardanoIndexWorker -> Reducers -> Database`

### Test Structure

**Organized Test Architecture**:
```
Tests/
├── Infrastructure/
│   ├── TestDatabaseManager.cs     # Database setup/cleanup
│   └── BlockTestDataLoader.cs     # Unified block loading
├── Mocks/
│   ├── MockChainSyncProvider.cs       # Ouroboros protocol simulation
│   └── MockChainProviderFactory.cs    # Factory for separate provider instances
├── EndToEnd/
│   ├── CardanoIndexWorkerTest.cs      # Worker factory pattern integration test
│   ├── DependencySystemTest.cs        # Reducer dependency system test
│   ├── StartPointLogicTest.cs         # Start point adjustment logic test
│   └── ReducerDirectTest.cs           # Direct reducer testing
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

# Test CardanoIndexWorker with factory pattern
dotnet test --filter "FullyQualifiedName~CardanoIndexWorkerTest" --logger "console;verbosity=detailed"

# Test direct reducer logic
dotnet test --filter "FullyQualifiedName~ReducerDirectTest" --logger "console;verbosity=detailed"

# Run specific test with detailed output
dotnet test --filter "FullyQualifiedName~SingleBlockRollForwardRollbackTest" --logger "console;verbosity=detailed"

# Test dependency system
dotnet test --filter "FullyQualifiedName~DependencySystemTest" --logger "console;verbosity=detailed"

# Test start point logic
dotnet test --filter "FullyQualifiedName~StartPointLogicTest" --logger "console;verbosity=detailed"
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

## Enhanced Block Content Logging and Memory State Verification

Argus testing infrastructure includes comprehensive block content analysis and memory-database state consistency verification to ensure data integrity throughout the sync lifecycle.

### Trigger-Based Chain Sync Control

**MockChainSyncProvider** supports manual trigger-based control for precise test scenarios:

```csharp
// Create controllable mock provider
var mockProvider = new MockChainSyncProvider(testDataDir);

// Trigger specific rollforward operations
await mockProvider.TriggerRollForwardAsync(slot);

// Trigger rollback with rollback type control
await mockProvider.TriggerRollBackAsync(rollbackSlot, RollBackType.Exclusive);

// Complete chain sync when done
mockProvider.CompleteChainSync();
```

**Key Features**:
- **Pure Manual Control**: No automatic block processing - test controls all events
- **Rollback Slot Override**: Can rollback to arbitrary slots using override mechanism
- **Channel-Based Communication**: Uses .NET Channels for thread-safe trigger coordination

### Block Content Analysis

**Detailed Block Information Logging**:
```
=== Block Contents Analysis ===
Block 1: Slot 82801348, Height 3314966, 1 txs, 2333 bytes, Hash 6afb5d5fb8f11608...
  Tx 0: Hash 81895e16c2537281..., 4 inputs, 5 outputs
Block 2: Slot 82916704, Height 3319101, 0 txs, 862 bytes, Hash 842ed25ecf3b6102...
Block 3: Slot 82916750, Height 3319102, 3 txs, 6001 bytes, Hash 9cca31b8bfb4647f...
  Tx 0: Hash 8dba283a27025fdc..., 2 inputs, 2 outputs
  Tx 1: Hash 315ae904930b0938..., 1 inputs, 2 outputs
  Tx 2: Hash 39c0da40bfaba92e..., 3 inputs, 3 outputs
```

**Technical Implementation**:
```csharp
// Block size calculation using CBOR serialization
var blockSize = CborSerializer.Serialize(block).Length;

// Transaction hash extraction using Chrysalis extensions
var txHash = tx.Hash(); // Uses Blake2b-256 via Chrysalis.Cbor.Extensions.Cardano.Core.Transaction

// Input/output counting with proper enumeration
var inputCount = tx.Inputs()?.Count() ?? 0;
var outputCount = tx.Outputs()?.Count() ?? 0;
```

### Memory-Database State Verification

**In-Memory State Tracking**:
```csharp
// Store detailed block information for verification
var blockDetails = new Dictionary<ulong, (string hash, ulong height, int txCount, List<string> txHashes)>();

// During rollforward - collect transaction hashes
var txHashes = new List<string>();
if (txCount > 0)
{
    var txBodies = response.Block.TransactionBodies();
    if (txBodies != null)
    {
        txHashes.AddRange(txBodies.Select(tx => tx.Hash()));
    }
}
blockDetails[slot] = (hash, height, txCount, txHashes);
```

**State Consistency Verification**:
```csharp
// Per-block verification during rollforward
var dbBlocksForMemCheck = await _databaseManager.DbContext.BlockTests
    .OrderBy(b => b.Slot)
    .Select(b => new { b.Slot, b.Hash, b.Height })
    .ToListAsync();

// Verify all DB blocks exist in memory with correct details
foreach (var dbBlock in dbBlocksForMemCheck)
{
    Assert.True(blockDetails.ContainsKey(dbBlock.Slot));
    var memoryBlock = blockDetails[dbBlock.Slot];
    Assert.Equal(memoryBlock.hash, dbBlock.Hash);
    Assert.Equal(memoryBlock.height, dbBlock.Height);
}
```

**Rollback State Management**:
```csharp
// Update in-memory state during rollbacks
var slotsToRemove = blockDetails.Keys.Where(s => s >= normalizedRollbackSlot).ToList();
foreach (var slotToRemove in slotsToRemove)
{
    blockDetails.Remove(slotToRemove);
    _output.WriteLine($"  Removed slot {slotToRemove} from memory (rollback to {rollbackSlot})");
}

// Verify no extra blocks remain in memory
Assert.Equal(dbBlocksAfterRollback.Count, blockDetails.Count);
```

### UnifiedFiveBlockTest Example

**Complete Integration Test**:
- **5 Rollforward Operations**: Each with per-block memory-database verification
- **6 Rollback Operations**: Including final complete database cleanup
- **Real Conway Era Blocks**: Uses actual Cardano mainnet CBOR data
- **Transaction-Level Tracking**: Verifies individual transaction hashes
- **Trigger-Based Control**: External test control of all chain sync events

**Key Test Commands**:
```bash
# Run enhanced test with detailed logging
dotnet test --filter "FullyQualifiedName~UnifiedFiveBlockTest" --logger "console;verbosity=detailed"
```

**Verification Outputs**:
```
✅ Per-block verification: 3 blocks, 6 transactions in DB
✅ Memory-DB state consistency verified for slot 82916750
✅ Per-rollback verification passed
✅ Memory-DB state consistency verified after rollback
✅ Final state: 0 blocks with 0 transactions (complete rollback)
```

### Key Technical Dependencies

**Required Namespaces**:
```csharp
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction; // For tx.Hash()
using Chrysalis.Cbor.Serialization; // For CborSerializer.Serialize()
```

**Block and Transaction Properties**:
- **BlockTest Model**: `Hash`, `Height`, `Slot`, `CreatedAt`
- **TransactionTest Model**: `TxHash`, `TxIndex`, `Slot`, `BlockHash`, `BlockHeight`, `RawTx`, `CreatedAt`

This enhanced testing approach ensures complete data integrity validation throughout the entire blockchain sync lifecycle, providing confidence in both the sync worker implementation and the underlying state management systems.