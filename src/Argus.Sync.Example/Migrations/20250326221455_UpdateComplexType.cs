using System.Collections.Generic;
using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Sync.Example.Migrations
{
    /// <inheritdoc />
    public partial class UpdateComplexType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LatestIntersections",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.DropColumn(
                name: "StartIntersection",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.AddColumn<string>(
                name: "LatestIntersectionsJson",
                schema: "public",
                table: "ReducerStates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StartIntersectionJson",
                schema: "public",
                table: "ReducerStates",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LatestIntersectionsJson",
                schema: "public",
                table: "ReducerStates");

            migrationBuilder.DropColumn(
                name: "StartIntersectionJson",
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
        }
    }
}
