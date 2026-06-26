import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/format.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui.dart';
import '../../strategies/data/strategy_repository.dart';
import '../data/backtest_repository.dart';

class BacktestScreen extends ConsumerStatefulWidget {
  const BacktestScreen({super.key});
  @override
  ConsumerState<BacktestScreen> createState() => _BacktestScreenState();
}

class _BacktestScreenState extends ConsumerState<BacktestScreen> {
  final _symbol = TextEditingController(text: 'BTCUSDT');
  final _capital = TextEditingController(text: '1000');
  final _topN = TextEditingController(text: '20');
  String? _strategyId;
  String _interval = '1h';
  String _market = 'Spot';
  int _months = 3;
  bool _scanMode = false;
  bool _busy = false;
  String? _error;
  List<ScanRow>? _scanResults;

  @override
  void dispose() {
    _symbol.dispose();
    _capital.dispose();
    _topN.dispose();
    super.dispose();
  }

  Map<String, dynamic> _baseBody() {
    final now = DateTime.now().toUtc();
    final from = DateTime(now.year, now.month - _months, now.day).toUtc();
    return {
      'strategyId': _strategyId,
      'interval': _interval,
      'from': from.toIso8601String(),
      'to': now.toIso8601String(),
      'initialCapital': double.tryParse(_capital.text) ?? 1000,
      'market': _market,
      'leverage': _market == 'Futures' ? 5 : 1,
    };
  }

  Future<void> _run() async {
    if (_strategyId == null) return setState(() => _error = 'Chọn strategy');
    setState(() {
      _busy = true;
      _error = null;
      _scanResults = null;
    });
    try {
      final repo = ref.read(backtestRepositoryProvider);
      if (_scanMode) {
        final rows = await repo.scan({..._baseBody(), 'topN': int.tryParse(_topN.text) ?? 20});
        setState(() => _scanResults = rows..sort((a, b) => (b.returnPercent ?? -999).compareTo(a.returnPercent ?? -999)));
      } else {
        await repo.run({..._baseBody(), 'symbolCode': _symbol.text.trim().toUpperCase()});
        ref.invalidate(backtestsProvider);
      }
    } catch (e) {
      setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final strategies = ref.watch(strategiesProvider);
    final list = ref.watch(backtestsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Backtest', style: TextStyle(fontWeight: FontWeight.bold)), titleSpacing: 16),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(backtestsProvider),
        child: ListView(padding: const EdgeInsets.all(12), children: [
          SectionCard(
            title: 'Chạy backtest',
            trailing: Switch(value: _scanMode, onChanged: (v) => setState(() => _scanMode = v)),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(_scanMode ? 'Quét top N coin' : 'Một mã', style: const TextStyle(fontSize: 11, color: Colors.grey)),
              const SizedBox(height: 8),
              strategies.when(
                loading: () => const LinearProgressIndicator(),
                error: (e, _) => Text('$e', style: const TextStyle(color: AppTheme.down)),
                data: (s) => DropdownButtonFormField<String>(
                  initialValue: _strategyId,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: 'Strategy', isDense: true),
                  items: s.map((x) => DropdownMenuItem(value: x.id, child: Text('${x.name} (${x.kind})'))).toList(),
                  onChanged: (v) => setState(() => _strategyId = v),
                ),
              ),
              const SizedBox(height: 10),
              if (_scanMode)
                TextField(controller: _topN, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Top N coin', isDense: true))
              else
                TextField(controller: _symbol, textCapitalization: TextCapitalization.characters, decoration: const InputDecoration(labelText: 'Symbol', isDense: true)),
              const SizedBox(height: 10),
              Row(children: [
                Expanded(child: TextField(controller: _capital, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Vốn USDT', isDense: true))),
                const SizedBox(width: 12),
                Expanded(
                  child: DropdownButtonFormField<String>(
                    initialValue: _interval,
                    decoration: const InputDecoration(labelText: 'Nến', isDense: true),
                    items: const ['15m', '1h', '4h', '1d'].map((i) => DropdownMenuItem(value: i, child: Text(i))).toList(),
                    onChanged: (v) => setState(() => _interval = v ?? '1h'),
                  ),
                ),
              ]),
              const SizedBox(height: 10),
              SizedBox(
                width: double.infinity,
                child: SegmentedButton<String>(
                  segments: const [ButtonSegment(value: 'Spot', label: Text('Spot')), ButtonSegment(value: 'Futures', label: Text('Futures'))],
                  selected: {_market},
                  onSelectionChanged: (s) => setState(() => _market = s.first),
                ),
              ),
              const SizedBox(height: 10),
              Row(children: [
                const Text('Thời gian:', style: TextStyle(fontSize: 12, color: Colors.grey)),
                const SizedBox(width: 8),
                for (final m in const [1, 3, 6])
                  Padding(
                    padding: const EdgeInsets.only(right: 6),
                    child: ChoiceChip(label: Text('${m}M'), selected: _months == m, onSelected: (_) => setState(() => _months = m)),
                  ),
              ]),
              if (_error != null) ...[
                const SizedBox(height: 10),
                Text(_error!, style: const TextStyle(color: AppTheme.down, fontSize: 12)),
              ],
              const SizedBox(height: 12),
              SizedBox(
                width: double.infinity,
                child: FilledButton.icon(
                  onPressed: _busy ? null : _run,
                  icon: _busy
                      ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.black))
                      : const Icon(Icons.play_arrow),
                  label: Text(_scanMode ? 'Quét' : 'Chạy backtest'),
                ),
              ),
            ]),
          ),
          const SizedBox(height: 12),
          if (_scanResults != null) _scanCard(_scanResults!),
          if (_scanResults != null) const SizedBox(height: 12),
          SectionCard(
            title: 'Kết quả gần đây',
            child: list.when(
              loading: () => const LoadingBlock(),
              error: (e, _) => ErrorRetry(e, () => ref.invalidate(backtestsProvider)),
              data: (rows) {
                if (rows.isEmpty) return const EmptyState('Chưa có backtest');
                return Column(children: rows.take(20).map((b) => _resultRow(b)).toList());
              },
            ),
          ),
        ]),
      ),
    );
  }

  Widget _scanCard(List<ScanRow> rows) => SectionCard(
        title: 'Kết quả quét (${rows.length})',
        child: Column(
          children: rows.take(30).map((r) => Padding(
                padding: const EdgeInsets.symmetric(vertical: 5),
                child: Row(children: [
                  Expanded(child: Text(r.symbol, style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600))),
                  Text('${r.tradeCount ?? 0} lệnh', style: const TextStyle(fontSize: 11, color: Colors.grey)),
                  const SizedBox(width: 12),
                  if (r.returnPercent != null) PnlText(r.returnPercent!, percent: true) else Text(r.status, style: const TextStyle(fontSize: 11, color: Colors.grey)),
                ]),
              )).toList(),
        ),
      );

  Widget _resultRow(BacktestSummary b) {
    return InkWell(
      onTap: b.done ? () => _showDetail(b.id) : null,
      borderRadius: BorderRadius.circular(8),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Row(children: [
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text('${b.symbolCode} · ${b.strategyKind}', style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
              Text('${b.interval} · ${b.tradeCount ?? 0} lệnh · ${Fmt.dt(b.createdAt)}', style: const TextStyle(fontSize: 11, color: Colors.grey)),
            ]),
          ),
          if (b.done && b.returnPercent != null)
            Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
              PnlText(b.returnPercent!, percent: true),
              Text('DD ${b.maxDrawdownPercent?.toStringAsFixed(1) ?? '-'}%', style: const TextStyle(fontSize: 11, color: Colors.grey)),
            ])
          else
            StatusPill(b.status, b.status == 'Failed' ? AppTheme.down : AppTheme.accent),
        ]),
      ),
    );
  }

  Future<void> _showDetail(String id) async {
    await showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surface,
      builder: (ctx) => DraggableScrollableSheet(
        expand: false,
        initialChildSize: 0.7,
        maxChildSize: 0.92,
        builder: (ctx, scroll) => _DetailSheet(id: id, scroll: scroll),
      ),
    );
  }
}

