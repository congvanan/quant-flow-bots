import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Giữ JWT trong Keychain (iOS) / Keystore-backed (Android). Có cache in-memory để
/// dio interceptor đọc token đồng bộ không phải hit secure storage mỗi request.
class TokenStorage {
  static const _key = 'qfb.jwt';
  final FlutterSecureStorage _storage;
  String? _cached;

  TokenStorage([FlutterSecureStorage? storage])
      : _storage = storage ?? const FlutterSecureStorage();

  String? get cached => _cached;

  Future<String?> load() async {
    _cached = await _storage.read(key: _key);
    return _cached;
  }

  Future<void> save(String token) async {
    _cached = token;
    await _storage.write(key: _key, value: token);
  }

  Future<void> clear() async {
    _cached = null;
    await _storage.delete(key: _key);
  }
}
