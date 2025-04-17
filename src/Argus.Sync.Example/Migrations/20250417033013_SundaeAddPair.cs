using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class SundaeAddPair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Pair",
                schema: "public",
                table: "SundaeSwapLiquidityPools",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Pair",
                schema: "public",
                table: "SundaeSwapLiquidityPools");
        }
    }
}
