import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/config/server_config.dart';
import 'core/router/app_router.dart';
import 'core/theme/app_theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await ServerConfig.load();
  // Tự lấy URL tunnel mới nhất từ Gist (nếu có mạng) trước khi vẽ UI → khỏi nhập tay.
  // Có timeout nội bộ 6s; offline thì bỏ qua, dùng URL đã lưu.
  await ServerConfig.syncFromPointer();
  runApp(const ProviderScope(child: QuantFlowApp()));
}

class QuantFlowApp extends ConsumerWidget {
  const QuantFlowApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(routerProvider);
    return MaterialApp.router(
      title: 'Quant Flow',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.dark,
      routerConfig: router,
    );
  }
}
