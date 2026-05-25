using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveTradingF1Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExchangePositionRef",
                schema: "qfb",
                table: "positions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeStopOrderId",
                schema: "qfb",
                table: "positions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeTpOrderId",
                schema: "qfb",
                table: "positions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Leverage",
                schema: "qfb",
                table: "bots",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangePositionRef",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "ExchangeStopOrderId",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "ExchangeTpOrderId",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "Leverage",
                schema: "qfb",
                table: "bots");
        }
    }
}
