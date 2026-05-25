using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaseEquityUsdt",
                schema: "qfb",
                table: "bots",
                type: "numeric(28,12)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CooldownAfterLossMinutes",
                schema: "qfb",
                table: "bots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "KillSwitchEnabled",
                schema: "qfb",
                table: "bots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "KillSwitchReason",
                schema: "qfb",
                table: "bots",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KillSwitchTrippedAt",
                schema: "qfb",
                table: "bots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxConsecutiveLosses",
                schema: "qfb",
                table: "bots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskPerTradePercent",
                schema: "qfb",
                table: "bots",
                type: "numeric(8,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "risk_events",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ActionTaken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_risk_events_bots_BotId",
                        column: x => x.BotId,
                        principalSchema: "qfb",
                        principalTable: "bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_risk_events_BotId_CreatedAt",
                schema: "qfb",
                table: "risk_events",
                columns: new[] { "BotId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_events_UserId_CreatedAt",
                schema: "qfb",
                table: "risk_events",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "risk_events",
                schema: "qfb");

            migrationBuilder.DropColumn(
                name: "BaseEquityUsdt",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "CooldownAfterLossMinutes",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "KillSwitchEnabled",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "KillSwitchReason",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "KillSwitchTrippedAt",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "MaxConsecutiveLosses",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "RiskPerTradePercent",
                schema: "qfb",
                table: "bots");
        }
    }
}
