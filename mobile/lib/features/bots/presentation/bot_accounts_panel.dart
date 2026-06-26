import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/format.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui.dart';
import '../../settings/data/settings_repository.dart';
import '../application/bots_providers.dart';
import '../data/bot_models.dart';

/// Panel multi-account trong chi tiết bot. Mỗi account: vốn riêng + weight + kill-switch riêng.
class BotAccountsPanel extends ConsumerWidget {
  final String botId;
  final String executionMarket;
  const BotAccountsPanel({super.key, required this.botId, required this.executionMarket});

  bool _keyMatches(String code) =>
      (executionMarket == 'Futures' && code == 'binance-futures-testnet') ||
      (executionMarket == 'Spot' && code == 'binance-spot-testnet') ||
      (code != 'binance-futures-testnet' && code != 'binance-spot-testnet');

  Future<void> _refresh(WidgetRef ref) async {
    ref.invalidate(botAccountsProvider(botId));
    ref.invalidate(botPositionsProvider(botId));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(botAccountsProvider(botId));
    return SectionCard(
      title: 'Multi-account',
      trailing: TextButton.icon(
        onPressed: () => _showAdd(context, ref),
        icon: const Icon(Icons.add, size: 18),
        label: const Text('Thêm'),
      ),
      child: async.when(
        loading: () => const LoadingBlock(),
        error: (e, _) => ErrorRetry(e, () => ref.invalidate(botAccountsProvider(botId))),
        data: (list) {
          if (list.isEmpty) {
            return const Text('Chưa có account — bot chạy đơn qua API key gắn trực tiếp.',
                style: TextStyle(color: Colors.grey, fontSize: 12.5));
          }
          final totalW = list.where((a) => a.isActive).fold<double>(0, (s, a) => s + a.weight);
          return Column(
            children: list.map((a) => _accountRow(context, ref, a, totalW)).toList(),
          );
        },
      ),
    );
  }

  Widget _accountRow(BuildContext context, WidgetRef ref, BotAccountDto a, double totalW) {
    final alloc = totalW > 0 && a.isActive ? a.weight / totalW * 100 : 0.0;
    final repo = ref.read(botsRepositoryProvider);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(children: [
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Flexible(child: Text(a.label, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13), overflow: TextOverflow.ellipsis)),
              const SizedBox(width: 6),
              if (a.killed) const StatusPill('KILL', AppTheme.down)
              else StatusPill(a.isActive ? 'active' : 'tắt', a.isActive ? AppTheme.up : Colors.grey),
            ]),
            Text('${a.keyLabel} · w=${Fmt.num2(a.weight)} (${alloc.toStringAsFixed(0)}%) · vốn ${Fmt.usd(a.baseEquityUsdt, decimals: 0)}',
                style: const TextStyle(fontSize: 11, color: Colors.grey)),
            Text('${a.openPositions} mở · PnL ${Fmt.signedUsd(a.realizedPnl)} · win ${a.winRatePercent.toStringAsFixed(0)}%',
                style: TextStyle(fontSize: 11, color: Fmt.pnlColor(a.realizedPnl))),
          ]),
        ),
        PopupMenuButton<String>(
          onSelected: (v) async {
            if (v == 'toggle') {
              await repo.updateAccount(botId, a.id, {'isActive': !a.isActive});
            } else if (v == 'kill') {
              await repo.resetAccountKill(botId, a.id);
            } else if (v == 'edit') {
              if (context.mounted) await _showEdit(context, ref, a);
            } else if (v == 'del') {
              await repo.deleteAccount(botId, a.id);
            }
            await _refresh(ref);
          },
          itemBuilder: (_) => [
            const PopupMenuItem(value: 'edit', child: Text('Sửa weight/vốn')),
            PopupMenuItem(value: 'toggle', child: Text(a.isActive ? 'Tắt' : 'Bật')),
            if (a.killed) const PopupMenuItem(value: 'kill', child: Text('Reset kill-switch')),
            const PopupMenuItem(value: 'del', child: Text('Xóa')),
          ],
        ),
      ]),
    );
  }

  Future<void> _showAdd(BuildContext context, WidgetRef ref) async {
    final keys = await ref.read(settingsRepositoryProvider).apiKeys();
    final existing = (await ref.read(botsRepositoryProvider).accounts(botId)).map((a) => a.apiKeyId).toSet();
    final avail = keys.where((k) => _keyMatches(k.exchangeCode) && !existing.contains(k.id)).toList();
    if (!context.mounted) return;
    String? keyId = avail.isNotEmpty ? avail.first.id : null;
    final weight = TextEditingController(text: '1');
    final equity = TextEditingController(text: '1000');
    await showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surface,
      builder: (ctx) => Padding(
        padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
        child: StatefulBuilder(
          builder: (ctx, setLocal) => Column(mainAxisSize: MainAxisSize.min, children: [
            const Text('Thêm account', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
            const SizedBox(height: 12),
            if (avail.isEmpty)
              Text('Không có API key $executionMarket khả dụng. Thêm ở tab Thêm → Settings.',
                  style: const TextStyle(color: AppTheme.accent))
            else ...[
              DropdownButtonFormField<String>(
                initialValue: keyId,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'API key'),
                items: avail.map((k) => DropdownMenuItem(value: k.id, child: Text('${k.label} (${k.mode})'))).toList(),
                onChanged: (v) => setLocal(() => keyId = v),
              ),
              const SizedBox(height: 10),
              Row(children: [
                Expanded(child: TextField(controller: weight, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Weight'))),
                const SizedBox(width: 12),
                Expanded(child: TextField(controller: equity, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Vốn USDT'))),
              ]),
              const SizedBox(height: 16),
              SizedBox(
                width: double.infinity,
                child: FilledButton(
                  onPressed: keyId == null ? null : () async {
                    await ref.read(botsRepositoryProvider).addAccount(botId, {
                      'apiKeyId': keyId,
                      'weight': double.tryParse(weight.text) ?? 1,
                      'baseEquityUsdt': double.tryParse(equity.text) ?? 1000,
                    });
                    await _refresh(ref);
                    if (ctx.mounted) Navigator.pop(ctx);
                  },
                  child: const Text('Thêm vào bot'),
                ),
              ),
            ],
            const SizedBox(height: 16),
          ]),
        ),
      ),
    );
  }

  Future<void> _showEdit(BuildContext context, WidgetRef ref, BotAccountDto a) async {
    final weight = TextEditingController(text: Fmt.num2(a.weight));
    final equity = TextEditingController(text: a.baseEquityUsdt.toStringAsFixed(0));
    await showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surface,
      builder: (ctx) => Padding(
        padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Text('Sửa ${a.label}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
          const SizedBox(height: 12),
          Row(children: [
            Expanded(child: TextField(controller: weight, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Weight'))),
            const SizedBox(width: 12),
            Expanded(child: TextField(controller: equity, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Vốn USDT'))),
          ]),
          const SizedBox(height: 16),
          SizedBox(
            width: double.infinity,
            child: FilledButton(
              onPressed: () async {
                await ref.read(botsRepositoryProvider).updateAccount(botId, a.id, {
                  'weight': double.tryParse(weight.text) ?? a.weight,
                  'baseEquityUsdt': double.tryParse(equity.text) ?? a.baseEquityUsdt,
                });
                await _refresh(ref);
                if (ctx.mounted) Navigator.pop(ctx);
              },
              child: const Text('Lưu'),
            ),
          ),
          const SizedBox(height: 16),
        ]),
      ),
    );
  }
}
