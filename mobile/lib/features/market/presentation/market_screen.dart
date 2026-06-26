import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/format.dart';
import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui.dart';
import '../data/market_models.dart';
import '../application/market_providers.dart';
import 'symbol_chart_screen.dart';

class MarketScreen extends StatelessWidget {
  const MarketScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 5,
      child: Scaffold(
        appBar: AppBar(
          titleSpacing: 16,
          title: const Text('Thị trường', style: TextStyle(fontWeight: FontWeight.bold)),
          bottom: const TabBar(
            isScrollable: true,
            tabAlignment: TabAlignment.start,
            tabs: [
              Tab(text: 'Alpha'),
              Tab(text: 'Quét'),
              Tab(text: 'Mới list'),
              Tab(text: 'Walls'),
              Tab(text: 'Tin'),
            ],
          ),
        ),
        body: const TabBarView(children: [
          _AlphaView(),
          _ScannerView(),
          _NewListingsView(),
          _WallsView(),
          _SentimentView(),
        ]),
      ),
    );
  }
}

// ════════════════════════════════════ ALPHA ════════════════════════════════
class _AlphaView extends ConsumerWidget {
  const _AlphaView();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tokensAsync = ref.watch(alphaTokensProvider);
    final live = ref.watch(alphaPricesProvider).value ?? const <AlphaPriceTick>[];
    final filter = ref.watch(alphaFilterProvider);
    final liveMap = {for (final t in live) t.symbol: t};

    return Column(children: [
      _AlphaHeader(filter: filter, liveCount: live.length),
      Expanded(
        child: RefreshIndicator(
          onRefresh: () async {
            ref.invalidate(alphaTokensProvider);
            ref.invalidate(alphaPricesProvider);
          },
          child: tokensAsync.when(
            skipLoadingOnReload: true,
            loading: () => const LoadingBlock(height: 300),
            error: (e, _) =>
                ListView(children: [ErrorRetry(e, () => ref.invalidate(alphaTokensProvider))]),
            data: (tokens) {
              // Lọc theo search + hướng (dùng pct live nếu có), rồi sort theo volume live.
              final q = filter.query.trim().toLowerCase();
              var rows = tokens.where((t) {
                if (q.isNotEmpty &&
                    !t.symbol.toLowerCase().contains(q) &&
                    !t.name.toLowerCase().contains(q)) {
                  return false;
                }
                if (filter.dir != AlphaDir.all) {
                  final pct = liveMap[t.futuresSymbol]?.percentChange24h ?? t.percentChange24h;
                  return filter.dir == AlphaDir.gainers ? pct > 0 : pct < 0;
                }
                return true;
              }).toList();
              rows.sort((a, b) {
                final av = liveMap[a.futuresSymbol]?.quoteVolume24h ?? a.volume24h;
                final bv = liveMap[b.futuresSymbol]?.quoteVolume24h ?? b.volume24h;
                return bv.compareTo(av);
              });

              if (rows.isEmpty) {
                return ListView(children: [
                  EmptyState(q.isNotEmpty ? 'Không token nào khớp "$q"' : 'Chưa có dữ liệu Alpha'),
                ]);
              }
              return ListView.separated(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                itemCount: rows.length,
                separatorBuilder: (_, _) => const Divider(height: 1, color: AppTheme.border),
                itemBuilder: (_, i) =>
                    _AlphaRow(rows[i], liveMap[rows[i].futuresSymbol]),
              );
            },
          ),
        ),
      ),
    ]);
  }
}

