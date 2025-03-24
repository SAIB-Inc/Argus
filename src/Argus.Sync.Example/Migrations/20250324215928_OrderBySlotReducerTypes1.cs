using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class OrderBySlotReducerTypes1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockTests",
                schema: "public");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OrdersBySlot",
                schema: "public",
                table: "OrdersBySlot");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrdersBySlot",
                schema: "public",
                table: "OrdersBySlot",
                columns: new[] { "TxHash", "Index" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_OrdersBySlot",
                schema: "public",
                table: "OrdersBySlot");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OrdersBySlot",
                schema: "public",
                table: "OrdersBySlot",
                column: "TxHash");

            migrationBuilder.CreateTable(
                name: "BlockTests",
                schema: "public",
                columns: table => new
                {
                    BlockHash = table.Column<string>(type: "text", nullable: false),
                    BlockNumber = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockTests", x => x.BlockHash);
                });
        }
    }
}
