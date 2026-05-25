using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionMonitorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloseReason",
                schema: "qfb",
                table: "positions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HighestPriceSinceEntry",
                schema: "qfb",
                table: "positions",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StopLossPrice",
                schema: "qfb",
                table: "positions",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfitPrice",
                schema: "qfb",
                table: "positions",
                type: "numeric(28,12)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TrailingStopPercent",
                schema: "qfb",
                table: "positions",
                type: "numeric(8,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultStopLossPercent",
                schema: "qfb",
                table: "bots",
                type: "numeric(8,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultTakeProfitPercent",
                schema: "qfb",
                table: "bots",
                type: "numeric(8,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultTrailingStopPercent",
                schema: "qfb",
                table: "bots",
                type: "numeric(8,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyPreview",
                schema: "qfb",
                table: "api_keys",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                schema: "qfb",
                table: "api_keys",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastUsedAt",
                schema: "qfb",
                table: "api_keys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastValidatedAt",
                schema: "qfb",
                table: "api_keys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PermissionsJson",
                schema: "qfb",
                table: "api_keys",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "qfb",
                table: "api_keys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_UserId_ExchangeId_Mode_IsActive",
                schema: "qfb",
                table: "api_keys",
                columns: new[] { "UserId", "ExchangeId", "Mode", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_api_keys_UserId_ExchangeId_Mode_IsActive",
                schema: "qfb",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "CloseReason",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "HighestPriceSinceEntry",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "StopLossPrice",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "TakeProfitPrice",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "TrailingStopPercent",
                schema: "qfb",
                table: "positions");

            migrationBuilder.DropColumn(
                name: "DefaultStopLossPercent",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "DefaultTakeProfitPercent",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "DefaultTrailingStopPercent",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "KeyPreview",
                schema: "qfb",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "LastError",
                schema: "qfb",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                schema: "qfb",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "LastValidatedAt",
                schema: "qfb",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "PermissionsJson",
                schema: "qfb",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "qfb",
                table: "api_keys");
        }
    }
}