class _AlphaHeader extends ConsumerWidget {
  final AlphaFilter filter;
  final int liveCount;
  const _AlphaHeader({required this.filter, required this.liveCount});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final n = ref.read(alphaFilterProvider.notifier);
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 4),
      child: Column(children: [
        Row(children: [
          Expanded(
            child: SizedBox(
              height: 38,
              child: TextField(
                onChanged: n.setQuery,
                style: const TextStyle(fontSize: 13),
                decoration: InputDecoration(
                  isDense: true,
                  prefixIcon: const Icon(Icons.search, size: 18),
                  hintText: 'Lọc symbol / tên',
                  contentPadding: const EdgeInsets.symmetric(vertical: 0),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
                ),
              ),
            ),
          ),
          const SizedBox(width: 8),
          Row(mainAxisSize: MainAxisSize.min, children: [
            const Icon(Icons.bolt, size: 13, color: AppTheme.up),
            Text(' $liveCount live', style: const TextStyle(fontSize: 11, color: Colors.grey)),
          ]),
        ]),
        const SizedBox(height: 8),
        SizedBox(
          width: double.infinity,
          child: SegmentedButton<AlphaDir>(
            style: const ButtonStyle(visualDensity: VisualDensity.compact),
            segments: const [
              ButtonSegment(value: AlphaDir.all, label: Text('Tất cả')),
              ButtonSegment(value: AlphaDir.gainers, label: Text('Tăng'), icon: Icon(Icons.trending_up, size: 15)),
              ButtonSegment(value: AlphaDir.losers, label: Text('Giảm'), icon: Icon(Icons.trending_down, size: 15)),
            ],
            selected: {filter.dir},
            onSelectionChanged: (s) => n.setDir(s.first),
          ),
        ),
      ]),
    );
  }
}

class _AlphaRow extends StatelessWidget {
  final AlphaToken t;
  final AlphaPriceTick? live;
  const _AlphaRow(this.t, this.live);

  @override
  Widget build(BuildContext context) {
    final price = live?.price ?? t.price;
    final pct = live?.percentChange24h ?? t.percentChange24h;
    final vol = live?.quoteVolume24h ?? t.volume24h;
    final positive = pct >= 0;
    final fund = live?.fundingRate;

    return InkWell(
      onTap: () => openChart(context, t.futuresSymbol, futures: true),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Row(children: [
          _coinIcon(t.iconUrl, t.symbol),
          const SizedBox(width: 10),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(t.symbol, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 13)),
              Text(
                [
                  if (t.name.isNotEmpty) t.name,
                  if (t.chain != null) t.chain!,
                ].join(' · '),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 10.5, color: Colors.grey),
              ),
              Text('MC ${Fmt.compact(t.marketCap)} · Vol ${Fmt.compact(vol)}',
                  style: const TextStyle(fontSize: 10, color: Colors.grey)),
            ]),
          ),
          const SizedBox(width: 6),
          _Sparkline(t.sparkline, positive ? AppTheme.up : AppTheme.down),
          const SizedBox(width: 8),
          Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
            Text(Fmt.num2(price), style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600)),
            PnlText(pct, percent: true, size: 11),
            if (fund != null)
              Text('fun ${(fund * 100).toStringAsFixed(3)}%',
                  style: TextStyle(fontSize: 9.5, color: fund > 0 ? AppTheme.down : AppTheme.up)),
          ]),
        ]),
      ),
    );
  }
}

Widget _coinIcon(String? url, String sym) {
  final fallback = CircleAvatar(
    radius: 14,
    backgroundColor: AppTheme.border,
    child: Text(
      sym.isEmpty ? '?' : sym.substring(0, math.min(2, sym.length)),
      style: const TextStyle(fontSize: 9, fontWeight: FontWeight.w700, color: Colors.white70),
    ),
  );
  if (url == null || url.isEmpty) return fallback;
  return CircleAvatar(
    radius: 14,
    backgroundColor: AppTheme.bg,
    backgroundImage: NetworkImage(url),
    onBackgroundImageError: (_, _) {},
  );
}

class _Sparkline extends StatelessWidget {
  final List<double> data;
  final Color color;
  const _Sparkline(this.data, this.color);

  @override
  Widget build(BuildContext context) {
    if (data.length < 2) return const SizedBox(width: 54);
    return SizedBox(width: 54, height: 24, child: CustomPaint(painter: _SparkPainter(data, color)));
  }
}

class _SparkPainter extends CustomPainter {
  final List<double> d;
  final Color c;
  _SparkPainter(this.d, this.c);

  @override
  void paint(Canvas canvas, Size size) {
    final lo = d.reduce(math.min);
    final hi = d.reduce(math.max);
    final range = (hi - lo) == 0 ? 1.0 : (hi - lo);
    final dx = size.width / (d.length - 1);
    final path = Path();
    for (var i = 0; i < d.length; i++) {
      final x = i * dx;
      final y = size.height - ((d[i] - lo) / range) * size.height;
      if (i == 0) {
        path.moveTo(x, y);
      } else {
        path.lineTo(x, y);
      }
    }
    canvas.drawPath(
      path,
      Paint()
        ..color = c
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.3,
    );
  }

