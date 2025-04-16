using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenPriceReducers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PricesBySubject",
                schema: "public",
                columns: table => new
                {
                    OutRef = table.Column<string>(type: "text", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    SpentSlot = table.Column<decimal>(type: "numeric(20,0)", nullable: true),
                    Price = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricesBySubject", x => new { x.OutRef, x.Slot, x.Subject });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PricesBySubject",
                schema: "public");
        }
    }
}
