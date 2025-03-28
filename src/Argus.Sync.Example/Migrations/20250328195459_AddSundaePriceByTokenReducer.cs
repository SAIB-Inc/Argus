using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class AddSundaePriceByTokenReducer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PricesByToken",
                schema: "public",
                columns: table => new
                {
                    OutRef = table.Column<string>(type: "text", nullable: false),
                    Slot = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TokenXSubject = table.Column<string>(type: "text", nullable: false),
                    TokenYSubject = table.Column<string>(type: "text", nullable: false),
                    PlatformType = table.Column<int>(type: "integer", nullable: false),
                    TokenXPrice = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TokenYPrice = table.Column<decimal>(type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricesByToken", x => new { x.OutRef, x.Slot, x.TokenXSubject, x.TokenYSubject, x.PlatformType });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PricesByToken",
                schema: "public");
        }
    }
}
