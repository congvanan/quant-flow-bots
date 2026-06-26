import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/network/dio_client.dart';

double? _dn(dynamic v) => v == null ? null : (v is num ? v.toDouble() : double.tryParse('$v'));
double _d(dynamic v) => _dn(v) ?? 0;
int _i(dynamic v) => v == null ? 0 : (v is num ? v.toInt() : int.tryParse('$v') ?? 0);

class BacktestSummary {
  final String id;
  final String strategyKind;
  final String symbolCode;
  final String interval;
  final String status;
  final double? returnPercent;
  final double? maxDrawdownPercent;
  final double? winRatePercent;
  final int? tradeCount;
  final double? finalEquity;
  final DateTime createdAt;
  final String? error;
  BacktestSummary({
    required this.id,
    required this.strategyKind,
    required this.symbolCode,
    required this.interval,
    required this.status,
    required this.returnPercent,
    required this.maxDrawdownPercent,
    required this.winRatePercent,
    required this.tradeCount,
    required this.finalEquity,
    required this.createdAt,
    required this.error,
  });
  bool get done => status == 'Completed';
  factory BacktestSummary.fromJson(Map<String, dynamic> j) => BacktestSummary(
        id: j['id'].toString(),
        strategyKind: (j['strategyKind'] ?? '').toString(),
        symbolCode: (j['symbolCode'] ?? '').toString(),
        interval: (j['interval'] ?? '').toString(),
        status: (j['status'] ?? '').toString(),
        returnPercent: _dn(j['returnPercent']),
        maxDrawdownPercent: _dn(j['maxDrawdownPercent']),
        winRatePercent: _dn(j['winRatePercent']),
        tradeCount: j['tradeCount'] == null ? null : _i(j['tradeCount']),
        finalEquity: _dn(j['finalEquity']),
        createdAt: DateTime.tryParse((j['createdAt'] ?? '').toString()) ?? DateTime.now(),
        error: j['error']?.toString(),
      );
}

class EquityPoint {
  final DateTime at;
  final double equity;
  EquityPoint(this.at, this.equity);
  factory EquityPoint.fromJson(Map<String, dynamic> j) =>
      EquityPoint(DateTime.tryParse((j['at'] ?? '').toString()) ?? DateTime.now(), _d(j['equity']));
}

class BacktestDetail {
  final BacktestSummary summary;
  final List<EquityPoint> equityCurve;
  BacktestDetail(this.summary, this.equityCurve);
  factory BacktestDetail.fromJson(Map<String, dynamic> j) => BacktestDetail(
        BacktestSummary.fromJson(j['summary'] as Map<String, dynamic>),
        ((j['equityCurve'] ?? []) as List).map((e) => EquityPoint.fromJson(e)).toList(),
      );
}

class ScanRow {
  final String symbol;
  final String status;
  final double? returnPercent;
  final int? tradeCount;
  final double? winRatePercent;
  ScanRow(this.symbol, this.status, this.returnPercent, this.tradeCount, this.winRatePercent);
  factory ScanRow.fromJson(Map<String, dynamic> j) => ScanRow(
        (j['symbol'] ?? j['symbolCode'] ?? '').toString(),
        (j['status'] ?? '').toString(),
        _dn(j['returnPercent']),
        j['tradeCount'] == null ? null : _i(j['tradeCount']),
        _dn(j['winRatePercent']),
      );
}

class BacktestRepository {
  final Dio _dio;
  BacktestRepository(this._dio);

  Future<List<BacktestSummary>> list() async {
    final res = await _get('/api/backtests');
    return (res as List).map((e) => BacktestSummary.fromJson(e)).toList();
  }

  Future<BacktestDetail> detail(String id) async {
    final res = await _get('/api/backtests/$id');
    return BacktestDetail.fromJson(res as Map<String, dynamic>);
  }

  Future<void> run(Map<String, dynamic> body) => _send('POST', '/api/backtests', body);
  Future<List<ScanRow>> scan(Map<String, dynamic> body) async {
    final res = await _sendReturn('POST', '/api/backtests/scan', body);
    final data = res is Map ? (res['results'] ?? res['rows'] ?? []) : res;
    return (data as List).map((e) => ScanRow.fromJson(e)).toList();
  }

  Future<void> remove(String id) => _send('DELETE', '/api/backtests/$id', null);
  Future<void> removeFailed() => _send('DELETE', '/api/backtests/failed', null);

  Future<dynamic> _get(String path) async {
    try {
      return (await _dio.get(path)).data;
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  Future<void> _send(String method, String path, Object? body) async {
    await _sendReturn(method, path, body);
  }

  Future<dynamic> _sendReturn(String method, String path, Object? body) async {
    try {
      final res = await _dio.request(path, data: body, options: Options(method: method));
      return res.data;
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final backtestRepositoryProvider =
    Provider<BacktestRepository>((ref) => BacktestRepository(ref.watch(dioProvider)));

final backtestsProvider =
    FutureProvider<List<BacktestSummary>>((ref) => ref.watch(backtestRepositoryProvider).list());

final backtestDetailProvider =
    FutureProvider.family<BacktestDetail, String>((ref, id) => ref.watch(backtestRepositoryProvider).detail(id));
