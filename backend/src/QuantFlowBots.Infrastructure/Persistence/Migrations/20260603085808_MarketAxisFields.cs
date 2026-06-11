using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketAxisFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextFiltersJson",
                schema: "qfb",
                table: "bots",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionMarket",
                schema: "qfb",
                table: "bots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxBasisPct",
                schema: "qfb",
                table: "bots",
                type: "numeric(8,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TriggerMarket",
                schema: "qfb",
                table: "bots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill: bots đã link tới api key của binance-futures-testnet → ExecutionMarket=Futures(1),
            // TriggerMarket=Futures(1) (v1 force Trigger=Execution). Bot không có ApiKeyId hoặc
            // link tới spot key → giữ default 0 (Spot). Tránh tình trạng futures bot bị dispatcher
            // route nhầm sang spot executor sau khi merge migration.
            migrationBuilder.Sql(@"
                UPDATE qfb.bots
                SET ""ExecutionMarket"" = 1, ""TriggerMarket"" = 1
                WHERE ""ApiKeyId"" IN (
                    SELECT k.""Id"" FROM qfb.api_keys k
                    JOIN qfb.exchanges x ON x.""Id"" = k.""ExchangeId""
                    WHERE x.""Code"" = 'binance-futures-testnet'
                );
            ");

            // Default 0.5% basis guard cho mọi bot hiện hữu (entity default sẽ áp cho row mới).
            migrationBuilder.Sql(@"UPDATE qfb.bots SET ""MaxBasisPct"" = 0.5 WHERE ""MaxBasisPct"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextFiltersJson",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "ExecutionMarket",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "MaxBasisPct",
                schema: "qfb",
                table: "bots");

            migrationBuilder.DropColumn(
                name: "TriggerMarket",
                schema: "qfb",
                table: "bots");
        }
    }
}
