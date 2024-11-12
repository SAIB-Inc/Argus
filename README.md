<div align="center">
 <h1 style="font-size: 5em;">Argus | Cardano Blockchain Indexer for .NET</h1>
</div>  

<div align="center" style="background-color: #0d1117">
  <img src="/assets/asset.png" alt="Argus Logo"/>
</div>


<div align="center">

![Forks](https://img.shields.io/github/forks/SAIB-Inc/Argus.svg?style=social)  
![Stars](https://img.shields.io/github/stars/SAIB-Inc/Argus.svg?style=social)  

</div>

<div align="center">

![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)
![Contributors](https://img.shields.io/github/contributors/SAIB-Inc/Argus.svg?label=Contributors)
![Issues](https://img.shields.io/github/issues/SAIB-Inc/Argus.svg?label=Open%20Issues)
![Issues Closed](https://img.shields.io/github/issues-closed/SAIB-Inc/Argus.svg?label=Closed%20Issues)

</div>

<div align="center">

<a href="https://www.nuget.org/packages/SAIB.Cardano.Sync">
    <img src="https://img.shields.io/nuget/v/SAIB.Cardano.Sync.svg" alt="NuGet">
</a>

![C#](https://img.shields.io/badge/C%23-purple.svg) 

</div>



Argus is a .NET library that simplifies interactions with the Cardano blockchain by providing an efficient indexing framework.
Initially supporting PostgreSQL as the database backend, it processes block data into structured, queryable formats.
This tool is designed for robust enterprise integration, with plans to introduce additional database backends in the future to broaden its applicability and flexibility.

## Features :sparkles:

- **Indexing**: Communicate with the Cardano blockchain and filter required information, allowing users to store relevant data in a database.
- **Cross-Platform Compatibility**: Use Argus to create .NET projects on web, mobile, and more! Furthermore, utilize powerful C# tools like LINQ, ASP .NET, and Entity Framework.
- **C# Data Structures**: Utilize C# data structures such as Lists and Dictionaries to insert Cardano data into a database or read and use that data.
- **Efficiency**: Improved producivity allows users to create secure and powerful Cardano dApps or update existing ones faster.
- **Utility**: Utilize C# for Cardano operations such as transaction or smart contract building. Additionally, users may use provided general reducers or create customer reducers that better suit your needs.

## Roadmap :rocket:

1. **Expand Functionality**: Expose blockchain data based upon the CDDL fields, create general reducers, and facilitate ease of installation and use for developers through nuget and a tutorial.
2. **Performance Improvements**: Improve the stability and performance of Argus.
3. **Documentation**: Expand and enhance Argus documentation through a website detailing its features and use cases.
4. **Launch**: Officially launch the Argus website and extensive documentation, with community engagement events such as webinars.

## Getting Started :package:

To use Argus in your .NET project:

1. You can install Argus via NuGet:  
    `dotnet add package SAIB.Cardano.Sync`

2. Dependency Installation:  
    Chrysalis, Nsec.Cryptography, Microsoft.EntityFramework.Design, Pallas.NET

3. Create your PostgreSQL DB.  

4. Configure your appsettings.json file:  
    Database Connection:

    ```json

      "AllowedHosts": "*",
      "ConnectionStrings": {
        "CardanoContext": "Host=localhost;Database=dbName;Username=yourUsername;Password=yourPassword;Port=yourPort",
        "CardanoContextSchema": "cardanoindexer"
      }
    
    ```

    Argus Connection Types:

    ```json

      "CardanoNodeConnection": {
        "StartSlot": 139522569,
        "StartHash": "3fd9925888302fca267c580d8fe6ebc923380d0b984523a1dfbefe88ef089b66",
        "ConnectionType": "gRPC",
        "UnixSocket": {
          "Path": "yourPath/tmp/node.socket"
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

    ```json

      "JPGStoreMarketplaceV1ValidatorScriptHash": "c727443d77df6cff95dca383994f4c3024d03ff56b02ecc22b0f3f65", 
      "SplashScriptHash": "9dee0659686c3ab807895c929e3284c11222affd710b09be690f924d", 
      "MinswapScriptHash": "ea07b733d932129c378af627436e7cbc2ef0bf96e0036bb51b3bde6b", 
      "SundaeSwapScriptHash": "e0302560ced2fdcbfcb2602697df970cd0d6a38f94b32703f51c312b"

    ```

5. Create your models and DbContext or use our general reducers:  

    Entity Class:

    ```cs
      public record TxBySlot(
        string Hash,
        ulong Slot,
        uint Index,
        byte[] RawCbor
      ) : IReducerModel;
    ```

    Context Object:

    ```cs
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
  *In the terminal

      `dotnet ef migrations add <migrationName>`  
      `dotned ef database update`  

7. Run your reducer!  
  `dotnet run -c Release`

## Example :pencil2:  

Program.cs

```cs
    builder.Services.AddCardanoIndexer<CardanoTestDbContext>(builder.Configuration); 
    builder.Services.AddReducers<CardanoTestDbContext, IReducerModel>([typeof(TxBySlotReducer<>)]); 
```

```cs

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