class _DetailSheet extends ConsumerWidget {
  final String id;
  final ScrollController scroll;
  const _DetailSheet({required this.id, required this.scroll});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(backtestDetailProvider(id));
    return async.when(
      loading: () => const LoadingBlock(height: 200),
      error: (e, _) => ErrorRetry(e, () => ref.invalidate(backtestDetailProvider(id))),
      data: (d) {
        final s = d.summary;
        return ListView(controller: scroll, padding: const EdgeInsets.all(16), children: [
          Text('${s.symbolCode} · ${s.strategyKind}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
          Text('${s.interval} · ${Fmt.dt(s.createdAt)}', style: const TextStyle(fontSize: 12, color: Colors.grey)),
          const SizedBox(height: 14),
          Row(children: [
            Expanded(child: StatTile(label: 'Lợi nhuận', value: Fmt.pct(s.returnPercent ?? 0), valueColor: Fmt.pnlColor(s.returnPercent ?? 0))),
            const SizedBox(width: 10),
            Expanded(child: StatTile(label: 'Max DD', value: '${s.maxDrawdownPercent?.toStringAsFixed(1) ?? '-'}%')),
          ]),
          const SizedBox(height: 10),
          Row(children: [
            Expanded(child: StatTile(label: 'Win rate', value: '${s.winRatePercent?.toStringAsFixed(0) ?? '-'}%')),
            const SizedBox(width: 10),
            Expanded(child: StatTile(label: 'Số lệnh', value: '${s.tradeCount ?? 0}')),
          ]),
          const SizedBox(height: 16),
          const Text('Đường vốn', style: TextStyle(fontWeight: FontWeight.w600)),
          const SizedBox(height: 8),
          SizedBox(height: 200, child: _EquityChart(points: d.equityCurve)),
        ]);
      },
    );
  }
}

class _EquityChart extends StatelessWidget {
  final List<EquityPoint> points;
  const _EquityChart({required this.points});
  @override
  Widget build(BuildContext context) {
    if (points.length < 2) return const Center(child: Text('Không đủ dữ liệu', style: TextStyle(color: Colors.grey)));
    final spots = [for (var i = 0; i < points.length; i++) FlSpot(i.toDouble(), points[i].equity)];
    final minY = points.map((p) => p.equity).reduce((a, b) => a < b ? a : b);
    final maxY = points.map((p) => p.equity).reduce((a, b) => a > b ? a : b);
    return LineChart(LineChartData(
      minY: minY - (maxY - minY) * 0.05 - 1,
      maxY: maxY + (maxY - minY) * 0.05 + 1,
      gridData: const FlGridData(show: false),
      titlesData: const FlTitlesData(show: false),
      borderData: FlBorderData(show: false),
      lineTouchData: const LineTouchData(enabled: false),
      lineBarsData: [
        LineChartBarData(
          spots: spots,
          isCurved: false,
          color: AppTheme.accent,
          barWidth: 2,
          dotData: const FlDotData(show: false),
          belowBarData: BarAreaData(show: true, color: AppTheme.accent.withValues(alpha: 0.12)),
        ),
      ],
    ));
  }
}
