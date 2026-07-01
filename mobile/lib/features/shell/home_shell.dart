import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/realtime/bot_feed.dart';
import '../dashboard/presentation/dashboard_screen.dart';
import '../market/presentation/market_screen.dart';
import '../bots/presentation/bots_list_screen.dart';
import '../backtest/presentation/backtest_screen.dart';
import '../more/presentation/more_screen.dart';

/// Khung chính: bottom navigation 5 tab, giữ state mỗi tab bằng IndexedStack.
class HomeShell extends ConsumerStatefulWidget {
  const HomeShell({super.key});
  @override
  ConsumerState<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends ConsumerState<HomeShell> {
  int _index = 0;

  static const _tabs = <Widget>[
    DashboardScreen(),
    MarketScreen(),
    BotsListScreen(),
    BacktestScreen(),
    MoreScreen(),
  ];

  @override
  Widget build(BuildContext context) {
    // Kích hoạt feed SignalR mức app (refresh list/dashboard khi có thay đổi từ web).
    ref.watch(botFeedProvider);
    return Scaffold(
      body: IndexedStack(index: _index, children: _tabs),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (i) => setState(() => _index = i),
        destinations: const [
          NavigationDestination(icon: Icon(Icons.dashboard_outlined), selectedIcon: Icon(Icons.dashboard), label: 'Tổng quan'),
          NavigationDestination(icon: Icon(Icons.show_chart_outlined), selectedIcon: Icon(Icons.show_chart), label: 'Market'),
          NavigationDestination(icon: Icon(Icons.smart_toy_outlined), selectedIcon: Icon(Icons.smart_toy), label: 'Bots'),
          NavigationDestination(icon: Icon(Icons.science_outlined), selectedIcon: Icon(Icons.science), label: 'Backtest'),
          NavigationDestination(icon: Icon(Icons.more_horiz), label: 'Thêm'),
        ],
      ),
    );
  }
}
