using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTpAndBreakEven : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BreakEvenTriggered",
                schema: "qfb",
                table: "positions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalQuantity",
                schema: "qfb",
                table: "positions",
                type: "numeric(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TakeProfitLevelsJson",
                schema: "qfb",
                table: "positions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AtrMultiplier",
                schema: "qfb",
                table: "bots",
                type: "numeric(8,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "AtrPeriod",
                schema: "qfb",
                table: "bots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "BreakEvenEnabled",
                schema: "qfb",
                table: "bots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "BreakEvenOffsetPercent",
                schema: "qfb",
                table: "bots",
                type: "numeric(8,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BreakEvenTriggerPercent",
                schema: "qfb",
                table: "bots",
                type: "numeric(8,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StopLossKind",
                schema: "qfb",
                table: "bots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TakeProfitLevelsJson",
                schema: "qfb",
                table: "bots",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreakEvenTriggered",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "OriginalQuantity",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "TakeProfitLevelsJson",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "AtrMultiplier",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "AtrPeriod",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "BreakEvenEnabled",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "BreakEvenOffsetPercent",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "BreakEvenTriggerPercent",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "StopLossKind",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "TakeProfitLevelsJson",
                schema: "qfb",
                table: "bots");
        }
    }
}
