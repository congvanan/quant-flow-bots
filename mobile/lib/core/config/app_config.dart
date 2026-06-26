/// Cấu hình môi trường. Dev: điện thoại thật nối Wi-Fi → trỏ IP LAN của PC chạy API.
/// Đổi [devHost] nếu IP máy bạn khác (xem `ipconfig`). Khi build release sẽ thay bằng domain thật.
class AppConfig {
  // Dev qua USB: `adb reverse tcp:5087 tcp:5087` → điện thoại gọi 127.0.0.1 sẽ chạm PC.
  // Bỏ qua Wi-Fi/firewall, chắc ăn nhất khi cắm cáp. Nếu test qua Wi-Fi cùng mạng thì đổi
  // devHost = IP LAN của PC (vd 192.168.x.x); emulator thì 10.0.2.2.
  static const String devHost = '127.0.0.1';
  static const int apiPort = 5087;

  static String get baseUrl => 'http://$devHost:$apiPort';
  static String get hubUrl => '$baseUrl/hubs/market';

  /// Gist công khai (cố định) chứa URL tunnel hiện tại. `start-tunnel.ps1` cập nhật
  /// file này mỗi lần chạy; app đọc lúc mở để tự set server (xem [ServerConfig.syncFromPointer]).
  /// raw không kèm sha → luôn trả bản mới nhất.
  static const String tunnelPointerUrl =
      'https://gist.githubusercontent.com/congvanan/3fa600bc2917d3d21027e2de5b773a06/raw/tunnel.json';

  const AppConfig._();
}
