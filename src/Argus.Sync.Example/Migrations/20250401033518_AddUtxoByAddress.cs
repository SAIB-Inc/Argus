using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class AddUtxoByAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "AssetOwnerBySlot",
                schema: "public",
                columns: table => new
                {
                    Address = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    OutRef = table.Column<string>(type: "text", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    PolicyId = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    SpentTxHash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetOwnerBySlot", x => new { x.Address, x.Subject, x.Slot, x.OutRef });
                });

            migrationBuilder.CreateTable(
                name: "BalanceByAddress",
                schema: "public",
                columns: table => new
                {
                    Address = table.Column<string>(type: "text", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceByAddress", x => x.Address);
                });

            migrationBuilder.CreateTable(
                name: "BlockTests",
                schema: "public",
                columns: table => new
                {
                    BlockHash = table.Column<string>(type: "text", nullable: false),
                    BlockNumber = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockTests", x => x.BlockHash);
                });

            migrationBuilder.CreateTable(
                name: "OrdersBySlot",
                schema: "public",
                columns: table => new
                {
                    TxHash = table.Column<string>(type: "text", nullable: false),
                    TxIndex = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    OwnerAddress = table.Column<string>(type: "text", nullable: false),
                    PolicyId = table.Column<string>(type: "text", nullable: false),
                    AssetName = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    SpentSlot = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    BuyerAddress = table.Column<string>(type: "text", nullable: true),
                    SpentTxHash = table.Column<string>(type: "text", nullable: true),
                    RawData = table.Column<byte[]>(type: "bytea", nullable: false),
                    DatumRaw = table.Column<byte[]>(type: "bytea", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdersBySlot", x => new { x.TxHash, x.TxIndex });
                });

            migrationBuilder.CreateTable(
                name: "OutputBySlot",
                schema: "public",
                columns: table => new
                {
                    TxHash = table.Column<string>(type: "text", nullable: false),
                    TxIndex = table.Column<long>(type: "bigint", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    SpentSlot = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: false),
                    RawCbor = table.Column<byte[]>(type: "bytea", nullable: false),
                    DatumData = table.Column<byte[]>(type: "bytea", nullable: false),
                    ReferenceScript = table.Column<byte[]>(type: "bytea", nullable: true),
                    UtxoStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutputBySlot", x => new { x.TxHash, x.TxIndex });
                });

            migrationBuilder.CreateTable(
                name: "PricesByToken",
                schema: "public",
                columns: table => new
                {
                    OutRef = table.Column<string>(type: "text", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TokenXSubject = table.Column<string>(type: "text", nullable: false),
                    TokenYSubject = table.Column<string>(type: "text", nullable: false),
                    PlatformType = table.Column<int>(type: "integer", nullable: false),
                    TokenXPrice = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TokenYPrice = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricesByToken", x => new { x.OutRef, x.Slot, x.TokenXSubject, x.TokenYSubject, x.PlatformType });
                });

            migrationBuilder.CreateTable(
                name: "ReducerStates",
                schema: "public",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LatestIntersectionsJson = table.Column<string>(type: "text", nullable: false),
                    StartIntersectionJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReducerStates", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Royalties",
                schema: "public",
                columns: table => new
                {
                    PolicyId = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Share = table.Column<decimal>(type: "numeric", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Royalties", x => x.PolicyId);
                });

            migrationBuilder.CreateTable(
                name: "TransactionTests",
                schema: "public",
                columns: table => new
                {
                    TxHash = table.Column<string>(type: "text", nullable: false),
                    TxIndex = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    BlockHash = table.Column<string>(type: "text", nullable: false),
                    BlockNumber = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    RawTx = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionTests", x => new { x.TxHash, x.TxIndex });
                });

            migrationBuilder.CreateTable(
                name: "TxsBySlot",
                schema: "public",
                columns: table => new
                {
                    TxHash = table.Column<string>(type: "text", nullable: false),
                    Index = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Raw = table.Column<byte[]>(type: "bytea", nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    InputsRaw = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    OutputsRaw = table.Column<byte[][]>(type: "bytea[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TxsBySlot", x => new { x.TxHash, x.Index });
                });

            migrationBuilder.CreateTable(
                name: "UtxosByAddress",
                schema: "public",
                columns: table => new
                {
                    TxHash = table.Column<string>(type: "text", nullable: false),
                    TxIndex = table.Column<int>(type: "integer", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    BlockNumber = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Raw = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtxosByAddress", x => new { x.Address, x.Slot, x.TxHash, x.TxIndex });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetOwnerBySlot",
                schema: "public");

            migrationBuilder.DropTable(
                name: "BalanceByAddress",
                schema: "public");

            migrationBuilder.DropTable(
                name: "BlockTests",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OrdersBySlot",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OutputBySlot",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PricesByToken",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ReducerStates",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Royalties",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TransactionTests",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TxsBySlot",
                schema: "public");

            migrationBuilder.DropTable(
                name: "UtxosByAddress",
                schema: "public");
        }
    }
}