  @override
  bool shouldRepaint(_SparkPainter o) => o.d != d || o.c != c;
}

// ════════════════════════════════════ SCANNER ══════════════════════════════
class _ScannerView extends ConsumerStatefulWidget {
  const _ScannerView();
  @override
  ConsumerState<_ScannerView> createState() => _ScannerViewState();
}

class _ScannerViewState extends ConsumerState<_ScannerView> {
  bool _expanded = false;
  static const _preview = 10;

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(scannerProvider);
    final f = ref.watch(scannerFilterProvider);
    final n = ref.read(scannerFilterProvider.notifier);

    return Column(children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(12, 10, 12, 4),
        child: Column(children: [
          SizedBox(
            width: double.infinity,
            child: SegmentedButton<String>(
              style: const ButtonStyle(visualDensity: VisualDensity.compact),
              segments: const [
                ButtonSegment(value: 'any', label: Text('Cả 2')),
                ButtonSegment(value: 'up', label: Text('Tăng'), icon: Icon(Icons.trending_up, size: 15)),
                ButtonSegment(value: 'down', label: Text('Giảm'), icon: Icon(Icons.trending_down, size: 15)),
              ],
              selected: {f.direction},
              onSelectionChanged: (s) => n.set(f.copyWith(direction: s.first)),
            ),
          ),
          const SizedBox(height: 8),
          Wrap(spacing: 7, runSpacing: 4, children: [
            for (final pct in [3.0, 5.0, 10.0])
              ChoiceChip(
                label: Text('≥${pct.toStringAsFixed(0)}%'),
                visualDensity: VisualDensity.compact,
                selected: f.minPct == pct,
                onSelected: (_) => n.set(f.copyWith(minPct: pct)),
              ),
            for (final v in [10000000.0, 50000000.0, 100000000.0])
              ChoiceChip(
                label: Text('Vol≥${(v / 1e6).toStringAsFixed(0)}M'),
                visualDensity: VisualDensity.compact,
                selected: f.minVolume == v,
                onSelected: (_) => n.set(f.copyWith(minVolume: v)),
              ),
          ]),
        ]),
      ),
      Expanded(
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(scannerProvider),
          child: async.when(
            skipLoadingOnReload: true,
            loading: () => const LoadingBlock(height: 300),
            error: (e, _) => ListView(children: [ErrorRetry(e, () => ref.invalidate(scannerProvider))]),
            data: (r) {
              if (r.results.isEmpty) {
                return ListView(children: const [EmptyState('Không mã nào khớp bộ lọc')]);
              }
              final showAll = _expanded || r.results.length <= _preview;
              final shown = showAll ? r.results : r.results.take(_preview).toList();
              return ListView.separated(
                padding: const EdgeInsets.symmetric(horizontal: 12),
                itemCount: shown.length + 1,
                separatorBuilder: (_, _) => const Divider(height: 1, color: AppTheme.border),
                itemBuilder: (_, i) {
                  if (i == shown.length) {
                    return _MoreFooter(
                      total: r.results.length,
                      preview: _preview,
                      expanded: _expanded,
                      onTap: () => setState(() => _expanded = !_expanded),
                    );
                  }
                  final t = shown[i];
                  return InkWell(
                    onTap: () => openChart(context, t.symbol),
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 10),
                      child: Row(children: [
                        Expanded(
                            child: Text(t.symbol,
                                style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13))),
                        Text(Fmt.compact(t.quoteVolume),
                            style: const TextStyle(fontSize: 11, color: Colors.grey)),
                        const SizedBox(width: 12),
                        Text(Fmt.num2(t.price),
                            style: const TextStyle(fontSize: 12, color: Colors.grey)),
                        const SizedBox(width: 12),
                        PnlText(t.priceChangePercent, percent: true),
                      ]),
                    ),
                  );
                },
              );
            },
          ),
        ),
      ),
    ]);
  }
}

// ═══════════════════════════════════ NEW LISTINGS ══════════════════════════
class _NewListingsView extends ConsumerStatefulWidget {
  const _NewListingsView();
  @override
  ConsumerState<_NewListingsView> createState() => _NewListingsViewState();
}

