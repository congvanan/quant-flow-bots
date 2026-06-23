using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiAccountFanout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                schema: "qfb",
                table: "positions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                schema: "qfb",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bot_accounts",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    BaseEquityUsdt = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    KillSwitchTrippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    KillSwitchReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bot_accounts_api_keys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalSchema: "qfb",
                        principalTable: "api_keys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bot_accounts_bots_BotId",
                        column: x => x.BotId,
                        principalSchema: "qfb",
                        principalTable: "bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_positions_ApiKeyId_Status",
                schema: "qfb",
                table: "positions",
                columns: new[] { "ApiKeyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_ApiKeyId",
                schema: "qfb",
                table: "orders",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_bot_accounts_ApiKeyId",
                schema: "qfb",
                table: "bot_accounts",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_bot_accounts_BotId_ApiKeyId",
                schema: "qfb",
                table: "bot_accounts",
                columns: new[] { "BotId", "ApiKeyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_accounts",
                schema: "qfb");

            migrationBuilder.DropIndex(
                name: "IX_positions_ApiKeyId_Status",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropIndex(
                name: "IX_orders_ApiKeyId",
                schema: "qfb",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                schema: "qfb",
                table: "orders");
        }
    }
}
