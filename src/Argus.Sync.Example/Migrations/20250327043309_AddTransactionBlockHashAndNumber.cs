using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionBlockHashAndNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockHash",
                schema: "public",
                table: "TransactionTests",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "BlockNumber",
                schema: "public",
                table: "TransactionTests",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockHash",
                schema: "public",
                table: "TransactionTests");

            migrationBuilder.DropColumn(
                name: "BlockNumber",
                schema: "public",
                table: "TransactionTests");
        }
    }
}
