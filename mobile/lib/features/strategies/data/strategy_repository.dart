import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/network/dio_client.dart';

double _d(dynamic v) => v == null ? 0 : (v is num ? v.toDouble() : double.tryParse('$v') ?? 0);

class StrategyDto {
  final String id;
  final String name;
  final String kind;
  final String parametersJson;
  final int runningBotCount;
  StrategyDto(this.id, this.name, this.kind, this.parametersJson, this.runningBotCount);
  factory StrategyDto.fromJson(Map<String, dynamic> j) => StrategyDto(
        j['id'].toString(),
        (j['name'] ?? '').toString(),
        (j['kind'] ?? '').toString(),
        (j['parametersJson'] ?? '{}').toString(),
        (_d(j['runningBotCount'])).toInt(),
      );
}

class StrategyRepository {
  final Dio _dio;
  StrategyRepository(this._dio);

  Future<List<StrategyDto>> list() async {
    final res = await _get('/api/strategies');
    return (res as List).map((e) => StrategyDto.fromJson(e)).toList();
  }

  Future<List<String>> kinds() async {
    final res = await _get('/api/strategies/kinds');
    return (res as List).map((e) => e.toString()).toList();
  }

  Future<void> create(String name, String kind, String parametersJson) =>
      _send('POST', '/api/strategies', {'name': name, 'kind': kind, 'parametersJson': parametersJson});

  Future<void> update(String id, String name, String parametersJson) =>
      _send('PATCH', '/api/strategies/$id', {'name': name, 'parametersJson': parametersJson});

  Future<void> remove(String id) => _send('DELETE', '/api/strategies/$id', null);

  Future<dynamic> _get(String path) async {
    try {
      return (await _dio.get(path)).data;
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  Future<void> _send(String method, String path, Object? body) async {
    try {
      await _dio.request(path,
          data: body, options: Options(method: method));
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}

final strategyRepositoryProvider =
    Provider<StrategyRepository>((ref) => StrategyRepository(ref.watch(dioProvider)));

final strategiesProvider =
    FutureProvider<List<StrategyDto>>((ref) => ref.watch(strategyRepositoryProvider).list());

final strategyKindsProvider =
    FutureProvider<List<String>>((ref) => ref.watch(strategyRepositoryProvider).kinds());
