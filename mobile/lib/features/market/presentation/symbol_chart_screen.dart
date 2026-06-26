import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:k_chart_plus/k_chart_plus.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui.dart';
import '../application/market_providers.dart';
import '../data/market_models.dart';

/// Khung thời gian hiển thị → tên enum CandleInterval của BE.
const _intervals = <String, String>{
  '15m': 'FifteenMinutes',
  '1H': 'OneHour',
  '4H': 'FourHours',
  '1D': 'OneDay',
};

/// Chart nến kiểu TradingView: pinch-zoom, kéo ngang, crosshair OHLC, indicator
/// (MA/BOLL/MACD/RSI/KDJ/WR). Dữ liệu Binance klines qua /api/market/candles.
class SymbolChartScreen extends ConsumerStatefulWidget {
  final String symbol;
  final bool futures;
  const SymbolChartScreen({super.key, required this.symbol, this.futures = false});
  @override
  ConsumerState<SymbolChartScreen> createState() => _SymbolChartScreenState();
}

class _SymbolChartScreenState extends ConsumerState<SymbolChartScreen> {
  String _interval = 'OneHour';
  final Set<MainState> _main = {MainState.MA};
  final Set<SecondaryState> _secondary = {};

  String get _sym => widget.symbol.toUpperCase();
  CandleKey get _key => (symbol: _sym, interval: _interval, futures: widget.futures);

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(candlesProvider(_key));
    return Scaffold(
      appBar: AppBar(
        title: Row(children: [
          Text(_sym),
          if (widget.futures) ...[
            const SizedBox(width: 8),
            const Text('PERP', style: TextStyle(fontSize: 11, color: Colors.grey)),
          ],
        ]),
      ),
      body: Column(children: [
        _intervalBar(),
        _indicatorBar(),
        Expanded(
          child: async.when(
            skipLoadingOnReload: true,
            loading: () => const LoadingBlock(height: 320),
            error: (e, _) => ErrorRetry(e, () => ref.invalidate(candlesProvider(_key))),
            data: (candles) {
              if (candles.length < 2) return const EmptyState('Không có dữ liệu nến');
              final data = _toKLine(candles);
              DataUtil.calculate(data); // tính MA/BOLL/MACD/RSI...
              return KChartWidget(
                data,
                ChartStyle(),
                _colors,
                isTrendLine: false,
                mainStateLi: _main,
                secondaryStateLi: _secondary,
                volHidden: false,
                fixedLength: _fixedLength(candles.last.close),
                maDayList: const [5, 10, 20],
                timeFormat: _interval == 'OneDay'
                    ? TimeFormat.YEAR_MONTH_DAY
                    : TimeFormat.YEAR_MONTH_DAY_WITH_HOUR,
                materialInfoDialog: true,
              );
            },
          ),
        ),
      ]),
    );
  }

  Widget _intervalBar() => Padding(
        padding: const EdgeInsets.fromLTRB(12, 8, 12, 2),
        child: SizedBox(
          width: double.infinity,
          child: SegmentedButton<String>(
            style: const ButtonStyle(visualDensity: VisualDensity.compact),
            segments: [
              for (final e in _intervals.entries)
                ButtonSegment(value: e.value, label: Text(e.key)),
            ],
            selected: {_interval},
            onSelectionChanged: (s) => setState(() => _interval = s.first),
          ),
        ),
      );

  Widget _indicatorBar() => SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
        child: Row(children: [
          _mainChip('MA', MainState.MA),
          _mainChip('BOLL', MainState.BOLL),
          const SizedBox(width: 6),
          const SizedBox(height: 18, child: VerticalDivider(width: 12, color: AppTheme.border)),
          _secChip('MACD', SecondaryState.MACD),
          _secChip('RSI', SecondaryState.RSI),
          _secChip('KDJ', SecondaryState.KDJ),
          _secChip('WR', SecondaryState.WR),
        ]),
      );

  Widget _mainChip(String label, MainState s) => Padding(
        padding: const EdgeInsets.only(right: 6),
        child: FilterChip(
          label: Text(label, style: const TextStyle(fontSize: 12)),
          visualDensity: VisualDensity.compact,
          selected: _main.contains(s),
          onSelected: (v) => setState(() => v ? _main.add(s) : _main.remove(s)),
        ),
      );

  Widget _secChip(String label, SecondaryState s) => Padding(
        padding: const EdgeInsets.only(right: 6),
        child: FilterChip(
          label: Text(label, style: const TextStyle(fontSize: 12)),
          visualDensity: VisualDensity.compact,
          selected: _secondary.contains(s),
          // KChart vẽ tối đa 1 secondary panel rõ ràng → chọn cái này bỏ cái khác cho gọn.
          onSelected: (v) => setState(() {
            _secondary.clear();
            if (v) _secondary.add(s);
          }),
        ),
      );

  List<KLineEntity> _toKLine(List<Candle> cs) => [
        for (final c in cs)
          KLineEntity.fromCustom(
            time: c.openTime.millisecondsSinceEpoch,
            open: c.open,
            high: c.high,
            low: c.low,
            close: c.close,
            vol: c.volume,
          ),
      ];

  int _fixedLength(double price) {
    if (price >= 1000) return 2;
    if (price >= 1) return 3;
    if (price >= 0.01) return 5;
    return 8;
  }

  ChartColors get _colors => ChartColors(
        bgColor: AppTheme.bg,
        upColor: AppTheme.up,
        dnColor: AppTheme.down,
      )
        ..defaultTextColor = Colors.grey
        ..gridColor = AppTheme.border
        ..nowPriceTextColor = Colors.white;
}

/// Mở chart từ bất kỳ đâu.
void openChart(BuildContext context, String symbol, {bool futures = false}) {
  Navigator.of(context).push(MaterialPageRoute(
    builder: (_) => SymbolChartScreen(symbol: symbol, futures: futures),
  ));
}
