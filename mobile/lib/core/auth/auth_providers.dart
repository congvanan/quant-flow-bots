import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config/server_config.dart';
import 'auth_models.dart';
import 'auth_repository.dart';
import 'token_storage.dart';

/// Lưu JWT (1 instance dùng chung toàn app).
final tokenStorageProvider = Provider<TokenStorage>((ref) => TokenStorage());

/// Dio "trần" cho riêng auth (login/register chưa có token → không gắn token).
/// QUAN TRỌNG: baseUrl phải đọc [ServerConfig.baseUrl] ĐỘNG mỗi request, vì người
/// dùng có thể đổi server ở màn login. Cold-start API ~30s nên timeout để rộng.
final _rawDioProvider = Provider<Dio>((ref) {
  final dio = Dio(BaseOptions(
    baseUrl: ServerConfig.baseUrl,
    connectTimeout: const Duration(seconds: 20),
    receiveTimeout: const Duration(seconds: 40),
    headers: {'Accept': 'application/json'},
  ));
  dio.interceptors.add(InterceptorsWrapper(
    onRequest: (options, handler) {
      options.baseUrl = ServerConfig.baseUrl;
      handler.next(options);
    },
  ));
  return dio;
});

final authRepositoryProvider =
    Provider<AuthRepository>((ref) => AuthRepository(ref.read(_rawDioProvider)));

final authControllerProvider =
    NotifierProvider<AuthController, AuthState>(AuthController.new);

class AuthController extends Notifier<AuthState> {
  @override
  AuthState build() {
    _bootstrap();
    return const AuthState(loading: true);
  }

  Future<void> _bootstrap() async {
    final token = await ref.read(tokenStorageProvider).load();
    if (token == null) {
      state = const AuthState(loading: false);
      return;
    }
    final user = AuthRepository.userFromJwt(token) ??
        const AuthUser(id: '', email: '', displayName: '');
    state = AuthState(loading: false, user: user);
  }

  Future<void> login(String email, String password) async {
    final (token, user) = await ref.read(authRepositoryProvider).login(email, password);
    await ref.read(tokenStorageProvider).save(token);
    state = AuthState(loading: false, user: user);
  }

  Future<void> logout() async {
    await ref.read(tokenStorageProvider).clear();
    state = const AuthState(loading: false);
  }

  /// Gọi từ dio interceptor khi gặp 401 — xoá token, đẩy về màn login.
  void forceLogout() {
    ref.read(tokenStorageProvider).clear();
    state = const AuthState(loading: false);
  }
}
