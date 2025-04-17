using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

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
                name: "SundaeSwapLiquidityPools",
                schema: "public",
                columns: table => new
                {
                    Outref = table.Column<string>(type: "text", nullable: false),
                    Identifier = table.Column<string>(type: "text", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    AssetX = table.Column<string>(type: "text", nullable: false),
                    AssetY = table.Column<string>(type: "text", nullable: false),
                    Pair = table.Column<string>(type: "text", nullable: false),
                    LpToken = table.Column<string>(type: "text", nullable: false),
                    CirculatingLp = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TxOutputRaw = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SundaeSwapLiquidityPools", x => new { x.Identifier, x.Outref });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReducerStates",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SundaeSwapLiquidityPools",
                schema: "public");
        }
    }
}
