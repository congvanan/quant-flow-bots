import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import 'bot_models.dart';

/// Đọc bot/positions/orders từ BE. Dùng Dio đã gắn JWT interceptor.
class BotsRepository {
  final Dio _dio;
  BotsRepository(this._dio);

  Future<List<BotDto>> list() => _getList('/api/bots', BotDto.fromJson);

  Future<List<BotStatsRow>> statsSummary() => _getList('/api/bots/stats/summary', BotStatsRow.fromJson);

  Future<BotDto?> byId(String id) async {
    final all = await list();
    for (final b in all) {
      if (b.id == id) return b;
    }
    return null;
  }

  Future<List<PositionDto>> positions(String id) =>
      _getList('/api/bots/$id/positions', PositionDto.fromJson);

  Future<List<OrderDto>> orders(String id) =>
      _getList('/api/bots/$id/orders', OrderDto.fromJson);

  Future<List<RiskEventDto>> riskEvents(String id) =>
      _getList('/api/bots/$id/risk-events', RiskEventDto.fromJson);
  Future<List<SignalDto>> signals(String id) =>
      _getList('/api/bots/$id/signals', SignalDto.fromJson);

  Future<void> start(String id) => _post('/api/bots/$id/start');
  Future<void> stop(String id) => _post('/api/bots/$id/stop');
  Future<void> tripKill(String id, String reason) =>
      _send('POST', '/api/bots/$id/kill-switch', {'reason': reason});
  Future<void> resetKill(String id) => _post('/api/bots/$id/kill-switch/reset');

  Future<void> create(Map<String, dynamic> body) => _send('POST', '/api/bots', body);
  Future<void> updateRisk(String id, Map<String, dynamic> body) => _send('PATCH', '/api/bots/$id/risk', body);
  Future<void> remove(String id) => _send('DELETE', '/api/bots/$id', null);

  // Multi-account
  Future<List<BotAccountDto>> accounts(String id) =>
      _getList('/api/bots/$id/accounts', BotAccountDto.fromJson);
  Future<void> addAccount(String id, Map<String, dynamic> body) => _send('POST', '/api/bots/$id/accounts', body);
  Future<void> updateAccount(String id, String accId, Map<String, dynamic> body) =>
      _send('PATCH', '/api/bots/$id/accounts/$accId', body);
  Future<void> deleteAccount(String id, String accId) => _send('DELETE', '/api/bots/$id/accounts/$accId', null);
  Future<void> resetAccountKill(String id, String accId) =>
      _post('/api/bots/$id/accounts/$accId/kill-switch/reset');

  Future<List<T>> _getList<T>(String path, T Function(Map<String, dynamic>) parse) async {
    try {
      final res = await _dio.get(path);
      final data = res.data as List<dynamic>;
      return data.map((e) => parse(e as Map<String, dynamic>)).toList();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  Future<void> _post(String path) async {
    try {
      await _dio.post(path);
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  Future<void> _send(String method, String path, Object? body) async {
    try {
      await _dio.request(path, data: body, options: Options(method: method));
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}
