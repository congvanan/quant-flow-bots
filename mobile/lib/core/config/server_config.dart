import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'app_config.dart';

/// Base URL của API có thể đổi lúc chạy (không cần build lại). Cho phép trỏ:
///  - USB:        http://127.0.0.1:5087   (adb reverse)
///  - Cùng Wi-Fi: http://192.168.x.x:5087 (IP LAN của PC)
///  - Internet:   https://abc.trycloudflare.com  (tunnel) hoặc domain cloud
class ServerConfig {
  static const _key = 'qfb.serverBase';
  static String baseUrl = AppConfig.baseUrl;

  static Future<void> load() async {
    final p = await SharedPreferences.getInstance();
    final v = p.getString(_key);
    if (v != null && v.trim().isNotEmpty) baseUrl = v.trim();
  }

  static Future<void> save(String url) async {
    baseUrl = url.trim();
    final p = await SharedPreferences.getInstance();
    await p.setString(_key, baseUrl);
  }

  static String get hubUrl => '$baseUrl/hubs/market';

  /// Tự đọc URL tunnel mới nhất từ Gist ([AppConfig.tunnelPointerUrl]) rồi lưu nếu khác.
  /// `start-tunnel.ps1` ghi URL vào Gist mỗi lần chạy → app khỏi phải nhập tay.
  /// Trả về true nếu baseUrl được cập nhật. Offline/lỗi → false, giữ nguyên URL cũ.
  static Future<bool> syncFromPointer() async {
    try {
      final dio = Dio(BaseOptions(
        connectTimeout: const Duration(seconds: 6),
        receiveTimeout: const Duration(seconds: 6),
      ));
      final res = await dio.get<String>(
        AppConfig.tunnelPointerUrl,
        // cache-bust để CDN của Gist không trả bản cũ
        queryParameters: {'t': DateTime.now().millisecondsSinceEpoch},
        options: Options(responseType: ResponseType.plain),
      );
      final raw = res.data;
      if (raw == null || raw.isEmpty) return false;
      final map = jsonDecode(raw) as Map<String, dynamic>;
      final url = (map['baseUrl'] ?? '').toString().trim();
      if (url.isEmpty || url == baseUrl) return false;
      await save(url);
      return true;
    } catch (_) {
      return false;
    }
  }
}
