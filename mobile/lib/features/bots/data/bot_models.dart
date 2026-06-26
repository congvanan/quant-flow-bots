// Mô hình tối thiểu cho Phase 1 (mirror api.ts của web, JSON camelCase từ BE).
// Khi parity rộng hơn sẽ cân nhắc sinh tự động từ OpenAPI.

double _d(dynamic v) => v == null ? 0 : (v is num ? v.toDouble() : double.tryParse('$v') ?? 0);
int _i(dynamic v) => v == null ? 0 : (v is num ? v.toInt() : int.tryParse('$v') ?? 0);

class BotDto {
  final String id;
  final String name;
  final String mode;
  final String state;
  final String kind;
  final String runMode;
  final String executionMarket;
  final String symbolCode;
  final String? strategyKind;
  final String strategyId;
  final String? apiKeyId;
  final double baseEquityUsdt;
  final int leverage;
  final DateTime? killSwitchTrippedAt;
  final String? killSwitchReason;
  final String? lastError;

  BotDto({
    required this.id,
    required this.name,
    required this.mode,
    required this.state,
    required this.kind,
    required this.runMode,
    required this.executionMarket,
    required this.symbolCode,
    required this.strategyKind,
    required this.strategyId,
    required this.apiKeyId,
    required this.baseEquityUsdt,
    required this.leverage,
    required this.killSwitchTrippedAt,
    required this.killSwitchReason,
    required this.lastError,
  });

  factory BotDto.fromJson(Map<String, dynamic> j) => BotDto(
        id: j['id'].toString(),
        name: (j['name'] ?? '').toString(),
        mode: (j['mode'] ?? '').toString(),
        state: (j['state'] ?? '').toString(),
        kind: (j['kind'] ?? '').toString(),
        runMode: (j['runMode'] ?? '').toString(),
        executionMarket: (j['executionMarket'] ?? 'Spot').toString(),
        symbolCode: (j['symbolCode'] ?? '').toString(),
        strategyKind: j['strategyKind']?.toString(),
        strategyId: (j['strategyId'] ?? '').toString(),
        apiKeyId: j['apiKeyId']?.toString(),
        baseEquityUsdt: _d(j['baseEquityUsdt']),
        leverage: _i(j['leverage']),
        killSwitchTrippedAt: j['killSwitchTrippedAt'] == null
            ? null
            : DateTime.tryParse(j['killSwitchTrippedAt'].toString()),
        killSwitchReason: j['killSwitchReason']?.toString(),
        lastError: j['lastError']?.toString(),
      );

  bool get isRunning => state == 'Running';
  bool get killSwitchTripped => killSwitchTrippedAt != null;
}

class BotStatsRow {
  final String botId;
  final String name;
  final String symbolCode;
  final String runMode;
  final String state;
  final double currentEquity;
  final double totalRealizedPnl;
  final double totalReturnPercent;
  final double pnlToday;
  final double winRatePercent;
  final int openPositions;
  BotStatsRow({
    required this.botId,
    required this.name,
    required this.symbolCode,
    required this.runMode,
    required this.state,
    required this.currentEquity,
    required this.totalRealizedPnl,
    required this.totalReturnPercent,
    required this.pnlToday,
    required this.winRatePercent,
    required this.openPositions,
  });
  bool get isRunning => state == 'Running';
  factory BotStatsRow.fromJson(Map<String, dynamic> j) => BotStatsRow(
        botId: j['botId'].toString(),
        name: (j['name'] ?? '').toString(),
        symbolCode: (j['symbolCode'] ?? '').toString(),
        runMode: (j['runMode'] ?? '').toString(),
        state: (j['state'] ?? '').toString(),
        currentEquity: _d(j['currentEquity']),
        totalRealizedPnl: _d(j['totalRealizedPnl']),
        totalReturnPercent: _d(j['totalReturnPercent']),
        pnlToday: _d(j['pnlToday']),
        winRatePercent: _d(j['winRatePercent']),
        openPositions: _i(j['openPositions']),
      );
}

class PositionDto {
  final String id;
  final String side;
  final String status;
  final double quantity;
  final double entryPrice;
  final double? exitPrice;
  final double realizedPnl;
  final String? closeReason;
  final DateTime openedAt;
  final DateTime? closedAt;

  PositionDto({
    required this.id,
    required this.side,
    required this.status,
    required this.quantity,
    required this.entryPrice,
    required this.exitPrice,
    required this.realizedPnl,
    required this.closeReason,
    required this.openedAt,
    required this.closedAt,
  });

  bool get isOpen => status == 'Open';

