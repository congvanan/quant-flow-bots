using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSymbolListedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ListedAt",
                schema: "qfb",
                table: "symbols",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_symbols_ListedAt",
                schema: "qfb",
                table: "symbols",
                column: "ListedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_symbols_ListedAt",
                schema: "qfb",
                table: "symbols");

            migrationBuilder.DropColumn(
                name: "ListedAt",
                schema: "qfb",
                table: "symbols");
        }
    }
}
