import 'package:dio/dio.dart';

/// Lỗi API đã chuẩn hoá để UI hiển thị message thật. Bắt đúng quy ước BE:
/// `{ error: "..." }` (singular) hoặc `{ message: "..." }` hoặc `{ errors: {...} }`.
class ApiException implements Exception {
  final int statusCode;
  final String message;
  ApiException(this.statusCode, this.message);

  factory ApiException.fromDio(DioException e) {
    final res = e.response;
    final status = res?.statusCode ?? 0;
    final data = res?.data;
    if (data is Map) {
      final err = data['error'];
      if (err is String && err.isNotEmpty) return ApiException(status, err);
      final msg = data['message'];
      if (msg is String && msg.isNotEmpty) return ApiException(status, msg);
      final errors = data['errors'];
      if (errors != null) return ApiException(status, errors.toString());
    }
    if (e.type == DioExceptionType.connectionTimeout ||
        e.type == DioExceptionType.receiveTimeout ||
        e.type == DioExceptionType.connectionError) {
      return ApiException(0, 'Không kết nối được máy chủ. Kiểm tra mạng / server.');
    }
    // Có status nhưng body không phải JSON chuẩn → message gọn theo mã, không phô exception thô.
    if (status >= 500) return ApiException(status, 'Lỗi máy chủ ($status)');
    if (status >= 400) return ApiException(status, 'Yêu cầu lỗi ($status)');
    return ApiException(status, 'Lỗi kết nối');
  }

  @override
  String toString() => message;
}
