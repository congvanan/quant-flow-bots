import 'dart:convert';
import 'package:dio/dio.dart';

import '../network/api_exception.dart';
import 'auth_models.dart';

/// Gọi các endpoint auth của BE. Lưu ý: repository này nhận Dio "trần" (không qua interceptor
/// gắn token) vì login/register chưa có token. Trả về (token, user).
class AuthRepository {
  final Dio _dio;
  AuthRepository(this._dio);

  Future<(String token, AuthUser user)> login(String email, String password) async {
    try {
      final res = await _dio.post('/api/auth/login',
          data: {'email': email, 'password': password});
      final data = res.data as Map<String, dynamic>;
      return (data['accessToken'] as String, AuthUser.fromAuthJson(data));
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  /// Giải mã payload JWT (không verify chữ ký — chỉ để lấy thông tin hiển thị khi bootstrap).
  static AuthUser? userFromJwt(String token) {
    try {
      final parts = token.split('.');
      if (parts.length != 3) return null;
      var payload = parts[1].replaceAll('-', '+').replaceAll('_', '/');
      switch (payload.length % 4) {
        case 2:
          payload += '==';
          break;
        case 3:
          payload += '=';
          break;
      }
      final map = jsonDecode(utf8.decode(base64.decode(payload))) as Map<String, dynamic>;
      final email = (map['email'] ?? map['unique_name'] ?? '').toString();
      return AuthUser(
        id: (map['sub'] ?? '').toString(),
        email: email,
        displayName: (map['displayName'] ?? email).toString(),
      );
    } catch (_) {
      return null;
    }
  }
}
