# Argus.Sync.Example

This project is a runnable Argus indexer and a live end-to-end smoke harness. It connects to a real Cardano node, writes to a real PostgreSQL database, and verifies that the reducer pipeline makes observable data and reducer-state progress.

The smoke harness covers the example reducer graph:

```text
BlockTestReducer -> DependentTransactionReducer -> ChainedDependentReducer
TransactionTestReducer
```

`Argus.Sync.Tests` uses an in-process mock chain provider. Use this example when you need to validate the full node-to-database path.

## Local Config

Create `src/Argus.Sync.Example/appsettings.json`. It is ignored by git so machine-specific socket paths and database credentials do not leak into commits.

```json
{
  "ConnectionStrings": {
    "CardanoContext": "Host=localhost;Database=argus-test;Username=postgres;Password=postgres;Port=4321",
    "CardanoContextSchema": "public"
  },
  "CardanoNodeConnection": {
    "ConnectionType": "UnixSocket",
    "UnixSocket": {
      "Path": "/path/to/node.socket"
    },
    "NetworkMagic": 2,
    "Slot": 82801045,
    "Hash": "3bf10d004679509605ad3d3bbd16048408914e74e8b8c85ea31c9ca9c04a92bf"
  }
}
```

## Live Smoke

Run a bounded live smoke against the configured node and database:

```bash
dotnet run --project src/Argus.Sync.Example/Argus.Sync.Example.csproj -- \
  --Example:Smoke:Enabled=true \
  --Example:Smoke:StopAfterBlocks=20 \
  --Example:Smoke:StopAfterSeconds=60 \
  --Example:Smoke:FailIfNoProgressSeconds=30 \
  --urls=http://127.0.0.1:0
```

The app exits with code `0` after the block or duration target is reached and all required progress checks pass. It exits with code `1` if the database stops progressing or the time limit is reached before required reducer state has advanced.

Smoke options:

```json
{
  "Example": {
    "Database": {
      "ApplyMigrations": true
    },
    "Smoke": {
      "Enabled": false,
      "StopAfterBlocks": 500,
      "StopAfterSeconds": 180,
      "PollIntervalSeconds": 5,
      "FailIfNoProgressSeconds": 60,
      "RequireTransactionProgress": true,
      "RequiredReducers": [
        "BlockTestReducer",
        "DependentTransactionReducer",
        "ChainedDependentReducer",
        "TransactionTestReducer"
      ]
    }
  }
}
```
