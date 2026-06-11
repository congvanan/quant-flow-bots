using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WallAlertTierThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WallAlertMinNotionalLow",
                schema: "qfb",
                table: "user_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WallAlertMinNotionalMid",
                schema: "qfb",
                table: "user_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WallAlertMinNotionalTop",
                schema: "qfb",
                table: "user_settings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            // Backfill cho user cũ: cột mới mặc định 0 sẽ làm worker fallback về MinNotional cũ
            // (giữ hành vi cũ — an toàn). Nhưng nếu user đã bật WallAlert thì preset 3 tier
            // theo default hợp lý (1M/500k/300k) để họ thấy cải thiện ngay, không cần vào lại
            // UI để cấu hình lại.
            migrationBuilder.Sql(@"
                UPDATE qfb.user_settings
                SET ""WallAlertMinNotionalTop"" = 1000000,
                    ""WallAlertMinNotionalMid"" = 500000,
                    ""WallAlertMinNotionalLow"" = 300000
                WHERE ""WallAlertEnabled"" = TRUE
                  AND ""WallAlertMinNotionalTop"" = 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WallAlertMinNotionalLow",
                schema: "qfb",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "WallAlertMinNotionalMid",
                schema: "qfb",
                table: "user_settings");

            migrationBuilder.DropColumn(
                name: "WallAlertMinNotionalTop",
                schema: "qfb",
                table: "user_settings");
        }
    }
}
