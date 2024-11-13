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

</div>

<div align="center">

  ![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)
  ![Contributors](https://img.shields.io/github/contributors/SAIB-Inc/Argus.svg?label=Contributors)
  ![Issues](https://img.shields.io/github/issues/SAIB-Inc/Argus.svg?label=Open%20Issues)
  ![Issues Closed](https://img.shields.io/github/issues-closed/SAIB-Inc/Argus.svg?label=Closed%20Issues)
  <a href="https://www.nuget.org/packages/SAIB.Cardano.Sync" style="display: inline-block; text-decoration: none; border: none;">
      <img src="https://img.shields.io/nuget/v/SAIB.Cardano.Sync.svg" alt="NuGet">
  </a>
  <img src="https://img.shields.io/badge/C%23-purple.svg" style="display: inline-block;">

</div>



Argus is a .NET library that simplifies interactions with the Cardano blockchain by providing an efficient and easy to use indexing framework.
Initially supporting PostgreSQL as the database backend, it processes block data into structured, queryable formats.
This tool is designed for robust enterprise integration, with plans to introduce additional database backends in the future to broaden its applicability and flexibility.

## Features :sparkles:

- **Indexing**: Communicate with the Cardano blockchain to filter and retrieve required information, making it easy for users to store relevant data in a database.
- **Power**: Integrates with C# tools like LINQ, ASP.NET, and Entity Framework, enhancing the development experience for building and updating dApps.
- **C# Data Structures**: Translates Cardano's CBOR data into C# data types, allowing users to seamlessly utilize blockchain data.
- **Efficiency**: Boosts producivity by allowing users to create secure and powerful Cardano dApps or update existing ones easier and faster.
- **Customizable**: Create custom reducers tailored to your specific needs. You may also utilize the given general reducers.

## Roadmap :rocket:

- [x] **Expand Comprehensive Library Enhancement**: 
  - Expose blockchain data based upon the CDDL fields.
  - Implement common general reducers and common use-case dApp reducers.
  - Simplify installation and usage for developers through NuGet and a comprehensive tutorial.
- [ ] **Stability Enhancement and Performance Optimization**: 
  - Improve the stability and performance of Argus.
  - Create a performance report detailing benchmarks, optimizations, and testing results.
- [ ] **Official Website**: 
  - Expand and enhance Argus documentation by creating a website detailing its features and use cases.
  - Have a reviewer confirm the accuracy of the website and asses the clarity and ease of use of the tutorial.
- [ ] **Official Launch and Community Outreach**: 
  - Officially launch the Argus website and extensive documentation.
  - Hold community engagement events such as webinars.

## Getting Started :package:

To use Argus in your .NET project:

1. You can install Argus via NuGet:  

    ```bash
    
      dotnet add package SAIB.Cardano.Sync
    
    ```

2. Dependency Installation:  
    Entity Framework

3. Run your PostgreSQL server instance.  

4. Configure your appsettings.json file:  
    Database Connection:

    ```json

      "ConnectionStrings": {
        "CardanoContext": "Host=localhost;Database=dbName;Username=yourUsername;Password=yourPassword;Port=yourPort",
        "CardanoContextSchema": "cardanoindexer"
      }
    
    ```

    Argus Connection Types:

    ```json

      "CardanoNodeConnection": {
        "ConnectionType": "gRPC",
        "UnixSocket": {
          "Path": "yourPath/node.socket"
        },
        "TCP": {
          "Host": "localhost",
          "Port": 3000
        },
        "gRPC": {
          "Endpoint": "https://yourEndpoint",
          "ApiKey": "yourKey"
        },
        "NetworkMagic": 764824073
      }

    ```

    Reducer Starting Intersection:

    ```json
    
      "CardanoIndexStart":{
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

    Smart Contract Related Info (Optional):
      Argus includes general reducers, add the corresponding configuration lines for the dApp reducers you plan to use.

    ```json

      "JPGStoreMarketplaceV1ValidatorScriptHash": "c727443d77df6cff95dca383994f4c3024d03ff56b02ecc22b0f3f65", 
      "SplashScriptHash": "9dee0659686c3ab807895c929e3284c11222affd710b09be690f924d", 
      "MinswapScriptHash": "ea07b733d932129c378af627436e7cbc2ef0bf96e0036bb51b3bde6b", 
      "SundaeSwapScriptHash": "e0302560ced2fdcbfcb2602697df970cd0d6a38f94b32703f51c312b"

    ```

5. Create your models and DbContext or use our general reducers:  

    Entity Class:  

    
    ```cs

    TxBySlot.cs

      public record TxBySlot(
        string Hash,
        ulong Slot,
        uint Index,
        byte[] RawCbor
      ) : IReducerModel;

    ```

    Context Object:  

    
    ```cs 

    TxBySlotDbContext.cs

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

6. Migrate and update your database changes:  

    ```bash

      dotnet ef migrations add <migrationName> 
      dotnet ef database update

    ```  

7. Run your reducer!  

    ```bash
    
      dotnet run -c Release
    
    ```

## Example :pencil2:  



    ```cs

    Program.cs
        builder.Services.AddCardanoIndexer<CardanoTestDbContext>(builder.Configuration); 
        builder.Services.AddReducers<CardanoTestDbContext, IReducerModel>([typeof(TxBySlotReducer<>)]); 

    TxBySlotReducer.cs
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

