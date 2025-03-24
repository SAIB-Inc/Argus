using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class OrderBySlotReducerTypes2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerAddress",
                schema: "public",
                table: "OrdersBySlot",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpentTxHash",
                schema: "public",
                table: "OrdersBySlot",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerAddress",
                schema: "public",
                table: "OrdersBySlot");

            migrationBuilder.DropColumn(
                name: "SpentTxHash",
                schema: "public",
                table: "OrdersBySlot");
        }
    }
}
