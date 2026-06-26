import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/auth/auth_providers.dart';
import '../../../core/format.dart';
import '../../../core/realtime/signalr_service.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui.dart';
import '../application/bots_providers.dart';
import '../data/bot_models.dart';
import 'bot_accounts_panel.dart';
import 'bot_edit_screen.dart';

class BotDetailScreen extends ConsumerStatefulWidget {
  final String botId;
  const BotDetailScreen({super.key, required this.botId});
  @override
  ConsumerState<BotDetailScreen> createState() => _BotDetailScreenState();
}

class _BotDetailScreenState extends ConsumerState<BotDetailScreen> {
  BotLiveConnection? _live;
  final List<BotLiveEvent> _events = [];
  bool _connected = false;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _startLive();
  }

  Future<void> _startLive() async {
    final conn = BotLiveConnection(
      botId: widget.botId,
      token: ref.read(tokenStorageProvider).cached,
      onStatus: (c) => mounted ? setState(() => _connected = c) : null,
      onEvent: (e) {
        if (e.botId != widget.botId || !mounted) return;
        setState(() {
          _events.insert(0, e);
          if (_events.length > 80) _events.removeLast();
        });
        if (e.kind == 'order' || e.kind == 'auto_close' || e.kind == 'risk') {
          ref.invalidate(botPositionsProvider(widget.botId));
          ref.invalidate(botOrdersProvider(widget.botId));
        }
      },
    );
    _live = conn;
    try {
      await conn.start();
    } catch (_) {}
  }

  @override
  void dispose() {
    _live?.stop();
    super.dispose();
  }

  void _invalidateAll() {
    ref.invalidate(botByIdProvider(widget.botId));
    ref.invalidate(botPositionsProvider(widget.botId));
    ref.invalidate(botOrdersProvider(widget.botId));
    ref.invalidate(botRiskEventsProvider(widget.botId));
    ref.invalidate(botSignalsProvider(widget.botId));
    ref.invalidate(botsStatsSummaryProvider);
  }

  Future<void> _run(BotDto bot) async {
    setState(() => _busy = true);
    try {
      final repo = ref.read(botsRepositoryProvider);
      bot.isRunning ? await repo.stop(bot.id) : await repo.start(bot.id);
      _invalidateAll();
    } catch (e) {
      _snack('$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _menu(String v, BotDto bot) async {
    final repo = ref.read(botsRepositoryProvider);
    try {
      if (v == 'edit') {
        await Navigator.of(context).push(MaterialPageRoute(builder: (_) => BotEditScreen(bot: bot)));
        _invalidateAll();
      } else if (v == 'kill') {
        await repo.tripKill(bot.id, 'manual');
        _invalidateAll();
      } else if (v == 'reset') {
        await repo.resetKill(bot.id);
        _invalidateAll();
      } else if (v == 'delete') {
        final ok = await _confirmDelete();
        if (ok) {
          await repo.remove(bot.id);
          if (mounted) Navigator.of(context).pop();
        }
      }
    } catch (e) {
      _snack('$e');
    }
  }

  void _snack(String m) {
    if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(m)));
  }

  Future<bool> _confirmDelete() async =>
      await showDialog<bool>(
        context: context,
        builder: (c) => AlertDialog(
          title: const Text('Xóa bot?'),
          content: const Text('Hành động không hoàn tác.'),
          actions: [
            TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Hủy')),
            FilledButton(onPressed: () => Navigator.pop(c, true), child: const Text('Xóa')),
          ],
        ),
      ) ??
      false;

  @override
  Widget build(BuildContext context) {
    final botAsync = ref.watch(botByIdProvider(widget.botId));
    return DefaultTabController(
      length: 5,
      child: Scaffold(
        appBar: AppBar(
          title: botAsync.maybeWhen(data: (b) => Text(b?.name ?? 'Bot'), orElse: () => const Text('Bot')),
          actions: [
            Padding(padding: const EdgeInsets.only(right: 4), child: Center(child: LiveBadge(connected: _connected))),
            botAsync.maybeWhen(
              data: (b) => b == null
                  ? const SizedBox.shrink()
                  : PopupMenuButton<String>(
                      onSelected: (v) => _menu(v, b),
                      itemBuilder: (_) => [
                        const PopupMenuItem(value: 'edit', child: Text('Sửa cấu hình')),
                        if (b.killSwitchTripped) const PopupMenuItem(value: 'reset', child: Text('Reset kill-switch'))
                        else const PopupMenuItem(value: 'kill', child: Text('Trip kill-switch')),
                        const PopupMenuItem(value: 'delete', child: Text('Xóa bot')),
                      ],
                    ),
              orElse: () => const SizedBox.shrink(),
            ),
          ],
          bottom: const TabBar(
            isScrollable: true,
            tabAlignment: TabAlignment.start,
            tabs: [Tab(text: 'Tổng quan'), Tab(text: 'Vị thế'), Tab(text: 'Lệnh'), Tab(text: 'Sự kiện'), Tab(text: 'Account')],
          ),
        ),
        body: botAsync.when(
          loading: () => const LoadingBlock(height: 300),
          error: (e, _) => ErrorRetry(e, () => ref.invalidate(botByIdProvider(widget.botId))),
          data: (bot) {
            if (bot == null) return const EmptyState('Không tìm thấy bot');
            return TabBarView(children: [
              _overviewTab(bot),
              _positionsTab(),
              _ordersTab(),
              _eventsTab(),
              _accountTab(bot),
            ]);
          },
        ),
      ),
    );
  }

  Widget _overviewTab(BotDto bot) {
    return ListView(padding: const EdgeInsets.all(12), children: [
      SectionCard(
        title: '${bot.symbolCode} · ${bot.executionMarket}',
        trailing: FilledButton.icon(
          onPressed: _busy ? null : () => _run(bot),
          style: FilledButton.styleFrom(backgroundColor: bot.isRunning ? AppTheme.down : AppTheme.up),
          icon: Icon(bot.isRunning ? Icons.stop : Icons.play_arrow, size: 18),
          label: Text(bot.isRunning ? 'Dừng' : 'Chạy'),
        ),
        child: Column(children: [
          KvRow('Trạng thái', bot.state),
          KvRow('RunMode', bot.runMode),
          KvRow('Vốn', Fmt.usd(bot.baseEquityUsdt, decimals: 0)),
          KvRow('Đòn bẩy', 'x${bot.leverage}'),
          if (bot.killSwitchTripped) KvRow('Kill-switch', bot.killSwitchReason ?? 'tripped', valueColor: AppTheme.down),
          if (bot.lastError != null) KvRow('Lỗi', bot.lastError!, valueColor: AppTheme.down),
        ]),
      ),
      const SizedBox(height: 12),
      SectionCard(
        title: 'Sự kiện realtime',
        trailing: Text('${_events.length}', style: const TextStyle(color: Colors.grey, fontSize: 12)),
        child: _events.isEmpty
            ? const Text('Đang chờ sự kiện…', style: TextStyle(color: Colors.grey, fontSize: 13))
            : Column(
                children: _events.take(25).map((e) => Padding(
                      padding: const EdgeInsets.symmetric(vertical: 3),
                      child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        AppChip(e.kind),
                        const SizedBox(width: 8),
                        Expanded(child: Text(e.message, style: const TextStyle(fontSize: 12.5))),
                      ]),
                    )).toList(),
              ),
      ),
    ]);
  }

  Widget _positionsTab() {
    final async = ref.watch(botPositionsProvider(widget.botId));
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(botPositionsProvider(widget.botId)),
      child: async.when(
        loading: () => const LoadingBlock(height: 200),
        error: (e, _) => ListView(children: [ErrorRetry(e, () => ref.invalidate(botPositionsProvider(widget.botId)))]),
        data: (list) {
          if (list.isEmpty) return ListView(children: const [EmptyState('Chưa có vị thế')]);
          return ListView.separated(
            padding: const EdgeInsets.all(12),
            itemCount: list.length,
            separatorBuilder: (_, _) => const Divider(height: 1, color: AppTheme.border),
            itemBuilder: (_, i) {
              final p = list[i];
              return Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Row(children: [
                  AppChip(p.side, color: p.side == 'Short' ? AppTheme.down : AppTheme.up, filled: true),
                  const SizedBox(width: 8),
                  Text(p.isOpen ? 'OPEN' : 'closed',
                      style: TextStyle(fontSize: 12, color: p.isOpen ? AppTheme.accent : Colors.grey)),
                  const Spacer(),
                  Text('${Fmt.num2(p.quantity)} @ ${Fmt.num2(p.entryPrice)}',
                      style: const TextStyle(fontSize: 12, color: Colors.grey)),
                  const SizedBox(width: 8),
                  if (!p.isOpen) PnlText(p.realizedPnl),
                ]),
              );
            },
          );
        },
      ),
    );
  }

  Widget _ordersTab() {
    final async = ref.watch(botOrdersProvider(widget.botId));
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(botOrdersProvider(widget.botId)),
      child: async.when(
        loading: () => const LoadingBlock(height: 200),
        error: (e, _) => ListView(children: [ErrorRetry(e, () => ref.invalidate(botOrdersProvider(widget.botId)))]),
        data: (list) {
          if (list.isEmpty) return ListView(children: const [EmptyState('Chưa có lệnh')]);
          return ListView.separated(
            padding: const EdgeInsets.all(12),
            itemCount: list.length,
            separatorBuilder: (_, _) => const Divider(height: 1, color: AppTheme.border),
            itemBuilder: (_, i) {
              final o = list[i];
              return Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Row(children: [
                  AppChip(o.side, color: o.side == 'Sell' ? AppTheme.down : AppTheme.up, filled: true),
                  const SizedBox(width: 8),
                  Text(o.status, style: const TextStyle(fontSize: 12, color: Colors.grey)),
                  const Spacer(),
                  Text('${Fmt.num2(o.quantity)} @ ${Fmt.num2(o.price)}', style: const TextStyle(fontSize: 12)),
                  const SizedBox(width: 8),
                  Text(Fmt.dt(o.createdAt), style: const TextStyle(fontSize: 11, color: Colors.grey)),
                ]),
              );
            },
          );
        },
      ),
    );
  }

  Widget _eventsTab() {
    final risk = ref.watch(botRiskEventsProvider(widget.botId));
    final signals = ref.watch(botSignalsProvider(widget.botId));
    return RefreshIndicator(
      onRefresh: () async {
        ref.invalidate(botRiskEventsProvider(widget.botId));
        ref.invalidate(botSignalsProvider(widget.botId));
      },
      child: ListView(padding: const EdgeInsets.all(12), children: [
        SectionCard(
          title: 'Rủi ro',
          child: risk.when(
            loading: () => const LoadingBlock(height: 60),
            error: (e, _) => ErrorRetry(e, () => ref.invalidate(botRiskEventsProvider(widget.botId))),
            data: (list) => list.isEmpty
                ? const Text('Không có', style: TextStyle(color: Colors.grey, fontSize: 12.5))
                : Column(
                    children: list.take(15).map((r) {
                      final c = r.severity == 'critical' ? AppTheme.down : (r.severity == 'warn' ? AppTheme.accent : Colors.grey);
                      return Padding(
                        padding: const EdgeInsets.symmetric(vertical: 4),
                        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                          AppChip(r.eventType, color: c, filled: true),
                          const SizedBox(width: 8),
                          Expanded(child: Text(r.message, style: const TextStyle(fontSize: 12))),
                          Text(Fmt.dt(r.createdAt), style: const TextStyle(fontSize: 10.5, color: Colors.grey)),
                        ]),
                      );
                    }).toList(),
                  ),
          ),
        ),
        const SizedBox(height: 12),
        SectionCard(
          title: 'Tín hiệu',
          child: signals.when(
            loading: () => const LoadingBlock(height: 60),
            error: (e, _) => ErrorRetry(e, () => ref.invalidate(botSignalsProvider(widget.botId))),
            data: (list) => list.isEmpty
                ? const Text('Không có', style: TextStyle(color: Colors.grey, fontSize: 12.5))
                : Column(
                    children: list.take(15).map((s) => Padding(
                          padding: const EdgeInsets.symmetric(vertical: 4),
                          child: Row(children: [
                            AppChip(s.side ?? s.type, color: s.side == 'Sell' ? AppTheme.down : AppTheme.up, filled: true),
                            const SizedBox(width: 8),
                            Text('@${Fmt.num2(s.price)}', style: const TextStyle(fontSize: 12)),
                            const Spacer(),
                            Text('score ${s.score.toStringAsFixed(2)}', style: const TextStyle(fontSize: 11, color: Colors.grey)),
                            const SizedBox(width: 8),
                            Text(Fmt.dt(s.generatedAt), style: const TextStyle(fontSize: 10.5, color: Colors.grey)),
                          ]),
                        )).toList(),
                  ),
          ),
        ),
      ]),
    );
  }

  Widget _accountTab(BotDto bot) {
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(botAccountsProvider(widget.botId)),
      child: ListView(padding: const EdgeInsets.all(12), children: [
        BotAccountsPanel(botId: widget.botId, executionMarket: bot.executionMarket),
      ]),
    );
  }
}
