import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/format.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui.dart';
import '../application/bots_providers.dart';
import '../data/bot_models.dart';
import 'bot_edit_screen.dart';

class BotsListScreen extends ConsumerStatefulWidget {
  const BotsListScreen({super.key});
  @override
  ConsumerState<BotsListScreen> createState() => _BotsListScreenState();
}

class _BotsListScreenState extends ConsumerState<BotsListScreen> {
  String _filter = 'all'; // all | live | paper | stopped

  bool _match(BotStatsRow b) => switch (_filter) {
        'live' => b.runMode == 'LiveTrading',
        'paper' => b.runMode == 'PaperTrading',
        'stopped' => !b.isRunning,
        _ => true,
      };

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(botsStatsSummaryProvider);
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text('Bots', style: TextStyle(fontWeight: FontWeight.bold)),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const BotEditScreen())),
        icon: const Icon(Icons.add),
        label: const Text('Tạo bot'),
      ),
      body: Column(children: [
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          child: Row(children: [
            for (final f in const [('all', 'Tất cả'), ('live', 'Live'), ('paper', 'Paper'), ('stopped', 'Dừng')])
              Padding(
                padding: const EdgeInsets.only(right: 8),
                child: ChoiceChip(
                  label: Text(f.$2),
                  selected: _filter == f.$1,
                  onSelected: (_) => setState(() => _filter = f.$1),
                ),
              ),
          ]),
        ),
        Expanded(
          child: RefreshIndicator(
            onRefresh: () async => ref.invalidate(botsStatsSummaryProvider),
            child: async.when(
              loading: () => const LoadingBlock(height: 300),
              error: (e, _) => ListView(children: [ErrorRetry(e, () => ref.invalidate(botsStatsSummaryProvider))]),
              data: (list) {
                final filtered = list.where(_match).toList();
                if (filtered.isEmpty) {
                  return ListView(children: const [EmptyState('Không có bot', icon: Icons.smart_toy_outlined)]);
                }
                return ListView.separated(
                  padding: const EdgeInsets.fromLTRB(12, 4, 12, 88),
                  itemCount: filtered.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (_, i) => _BotCard(b: filtered[i]),
                );
              },
            ),
          ),
        ),
      ]),
    );
  }
}

class _BotCard extends StatelessWidget {
  final BotStatsRow b;
  const _BotCard({required this.b});
  @override
  Widget build(BuildContext context) {
    return Card(
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: () => context.push('/bots/${b.botId}'),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Icon(Icons.circle, size: 9, color: b.isRunning ? AppTheme.up : Colors.grey),
              const SizedBox(width: 8),
              Expanded(child: Text(b.name, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 15))),
              StatusPill(b.isRunning ? 'Running' : b.state, b.isRunning ? AppTheme.up : Colors.grey),
            ]),
            const SizedBox(height: 8),
            Row(children: [
              AppChip(b.symbolCode, color: AppTheme.accent, filled: true),
              const SizedBox(width: 6),
              AppChip(b.runMode),
              const Spacer(),
              Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
                PnlText(b.totalReturnPercent, percent: true, size: 14),
                Text('PnL ${Fmt.signedUsd(b.totalRealizedPnl)} · ${b.openPositions} mở',
                    style: const TextStyle(fontSize: 11, color: Colors.grey)),
              ]),
            ]),
          ]),
        ),
      ),
    );
  }
}
