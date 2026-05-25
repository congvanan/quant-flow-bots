using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBotApiKeyLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                schema: "qfb",
                table: "bots",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_bots_ApiKeyId",
                schema: "qfb",
                table: "bots",
                column: "ApiKeyId");

            migrationBuilder.AddForeignKey(
                name: "FK_bots_api_keys_ApiKeyId",
                schema: "qfb",
                table: "bots",
                column: "ApiKeyId",
                principalSchema: "qfb",
                principalTable: "api_keys",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bots_api_keys_ApiKeyId",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropIndex(
                name: "IX_bots_ApiKeyId",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                schema: "qfb",
                table: "bots");
        }
    }
}
