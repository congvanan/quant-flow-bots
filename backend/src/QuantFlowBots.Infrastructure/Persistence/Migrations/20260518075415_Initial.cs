using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QuantFlowBots.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "qfb");

            migrationBuilder.CreateTable(
                name: "exchanges",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RestBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    WebSocketBaseUrl = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "symbols",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExchangeId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BaseAsset = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QuoteAsset = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MinQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    TickSize = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    StepSize = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_symbols", x => x.Id);
                    table.ForeignKey(
                        name: "FK_symbols_exchanges_ExchangeId",
                        column: x => x.ExchangeId,
                        principalSchema: "qfb",
                        principalTable: "exchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_claims_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "qfb",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExchangeId = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EncryptedKey = table.Column<string>(type: "text", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "text", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_api_keys_exchanges_ExchangeId",
                        column: x => x.ExchangeId,
                        principalSchema: "qfb",
                        principalTable: "exchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_api_keys_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "qfb",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "strategies",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_strategies_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "qfb",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_claims_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "qfb",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "qfb",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_logins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_user_logins_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "qfb",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "qfb",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "qfb",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "qfb",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "qfb",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_user_tokens_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "qfb",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "candles",
                schema: "qfb",
                columns: table => new
                {
                    OpenTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SymbolId = table.Column<int>(type: "integer", nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    Open = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    High = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Low = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Close = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Volume = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    QuoteVolume = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    TradeCount = table.Column<int>(type: "integer", nullable: false),
                    CloseTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candles", x => new { x.SymbolId, x.Interval, x.OpenTime });
                    table.ForeignKey(
                        name: "FK_candles_symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalSchema: "qfb",
                        principalTable: "symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "backtests",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SymbolId = table.Column<int>(type: "integer", nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    FromTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ToTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InitialCapital = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backtests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_backtests_strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalSchema: "qfb",
                        principalTable: "strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_backtests_symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalSchema: "qfb",
                        principalTable: "symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_backtests_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "qfb",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bots",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SymbolId = table.Column<int>(type: "integer", nullable: false),
                    ExchangeId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    MaxPositionSize = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    DailyLossStopPercent = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    MaxOpenPositions = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bots_exchanges_ExchangeId",
                        column: x => x.ExchangeId,
                        principalSchema: "qfb",
                        principalTable: "exchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bots_strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalSchema: "qfb",
                        principalTable: "strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bots_symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalSchema: "qfb",
                        principalTable: "symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bots_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "qfb",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "signals",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SymbolId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: true),
                    Price = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(10,4)", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_signals_strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalSchema: "qfb",
                        principalTable: "strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_signals_symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalSchema: "qfb",
                        principalTable: "symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bot_runs",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StoppedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StopReason = table.Column<string>(type: "text", nullable: true),
                    RealizedPnl = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    OrderCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bot_runs_bots_BotId",
                        column: x => x.BotId,
                        principalSchema: "qfb",
                        principalTable: "bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SymbolId = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    FilledQuantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    AveragePrice = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    Commission = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ClientOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExchangeOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RejectReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_orders_bot_runs_BotRunId",
                        column: x => x.BotRunId,
                        principalSchema: "qfb",
                        principalTable: "bot_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_orders_bots_BotId",
                        column: x => x.BotId,
                        principalSchema: "qfb",
                        principalTable: "bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_orders_symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalSchema: "qfb",
                        principalTable: "symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "qfb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BotId = table.Column<Guid>(type: "uuid", nullable: false),
                    BotRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SymbolId = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Side = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "numeric(28,12)", nullable: true),
                    RealizedPnl = table.Column<decimal>(type: "numeric(28,12)", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_positions_bot_runs_BotRunId",
                        column: x => x.BotRunId,
                        principalSchema: "qfb",
                        principalTable: "bot_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_positions_bots_BotId",
                        column: x => x.BotId,
                        principalSchema: "qfb",
                        principalTable: "bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_positions_symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalSchema: "qfb",
                        principalTable: "symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_ExchangeId",
                schema: "qfb",
                table: "api_keys",
                column: "ExchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_UserId_ExchangeId_Label",
                schema: "qfb",
                table: "api_keys",
                columns: new[] { "UserId", "ExchangeId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_backtests_StrategyId",
                schema: "qfb",
                table: "backtests",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_backtests_SymbolId",
                schema: "qfb",
                table: "backtests",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_backtests_UserId_CreatedAt",
                schema: "qfb",
                table: "backtests",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_bot_runs_BotId_StartedAt",
                schema: "qfb",
                table: "bot_runs",
                columns: new[] { "BotId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_bots_ExchangeId",
                schema: "qfb",
                table: "bots",
                column: "ExchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_bots_StrategyId",
                schema: "qfb",
                table: "bots",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_bots_SymbolId",
                schema: "qfb",
                table: "bots",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_bots_UserId_State",
                schema: "qfb",
                table: "bots",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_candles_SymbolId_Interval_OpenTime",
                schema: "qfb",
                table: "candles",
                columns: new[] { "SymbolId", "Interval", "OpenTime" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_exchanges_Code",
                schema: "qfb",
                table: "exchanges",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_BotId_CreatedAt",
                schema: "qfb",
                table: "orders",
                columns: new[] { "BotId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_BotRunId",
                schema: "qfb",
                table: "orders",
                column: "BotRunId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_ClientOrderId",
                schema: "qfb",
                table: "orders",
                column: "ClientOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_SymbolId",
                schema: "qfb",
                table: "orders",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_positions_BotId_Status",
                schema: "qfb",
                table: "positions",
                columns: new[] { "BotId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_positions_BotRunId",
                schema: "qfb",
                table: "positions",
                column: "BotRunId");

            migrationBuilder.CreateIndex(
                name: "IX_positions_SymbolId",
                schema: "qfb",
                table: "positions",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_role_claims_RoleId",
                schema: "qfb",
                table: "role_claims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "qfb",
                table: "roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_signals_StrategyId_GeneratedAt",
                schema: "qfb",
                table: "signals",
                columns: new[] { "StrategyId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_signals_SymbolId",
                schema: "qfb",
                table: "signals",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_strategies_UserId_Name",
                schema: "qfb",
                table: "strategies",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_symbols_ExchangeId_Code",
                schema: "qfb",
                table: "symbols",
                columns: new[] { "ExchangeId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_claims_UserId",
                schema: "qfb",
                table: "user_claims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_logins_UserId",
                schema: "qfb",
                table: "user_logins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_RoleId",
                schema: "qfb",
                table: "user_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "qfb",
                table: "users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "qfb",
                table: "users",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "backtests",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "candles",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "signals",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "bot_runs",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "bots",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "strategies",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "symbols",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "users",
                schema: "qfb");

            migrationBuilder.DropTable(
                name: "exchanges",
                schema: "qfb");
        }
    }
}
