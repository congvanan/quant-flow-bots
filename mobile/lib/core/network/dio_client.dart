import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config/app_config.dart';
import '../config/server_config.dart';
import '../auth/auth_providers.dart';

/// Dio dùng chung: base URL + interceptor gắn Bearer JWT (đọc từ cache in-memory của
/// TokenStorage nên không async mỗi request). 401 → đẩy sự kiện logout qua authcontroller.
final dioProvider = Provider<Dio>((ref) {
  final dio = Dio(BaseOptions(
    baseUrl: AppConfig.baseUrl,
    connectTimeout: const Duration(seconds: 15),
    receiveTimeout: const Duration(seconds: 20),
    headers: {'Accept': 'application/json'},
  ));

  dio.interceptors.add(InterceptorsWrapper(
    onRequest: (options, handler) {
      // baseUrl đọc động từ ServerConfig → đổi server lúc chạy không cần tạo lại Dio.
      options.baseUrl = ServerConfig.baseUrl;
      final token = ref.read(tokenStorageProvider).cached;
      if (token != null) options.headers['Authorization'] = 'Bearer $token';
      handler.next(options);
    },
    onError: (e, handler) {
      // Token hết hạn / không hợp lệ → buộc đăng xuất (trừ chính endpoint auth).
      final path = e.requestOptions.path;
      if (e.response?.statusCode == 401 && !path.startsWith('/api/auth/')) {
        ref.read(authControllerProvider.notifier).forceLogout();
      }
      handler.next(e);
    },
  ));

  return dio;
});
