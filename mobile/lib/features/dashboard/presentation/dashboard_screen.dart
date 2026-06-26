import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/format.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui.dart';
import '../../bots/application/bots_providers.dart';
import '../../bots/data/bot_models.dart';
import '../../market/application/market_providers.dart';
import '../../market/presentation/symbol_chart_screen.dart';

class DashboardScreen extends ConsumerWidget {
  const DashboardScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final stats = ref.watch(accountStatsProvider);
    final bots = ref.watch(botsStatsSummaryProvider);
    final overview = ref.watch(marketOverviewProvider);

    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: Row(children: const [
          Icon(Icons.bolt, color: AppTheme.accent, size: 22),
          SizedBox(width: 6),
          Text('Quant Flow', style: TextStyle(fontWeight: FontWeight.bold)),
        ]),
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(accountStatsProvider);
          ref.invalidate(botsStatsSummaryProvider);
          ref.invalidate(marketOverviewProvider);
        },
        child: ListView(
          padding: const EdgeInsets.all(12),
          children: [
            _accountCard(stats, bots),
            const SizedBox(height: 12),
            _botsCard(context, ref, bots),
            const SizedBox(height: 12),
            _moversCard(context, overview, ref),
          ],
        ),
      ),
    );
  }

  Widget _accountCard(AsyncValue stats, AsyncValue<List<BotStatsRow>> bots) {
    final equity = bots.maybeWhen(
      data: (list) => list.fold<double>(0, (s, b) => s + b.currentEquity),
      orElse: () => 0.0,
    );
    return SectionCard(
      title: 'Tài khoản',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(Fmt.usd(equity), style: const TextStyle(fontSize: 26, fontWeight: FontWeight.bold)),
          const Text('Tổng equity các bot', style: TextStyle(fontSize: 11, color: Colors.grey)),
          const SizedBox(height: 12),
          stats.when(
            skipLoadingOnReload: true,
            loading: () => const LoadingBlock(height: 60),
            error: (e, _) => Text('$e', style: const TextStyle(color: AppTheme.down, fontSize: 12)),
            data: (s) => Row(children: [
              Expanded(child: StatTile(label: 'PnL hôm nay', value: Fmt.signedUsd(s.todayPnl), valueColor: Fmt.pnlColor(s.todayPnl))),
              const SizedBox(width: 10),
              Expanded(child: StatTile(label: 'Lệnh mở', value: '${s.openPositions}')),
            ]),
          ),
        ],
      ),
    );
  }

  Widget _botsCard(BuildContext context, WidgetRef ref, AsyncValue<List<BotStatsRow>> bots) {
    return SectionCard(
      title: 'Bots',
      child: bots.when(
        skipLoadingOnReload: true,
        loading: () => const LoadingBlock(),
        error: (e, _) => ErrorRetry(e, () => ref.invalidate(botsStatsSummaryProvider)),
        data: (list) {
          if (list.isEmpty) return const EmptyState('Chưa có bot nào', icon: Icons.smart_toy_outlined);
          final sorted = [...list]..sort((a, b) => (b.isRunning ? 1 : 0).compareTo(a.isRunning ? 1 : 0));
          return Column(
            children: sorted.take(6).map((b) {
              return InkWell(
                onTap: () => context.push('/bots/${b.botId}'),
                borderRadius: BorderRadius.circular(8),
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Row(children: [
                    Icon(Icons.circle, size: 9, color: b.isRunning ? AppTheme.up : Colors.grey),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text(b.name, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
                        Text('${b.symbolCode} · ${b.runMode}', style: const TextStyle(fontSize: 11, color: Colors.grey)),
                      ]),
                    ),
                    Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
                      PnlText(b.totalReturnPercent, percent: true),
                      Text(Fmt.signedUsd(b.pnlToday), style: TextStyle(fontSize: 11, color: Fmt.pnlColor(b.pnlToday))),
                    ]),
                  ]),
                ),
              );
            }).toList(),
          );
        },
      ),
    );
  }

  Widget _moversCard(BuildContext context, AsyncValue overview, WidgetRef ref) {
    return SectionCard(
      title: 'Biến động mạnh',
      child: overview.when(
        skipLoadingOnReload: true,
        loading: () => const LoadingBlock(),
        error: (e, _) => ErrorRetry(e, () => ref.invalidate(marketOverviewProvider)),
        data: (o) {
          final gainers = (o.topGainers as List).take(6).toList();
          if (gainers.isEmpty) return const EmptyState('Không có dữ liệu');
          return Column(
            children: gainers.map<Widget>((t) {
              return InkWell(
                onTap: () => openChart(context, t.symbol),
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 6),
                  child: Row(children: [
                    Expanded(child: Text(t.symbol, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13))),
                    Text(Fmt.num2(t.lastPrice), style: const TextStyle(fontSize: 12, color: Colors.grey)),
                    const SizedBox(width: 10),
                    PnlText(t.priceChangePercent, percent: true),
                  ]),
                ),
              );
            }).toList(),
          );
        },
      ),
    );
  }
}
