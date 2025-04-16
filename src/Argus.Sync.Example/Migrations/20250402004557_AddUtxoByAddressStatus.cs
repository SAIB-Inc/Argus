using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class AddUtxoByAddressStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UtxosByAddress",
                schema: "public",
                table: "UtxosByAddress");

            migrationBuilder.AlterColumn<decimal>(
                name: "TxIndex",
                schema: "public",
                table: "UtxosByAddress",
                type: "numeric(20,0)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "public",
                table: "UtxosByAddress",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SpentSlot",
                schema: "public",
                table: "UtxosByAddress",
                type: "numeric(20,0)",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UtxosByAddress",
                schema: "public",
                table: "UtxosByAddress",
                columns: new[] { "Address", "Slot", "TxHash", "TxIndex", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UtxosByAddress",
                schema: "public",
                table: "UtxosByAddress");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "public",
                table: "UtxosByAddress");

            migrationBuilder.DropColumn(
                name: "SpentSlot",
                schema: "public",
                table: "UtxosByAddress");

            migrationBuilder.AlterColumn<int>(
                name: "TxIndex",
                schema: "public",
                table: "UtxosByAddress",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UtxosByAddress",
                schema: "public",
                table: "UtxosByAddress",
                columns: new[] { "Address", "Slot", "TxHash", "TxIndex" });
        }
    }
}
