using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class Sundae : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    LpToken = table.Column<string>(type: "text", nullable: false),
                    CirculatingLp = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TxRaw = table.Column<byte[]>(type: "bytea", nullable: false)
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
                name: "SundaeSwapLiquidityPools",
                schema: "public");
        }
    }
}
