using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSentimentEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sentiment_events",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SymbolId = table.Column<int>(type: "integer", nullable: true),
                    SymbolCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Headline = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Score = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    Magnitude = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    Tags = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sentiment_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sentiment_events_symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalSchema: "qfb",
                        principalTable: "symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sentiment_events_IngestedAt",
                schema: "qfb",
                table: "sentiment_events",
                column: "IngestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_sentiment_events_SymbolCode_At",
                schema: "qfb",
                table: "sentiment_events",
                columns: new[] { "SymbolCode", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_sentiment_events_SymbolId",
                schema: "qfb",
                table: "sentiment_events",
                column: "SymbolId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sentiment_events",
                schema: "qfb");
        }
    }
}