class _NewListingsViewState extends ConsumerState<_NewListingsView> {
  bool _expanded = false;
  static const _preview = 8;

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(newListingsProvider);
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(newListingsProvider),
      child: async.when(
        skipLoadingOnReload: true,
        loading: () => const LoadingBlock(height: 300),
        error: (e, _) => ListView(children: [ErrorRetry(e, () => ref.invalidate(newListingsProvider))]),
        data: (list) {
          if (list.isEmpty) return ListView(children: const [EmptyState('Chưa có niêm yết mới')]);
          final showAll = _expanded || list.length <= _preview;
          final shown = showAll ? list : list.take(_preview).toList();
          return ListView.separated(
            padding: const EdgeInsets.all(12),
            itemCount: shown.length + 1,
            separatorBuilder: (_, _) => const Divider(height: 1, color: AppTheme.border),
            itemBuilder: (_, i) {
              if (i == shown.length) {
                return _MoreFooter(
                  total: list.length,
                  preview: _preview,
                  expanded: _expanded,
                  onTap: () => setState(() => _expanded = !_expanded),
                );
              }
              final t = shown[i];
              return InkWell(
                onTap: () => openChart(context, t.code),
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Row(children: [
                    CircleAvatar(
                      radius: 15,
                      backgroundColor: AppTheme.accent.withValues(alpha: 0.15),
                      child: Text(
                        t.baseAsset.isEmpty
                            ? '?'
                            : t.baseAsset.substring(0, math.min(3, t.baseAsset.length)),
                        style: const TextStyle(fontSize: 9, fontWeight: FontWeight.w700, color: AppTheme.accent),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text(t.baseAsset.isEmpty ? t.code : t.baseAsset,
                            style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 13)),
                        Text('${t.code} · ${Fmt.dt(t.listedAt)}',
                            style: const TextStyle(fontSize: 11, color: Colors.grey)),
                      ]),
                    ),
                    Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
                      Text(Fmt.num2(t.price), style: const TextStyle(fontSize: 12)),
                      PnlText(t.priceChangePercent, percent: true, size: 11),
                    ]),
                  ]),
                ),
              );
            },
          );
        },
      ),
    );
  }
}

// ════════════════════════════════════ WALLS ════════════════════════════════
class _WallGroup {
  final String symbol;
  OrderBookWall largest;
  double total;
  int extra;
  _WallGroup(this.symbol, this.largest, this.total, this.extra);
}

class _WallsView extends ConsumerWidget {
  const _WallsView();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(wallsProvider);
    final f = ref.watch(wallsFilterProvider);
    final n = ref.read(wallsFilterProvider.notifier);

