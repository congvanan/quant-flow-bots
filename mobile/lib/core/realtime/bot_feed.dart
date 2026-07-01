import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../../features/bots/application/bots_providers.dart';
import '../../features/market/application/market_providers.dart';
import '../auth/auth_providers.dart';
import '../config/server_config.dart';

/// Feed SignalR mức app: nghe event 'bot' (backend đẩy lifecycle start/stop tới group
/// user qua Clients.User) rồi tự refresh list/dashboard/chi tiết khi có thay đổi từ
/// THIẾT BỊ KHÁC (vd thao tác trên web). Bù cho việc list/dashboard là FutureProvider
/// tĩnh, không tự cập nhật. Watch ở HomeShell để sống suốt phiên đăng nhập.
///
/// Lưu ý: màn chi tiết bot đã có kết nối riêng (BotLiveConnection) cho event trade theo
/// group bot:{id}; feed này lo riêng các view tổng hợp.
final botFeedProvider = Provider<void>((ref) {
  final auth = ref.watch(authControllerProvider);
  final token = ref.read(tokenStorageProvider).cached;
  if (auth.user == null || token == null || token.isEmpty) return;

  HubConnection? hub;
  var disposed = false;

  void refresh(String botId) {
    ref.invalidate(botsListProvider);
    ref.invalidate(botsStatsSummaryProvider);
    ref.invalidate(accountStatsProvider);
    if (botId.isNotEmpty) {
      ref.invalidate(botByIdProvider(botId));
      ref.invalidate(botPositionsProvider(botId));
      ref.invalidate(botOrdersProvider(botId));
      ref.invalidate(botRiskEventsProvider(botId));
      ref.invalidate(botSignalsProvider(botId));
    }
  }

  Future<void> start() async {
    final c = HubConnectionBuilder()
        .withUrl(
          ServerConfig.hubUrl,
          options: HttpConnectionOptions(accessTokenFactory: () async => token),
        )
        .withAutomaticReconnect()
        .build();
    c.on('bot', (args) {
      if (args == null || args.isEmpty) return;
      final m = args[0];
      if (m is Map) refresh((m['botId'] ?? '').toString());
    });
    try {
      await c.start();
      if (disposed) {
        await c.stop();
        return;
      }
      hub = c;
    } catch (_) {
      /* offline / chưa có server → bỏ qua, vẫn còn kéo-để-refresh */
    }
  }

  start();
  ref.onDispose(() async {
    disposed = true;
    try {
      await hub?.stop();
    } catch (_) {}
  });
});
