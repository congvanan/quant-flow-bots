using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSymbolMinNotionalAndUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FiltersUpdatedAt",
                schema: "qfb",
                table: "symbols",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinNotional",
                schema: "qfb",
                table: "symbols",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "user_settings",
                schema: "qfb",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramBotToken = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TelegramChatId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TelegramAlertsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_settings", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_settings",
                schema: "qfb");

            migrationBuilder.DropColumn(
                name: "FiltersUpdatedAt",
                schema: "qfb",
                table: "symbols");

            migrationBuilder.DropColumn(
                name: "MinNotional",
                schema: "qfb",
                table: "symbols");
        }
    }
}
