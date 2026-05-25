using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunModeAndSymbolFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RunMode",
                schema: "qfb",
                table: "bots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SymbolFilterJson",
                schema: "qfb",
                table: "bots",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RunMode",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "SymbolFilterJson",
                schema: "qfb",
                table: "bots");
        }
    }
}
