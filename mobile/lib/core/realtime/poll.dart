import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

/// App đang foreground hay không — poll chỉ chạy khi app hiện trên màn hình
/// để khỏi đốt 4G / pin khi người dùng tắt màn hình hoặc chuyển app khác.
final appResumedProvider =
    NotifierProvider<AppResumedNotifier, bool>(AppResumedNotifier.new);

class AppResumedNotifier extends Notifier<bool> {
  AppLifecycleListener? _listener;

  @override
  bool build() {
    _listener = AppLifecycleListener(
      onStateChange: (s) => state = s == AppLifecycleState.resumed,
    );
    ref.onDispose(() => _listener?.dispose());
    return true;
  }
}

/// Gắn vào trong 1 FutureProvider để nó tự refetch theo chu kỳ (giống
/// refetchInterval của React Query bên web).
///
/// - Mỗi lần tới hạn: nếu app đang foreground → [Ref.invalidateSelf] (provider
///   chạy lại body → fetch mới → gọi lại autoPoll, vòng lặp tiếp tục).
/// - Nếu app đang nền: KHÔNG gọi API, chỉ hẹn kiểm tra lại sau đúng [interval].
/// - Timer tự hủy khi provider bị dispose (rời màn hình với autoDispose).
void autoPoll(Ref ref, Duration interval) {
  Timer? timer;
  void schedule() {
    timer = Timer(interval, () {
      if (ref.read(appResumedProvider)) {
        ref.invalidateSelf();
      } else {
        schedule();
      }
    });
  }

  schedule();
  ref.onDispose(() => timer?.cancel());
}
