using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeySettingsMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyPreview",
                schema: "qfb",
                table: "api_keys",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

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
                name: "LastError",
                schema: "qfb",
                table: "api_keys",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PermissionsJson",
                schema: "qfb",
                table: "api_keys",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "qfb",
                table: "api_keys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

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
                name: "KeyPreview",
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
                name: "LastError",
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
