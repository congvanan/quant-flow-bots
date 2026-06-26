import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import 'theme/app_theme.dart';

/// Tiện ích format số/tiền/%/ngày dùng chung toàn app.
class Fmt {
  static final _money = NumberFormat('#,##0.00', 'en_US');
  static final _money0 = NumberFormat('#,##0', 'en_US');
  static final _dt = DateFormat('HH:mm dd/MM');
  static final _time = DateFormat('HH:mm:ss');

  static String usd(num v, {int decimals = 2}) =>
      '\$${(decimals == 0 ? _money0 : _money).format(v)}';

  static String num2(num v, {int max = 6}) {
    final s = v.toStringAsFixed(max);
    return s.contains('.') ? s.replaceAll(RegExp(r'0+$'), '').replaceAll(RegExp(r'\.$'), '') : s;
  }

  static String pct(num v, {int decimals = 2, bool sign = true}) {
    final s = '${v.toStringAsFixed(decimals)}%';
    return sign && v > 0 ? '+$s' : s;
  }

  static String signedUsd(num v) => '${v >= 0 ? '+' : ''}${usd(v)}';

  /// Rút gọn USD lớn: $1.23B / $45.6M / $789K. Dùng cho market cap, volume.
  static String compact(num v) {
    if (v <= 0 || v.isNaN || v.isInfinite) return '—';
    if (v >= 1e9) return '\$${(v / 1e9).toStringAsFixed(2)}B';
    if (v >= 1e6) return '\$${(v / 1e6).toStringAsFixed(2)}M';
    if (v >= 1e3) return '\$${(v / 1e3).toStringAsFixed(2)}K';
    return '\$${v.toStringAsFixed(0)}';
  }

  /// Rút gọn số đếm (holders): 1.2M / 45.6K. Không có dấu $.
  static String compactNum(num v) {
    if (v <= 0) return '—';
    if (v >= 1e6) return '${(v / 1e6).toStringAsFixed(2)}M';
    if (v >= 1e3) return '${(v / 1e3).toStringAsFixed(1)}K';
    return v.toStringAsFixed(0);
  }

  static String dt(DateTime d) => _dt.format(d.toLocal());
  static String time(DateTime d) => _time.format(d.toLocal());

  static Color pnlColor(num v) => v > 0 ? AppTheme.up : (v < 0 ? AppTheme.down : Colors.grey);
}
