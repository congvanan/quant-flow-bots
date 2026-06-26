import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/network/dio_client.dart';

class ApiKeyDto {
  final String id;
  final int exchangeId;
  final String exchangeCode;
  final String label;
  final String keyPreview;
  final String mode;
  final bool isActive;
  final String? lastError;
  ApiKeyDto(this.id, this.exchangeId, this.exchangeCode, this.label, this.keyPreview, this.mode, this.isActive, this.lastError);
  factory ApiKeyDto.fromJson(Map<String, dynamic> j) => ApiKeyDto(
        j['id'].toString(),
        (j['exchangeId'] ?? 0) is num ? (j['exchangeId'] as num).toInt() : 0,
        (j['exchangeCode'] ?? '').toString(),
        (j['label'] ?? '').toString(),
        (j['keyPreview'] ?? '').toString(),
        (j['mode'] ?? '').toString(),
        j['isActive'] == true,
        j['lastError']?.toString(),
      );
}

class ExchangeDto {
  final int id;
  final String code;
  final String name;
  ExchangeDto(this.id, this.code, this.name);
  factory ExchangeDto.fromJson(Map<String, dynamic> j) =>
      ExchangeDto((j['id'] as num).toInt(), (j['code'] ?? '').toString(), (j['name'] ?? '').toString());
}

class SettingsRepository {
  final Dio _dio;
  SettingsRepository(this._dio);

  Future<List<ApiKeyDto>> apiKeys() async {
    final res = await _get('/api/settings/api-keys');
    return (res as List).map((e) => ApiKeyDto.fromJson(e)).toList();
  }

  Future<List<ExchangeDto>> exchanges() async {
    final res = await _get('/api/settings/exchanges');
    return (res as List).map((e) => ExchangeDto.fromJson(e)).toList();
  }

  Future<void> createKey(Map<String, dynamic> body) => _send('POST', '/api/settings/api-keys', body);
  Future<void> deleteKey(String id) => _send('DELETE', '/api/settings/api-keys/$id', null);
  Future<void> validateKey(String id) => _send('POST', '/api/settings/api-keys/$id/validate', null);
  Future<void> toggleKey(String id, bool active) =>
      _send('POST', '/api/settings/api-keys/$id/${active ? 'deactivate' : 'activate'}', null);

  Future<dynamic> _get(String path) async {
    try {
      return (await _dio.get(path)).data;
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

final settingsRepositoryProvider =
    Provider<SettingsRepository>((ref) => SettingsRepository(ref.watch(dioProvider)));

final apiKeysProvider =
    FutureProvider<List<ApiKeyDto>>((ref) => ref.watch(settingsRepositoryProvider).apiKeys());

final exchangesProvider =
    FutureProvider<List<ExchangeDto>>((ref) => ref.watch(settingsRepositoryProvider).exchanges());
