using System.Collections.Generic;
using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class UpdateReducerState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ReducerStates",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.DropIndex(
                name: "IX_ReducerStates_Name_Slot",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.DropIndex(
                name: "IX_ReducerStates_Slot",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.DropColumn(
                name: "Slot",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.DropColumn(
                name: "Hash",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.AddColumn<IEnumerable<Point>>(
                name: "LatestIntersections",
                schema: "public",
                table: "ReducerStates",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<Point>(
                name: "StartIntersection",
                schema: "public",
                table: "ReducerStates",
                type: "jsonb",
                nullable: false);

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

            migrationBuilder.DropColumn(
                name: "LatestIntersections",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.DropColumn(
                name: "StartIntersection",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.AddColumn<decimal>(
                name: "Slot",
                schema: "public",
                table: "ReducerStates",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                schema: "public",
                table: "ReducerStates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReducerStates",
                schema: "public",
                table: "ReducerStates",
                columns: new[] { "Name", "Slot" });

            migrationBuilder.CreateIndex(
                name: "IX_ReducerStates_Name_Slot",
                schema: "public",
                table: "ReducerStates",
                columns: new[] { "Name", "Slot" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ReducerStates_Slot",
                schema: "public",
                table: "ReducerStates",
                column: "Slot",
                descending: new bool[0]);
        }
    }
}