  factory PositionDto.fromJson(Map<String, dynamic> j) => PositionDto(
        id: j['id'].toString(),
        side: (j['side'] ?? '').toString(),
        status: (j['status'] ?? '').toString(),
        quantity: _d(j['quantity']),
        entryPrice: _d(j['entryPrice']),
        exitPrice: j['exitPrice'] == null ? null : _d(j['exitPrice']),
        realizedPnl: _d(j['realizedPnl']),
        closeReason: j['closeReason']?.toString(),
        openedAt: DateTime.tryParse((j['openedAt'] ?? '').toString()) ?? DateTime.now(),
        closedAt: j['closedAt'] == null ? null : DateTime.tryParse(j['closedAt'].toString()),
      );
}

class BotAccountDto {
  final String id;
  final String apiKeyId;
  final String exchangeCode;
  final String keyLabel;
  final String label;
  final double weight;
  final double baseEquityUsdt;
  final bool isActive;
  final DateTime? killSwitchTrippedAt;
  final String? killSwitchReason;
  final int openPositions;
  final double realizedPnl;
  final double pnlToday;
  final double winRatePercent;
  BotAccountDto({
    required this.id,
    required this.apiKeyId,
    required this.exchangeCode,
    required this.keyLabel,
    required this.label,
    required this.weight,
    required this.baseEquityUsdt,
    required this.isActive,
    required this.killSwitchTrippedAt,
    required this.killSwitchReason,
    required this.openPositions,
    required this.realizedPnl,
    required this.pnlToday,
    required this.winRatePercent,
  });
  bool get killed => killSwitchTrippedAt != null;
  factory BotAccountDto.fromJson(Map<String, dynamic> j) => BotAccountDto(
        id: j['id'].toString(),
        apiKeyId: j['apiKeyId'].toString(),
        exchangeCode: (j['exchangeCode'] ?? '').toString(),
        keyLabel: (j['keyLabel'] ?? '').toString(),
        label: (j['label'] ?? '').toString(),
        weight: _d(j['weight']),
        baseEquityUsdt: _d(j['baseEquityUsdt']),
        isActive: j['isActive'] == true,
        killSwitchTrippedAt: j['killSwitchTrippedAt'] == null ? null : DateTime.tryParse(j['killSwitchTrippedAt'].toString()),
        killSwitchReason: j['killSwitchReason']?.toString(),
        openPositions: _i(j['openPositions']),
        realizedPnl: _d(j['realizedPnl']),
        pnlToday: _d(j['pnlToday']),
        winRatePercent: _d(j['winRatePercent']),
      );
}

class RiskEventDto {
  final String id;
  final String eventType;
  final String severity;
  final String message;
  final DateTime createdAt;
  RiskEventDto(this.id, this.eventType, this.severity, this.message, this.createdAt);
  factory RiskEventDto.fromJson(Map<String, dynamic> j) => RiskEventDto(
        j['id'].toString(),
        (j['eventType'] ?? '').toString(),
        (j['severity'] ?? '').toString(),
        (j['message'] ?? '').toString(),
        DateTime.tryParse((j['createdAt'] ?? '').toString()) ?? DateTime.now(),
      );
}

class SignalDto {
  final String id;
  final String type;
  final String? side;
  final double price;
  final double score;
  final DateTime generatedAt;
  SignalDto(this.id, this.type, this.side, this.price, this.score, this.generatedAt);
  factory SignalDto.fromJson(Map<String, dynamic> j) => SignalDto(
        j['id'].toString(),
        (j['type'] ?? '').toString(),
        j['side']?.toString(),
        _d(j['price']),
        _d(j['score']),
        DateTime.tryParse((j['generatedAt'] ?? '').toString()) ?? DateTime.now(),
      );
}

class OrderDto {
  final String id;
  final String side;
  final String status;
  final double price;
  final double quantity;
  final double realizedPnl;
  final DateTime createdAt;

  OrderDto({
    required this.id,
    required this.side,
    required this.status,
    required this.price,
    required this.quantity,
    required this.realizedPnl,
    required this.createdAt,
  });

  factory OrderDto.fromJson(Map<String, dynamic> j) => OrderDto(
        id: j['id'].toString(),
        side: (j['side'] ?? '').toString(),
        status: (j['status'] ?? '').toString(),
        price: _d(j['price']),
        quantity: _d(j['quantity']),
        realizedPnl: _d(j['realizedPnl']),
        createdAt: DateTime.tryParse((j['createdAt'] ?? '').toString()) ?? DateTime.now(),
      );
}
