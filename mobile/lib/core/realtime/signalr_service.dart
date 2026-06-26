import 'package:signalr_netcore/signalr_client.dart';

import '../config/server_config.dart';

class BotLiveEvent {
  final String botId;
  final String kind;
  final String message;
  final String at;
  BotLiveEvent(this.botId, this.kind, this.message, this.at);
}

/// Kết nối SignalR tới hub /hubs/market, join group của 1 bot và stream sự kiện "bot"
/// (giống web: connection.on('bot', ...)). JWT đẩy qua accessTokenFactory (?access_token=).
/// Quản lý vòng đời thủ công trong State của màn hình (initState start / dispose stop).
class BotLiveConnection {
  final String botId;
  final String? token;
  final void Function(BotLiveEvent) onEvent;
  final void Function(bool connected)? onStatus;
  HubConnection? _hub;

  BotLiveConnection({
    required this.botId,
    required this.token,
    required this.onEvent,
    this.onStatus,
  });

  Future<void> start() async {
    final hub = HubConnectionBuilder()
        .withUrl(
          ServerConfig.hubUrl,
          options: HttpConnectionOptions(
            accessTokenFactory: () async => token ?? '',
          ),
        )
        .withAutomaticReconnect()
        .build();

    hub.onclose(({error}) => onStatus?.call(false));
    hub.onreconnected(({connectionId}) {
      onStatus?.call(true);
      _join(hub);
    });

    hub.on('bot', (args) {
      if (args == null || args.isEmpty) return;
      final m = args[0];
      if (m is Map) {
        onEvent(BotLiveEvent(
          (m['botId'] ?? '').toString(),
          (m['kind'] ?? '').toString(),
          (m['message'] ?? '').toString(),
          (m['at'] ?? '').toString(),
        ));
      }
    });

    await hub.start();
    _hub = hub;
    onStatus?.call(true);
    await _join(hub);
  }

  Future<void> _join(HubConnection hub) async {
    try {
      await hub.invoke('JoinBotGroup', args: [botId]);
    } catch (_) {/* group join best-effort */}
  }

  Future<void> stop() async {
    final hub = _hub;
    _hub = null;
    if (hub == null) return;
    try {
      await hub.invoke('LeaveBotGroup', args: [botId]);
    } catch (_) {}
    await hub.stop();
  }
}
