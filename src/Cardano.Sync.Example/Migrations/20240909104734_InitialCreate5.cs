using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cardano.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_reducer_state",
                schema: "public",
                table: "reducer_state");

            migrationBuilder.RenameTable(
                name: "reducer_state",
                schema: "public",
                newName: "ReducerStates",
                newSchema: "public");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReducerStates",
                schema: "public",
                table: "ReducerStates",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ReducerStates",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.RenameTable(
                name: "ReducerStates",
                schema: "public",
                newName: "reducer_state",
                newSchema: "public");

            migrationBuilder.AddPrimaryKey(
                name: "PK_reducer_state",
                schema: "public",
                table: "reducer_state",
                column: "Name");
        }
    }
}
