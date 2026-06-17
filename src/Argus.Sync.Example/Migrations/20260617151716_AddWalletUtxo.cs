using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations;

/// <inheritdoc />
public partial class AddWalletUtxo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        _ = migrationBuilder.CreateTable(
            name: "WalletUtxos",
            schema: "public",
            columns: table => new
            {
                TxHash = table.Column<string>(type: "text", nullable: false),
                TxIndex = table.Column<int>(type: "integer", nullable: false),
                Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                Address = table.Column<string>(type: "text", nullable: false),
                AddressName = table.Column<string>(type: "text", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                SpentSlot = table.Column<decimal>(type: "numeric(20,0)", nullable: true)
            },
            constraints: table =>
            {
                _ = table.PrimaryKey("PK_WalletUtxos", x => new { x.TxHash, x.TxIndex });
            });

        _ = migrationBuilder.CreateIndex(
            name: "IX_WalletUtxos_Address",
            schema: "public",
            table: "WalletUtxos",
            column: "Address");

        _ = migrationBuilder.CreateIndex(
            name: "IX_WalletUtxos_SpentSlot",
            schema: "public",
            table: "WalletUtxos",
            column: "SpentSlot");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        _ = migrationBuilder.DropTable(
            name: "WalletUtxos",
            schema: "public");
    }
}
