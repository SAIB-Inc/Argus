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