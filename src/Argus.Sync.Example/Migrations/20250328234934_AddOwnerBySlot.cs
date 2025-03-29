using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerBySlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Type",
                schema: "public",
                table: "AssetOwnerBySlot",
                newName: "TransactionType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TransactionType",
                schema: "public",
                table: "AssetOwnerBySlot",
                newName: "Type");
        }
    }
}