    return Column(children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(12, 10, 12, 4),
        child: Column(children: [
          SizedBox(
            width: double.infinity,
            child: SegmentedButton<String>(
              style: const ButtonStyle(visualDensity: VisualDensity.compact),
              segments: const [
                ButtonSegment(value: '', label: Text('Cả 2')),
                ButtonSegment(value: 'Bid', label: Text('Mua'), icon: Icon(Icons.arrow_upward, size: 14)),
                ButtonSegment(value: 'Ask', label: Text('Bán'), icon: Icon(Icons.arrow_downward, size: 14)),
              ],
              selected: {f.side},
              onSelectionChanged: (s) => n.set(f.copyWith(side: s.first)),
            ),
          ),
          const SizedBox(height: 8),
          Wrap(spacing: 7, children: [
            for (final v in [1000000.0, 10000000.0, 50000000.0, 200000000.0])
              ChoiceChip(
                label: Text('≥${Fmt.compact(v)}'),
                visualDensity: VisualDensity.compact,
                selected: f.minNotional == v,
                onSelected: (_) => n.set(f.copyWith(minNotional: v)),
              ),
          ]),
        ]),
      ),
      Expanded(
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(wallsProvider),
          child: async.when(
            skipLoadingOnReload: true,
            loading: () => const LoadingBlock(height: 300),
            error: (e, _) => ListView(children: [ErrorRetry(e, () => ref.invalidate(wallsProvider))]),
            data: (list) {
              if (list.isEmpty) {
                return ListView(children: const [
                  EmptyState('Không tường lệnh nào khớp. Thử hạ ngưỡng USDT.'),
                ]);
              }
              final groups = _group(list);
              return ListView.separated(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
                itemCount: groups.length,
                separatorBuilder: (_, _) => const Divider(height: 1, color: AppTheme.border),
                itemBuilder: (_, i) => _wallTile(context, groups[i]),
              );
            },
          ),
        ),
      ),
    ]);
  }

  List<_WallGroup> _group(List<OrderBookWall> ws) {
    final m = <String, _WallGroup>{};
    for (final w in ws) {
      final g = m[w.symbol];
      if (g == null) {
        m[w.symbol] = _WallGroup(w.symbol, w, w.quoteNotional, 0);
      } else {
        g.total += w.quoteNotional;
        g.extra++;
        if (w.quoteNotional > g.largest.quoteNotional) g.largest = w;
      }
    }
    return m.values.toList()
      ..sort((a, b) => b.largest.quoteNotional.compareTo(a.largest.quoteNotional));
  }

  Widget _wallTile(BuildContext context, _WallGroup g) {
    final w = g.largest;
    final isBid = w.side.toLowerCase() == 'bid';
    final c = isBid ? AppTheme.up : AppTheme.down;
    return InkWell(
      onTap: () => openChart(context, w.symbol),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 9),
        child: Row(children: [
          AppChip(isBid ? 'MUA' : 'BÁN', color: c, filled: true),
          const SizedBox(width: 10),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                Text(w.symbol, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 13)),
                if (g.extra > 0)
                  Padding(
                    padding: const EdgeInsets.only(left: 6),
                    child: Text('+${g.extra}',
                        style: const TextStyle(fontSize: 10, color: Colors.grey)),
                  ),
              ]),
              Text(
                  '@${Fmt.num2(w.price)} · ${w.distanceFromMidPercent.toStringAsFixed(2)}% từ mid · ${w.multiplier.toStringAsFixed(1)}×',
                  style: const TextStyle(fontSize: 10.5, color: Colors.grey)),
            ]),
          ),
          Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
            Text(Fmt.compact(w.quoteNotional),
                style: TextStyle(fontSize: 13, color: c, fontWeight: FontWeight.w700)),
            if (g.extra > 0)
              Text('tổng ${Fmt.compact(g.total)}',
                  style: const TextStyle(fontSize: 9.5, color: Colors.grey)),
          ]),
        ]),
      ),
    );
  }
}

// ════════════════════════════════════ SENTIMENT ════════════════════════════
class _SentimentView extends ConsumerWidget {
  const _SentimentView();
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(sentimentRecentProvider);
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(sentimentRecentProvider),
      child: async.when(
        skipLoadingOnReload: true,
        loading: () => const LoadingBlock(height: 300),
        error: (e, _) =>
            ListView(children: [ErrorRetry(e, () => ref.invalidate(sentimentRecentProvider))]),
        data: (list) {
          if (list.isEmpty) return ListView(children: const [EmptyState('Chưa có tin')]);
          return ListView.separated(
            padding: const EdgeInsets.all(12),
            itemCount: list.length,
            separatorBuilder: (_, _) => const Divider(height: 1, color: AppTheme.border),
            itemBuilder: (_, i) {
              final s = list[i];
              return Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  SizedBox(
                      width: 64,
                      child: AppChip(s.symbolCode.isEmpty ? '—' : s.symbolCode,
                          color: Fmt.pnlColor(s.score), filled: true)),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(s.headline,
                          style: const TextStyle(fontSize: 12.5),
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis),
                      Text('${s.source} · ${Fmt.dt(s.at)} · score ${s.score.toStringAsFixed(2)}',
                          style: const TextStyle(fontSize: 10.5, color: Colors.grey)),
                    ]),
                  ),
                ]),
              );
            },
          );
        },
      ),
    );
  }
}

// ════════════════════════════════════ SHARED ═══════════════════════════════
class _MoreFooter extends StatelessWidget {
  final int total;
  final int preview;
  final bool expanded;
  final VoidCallback onTap;
  const _MoreFooter({
    required this.total,
    required this.preview,
    required this.expanded,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    // List ngắn hơn ngưỡng preview → luôn hiện hết, không cần nút.
    if (total <= preview) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Center(
        child: TextButton.icon(
          onPressed: onTap,
          icon: Icon(expanded ? Icons.expand_less : Icons.expand_more, size: 18),
          label: Text(expanded ? 'Thu gọn' : 'Xem thêm (${total - preview})'),
        ),
      ),
    );
  }
}
