import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../../../core/realtime/poll.dart';
import '../data/market_models.dart';
import '../data/market_repository.dart';

final marketRepositoryProvider =
    Provider<MarketRepository>((ref) => MarketRepository(ref.watch(dioProvider)));

// ── Polling intervals (mirror refetchInterval bên web) ──────────────────────
const _alphaPriceEvery = Duration(seconds: 3); // web 2s
const _overviewEvery = Duration(seconds: 20);
const _scannerEvery = Duration(seconds: 20);
const _listingsEvery = Duration(seconds: 30);
const _wallsEvery = Duration(seconds: 20);
const _sentimentEvery = Duration(seconds: 60);
const _alphaTokensEvery = Duration(seconds: 60); // data nặng: name/marketCap/sparkline

final accountStatsProvider = FutureProvider.autoDispose<AccountStats>((ref) {
  autoPoll(ref, _overviewEvery);
  return ref.watch(marketRepositoryProvider).accountStats();
});

final marketOverviewProvider = FutureProvider.autoDispose<MarketOverview>((ref) {
  autoPoll(ref, _overviewEvery);
  return ref.watch(marketRepositoryProvider).overview();
});

/// Live tick (giá/pct/funding) — poll nhanh 3s.
final alphaPricesProvider = FutureProvider.autoDispose<List<AlphaPriceTick>>((ref) {
  autoPoll(ref, _alphaPriceEvery);
  return ref.watch(marketRepositoryProvider).alphaPrices();
});

/// Data nặng (name/chain/marketCap/sparkline) — poll chậm 60s.
final alphaTokensProvider = FutureProvider.autoDispose<List<AlphaToken>>((ref) {
  autoPoll(ref, _alphaTokensEvery);
  return ref.watch(marketRepositoryProvider).alphaTokens();
});

final newListingsProvider = FutureProvider.autoDispose<List<NewListing>>((ref) {
  autoPoll(ref, _listingsEvery);
  return ref.watch(marketRepositoryProvider).newListings();
});

final sentimentRecentProvider = FutureProvider.autoDispose<List<SentimentItem>>((ref) {
  autoPoll(ref, _sentimentEvery);
  return ref.watch(marketRepositoryProvider).sentimentRecent();
});

// ── Alpha: bộ lọc hướng tăng/giảm + ô search ────────────────────────────────
enum AlphaDir { all, gainers, losers }

class AlphaFilter {
  final String query;
  final AlphaDir dir;
  const AlphaFilter({this.query = '', this.dir = AlphaDir.all});
  AlphaFilter copyWith({String? query, AlphaDir? dir}) =>
      AlphaFilter(query: query ?? this.query, dir: dir ?? this.dir);
}

final alphaFilterProvider =
    NotifierProvider<AlphaFilterNotifier, AlphaFilter>(AlphaFilterNotifier.new);

class AlphaFilterNotifier extends Notifier<AlphaFilter> {
  @override
  AlphaFilter build() => const AlphaFilter();
  void setQuery(String q) => state = state.copyWith(query: q);
  void setDir(AlphaDir d) => state = state.copyWith(dir: d);
}

// ── Scanner: filter + direction ─────────────────────────────────────────────
class ScannerFilter {
  final double minVolume;
  final double minPct;
  final double maxPct;
  final String direction; // any/up/down
  const ScannerFilter({
    this.minVolume = 50000000,
    this.minPct = 5,
    this.maxPct = 25,
    this.direction = 'any',
  });
  ScannerFilter copyWith({double? minVolume, double? minPct, double? maxPct, String? direction}) =>
      ScannerFilter(
        minVolume: minVolume ?? this.minVolume,
        minPct: minPct ?? this.minPct,
        maxPct: maxPct ?? this.maxPct,
        direction: direction ?? this.direction,
      );
}

final scannerFilterProvider =
    NotifierProvider<ScannerFilterNotifier, ScannerFilter>(ScannerFilterNotifier.new);

class ScannerFilterNotifier extends Notifier<ScannerFilter> {
  @override
  ScannerFilter build() => const ScannerFilter();
  void set(ScannerFilter f) => state = f;
}

final scannerProvider = FutureProvider.autoDispose<ScannerResponse>((ref) {
  autoPoll(ref, _scannerEvery);
  final f = ref.watch(scannerFilterProvider);
  return ref.watch(marketRepositoryProvider).scanner(
        minVolume: f.minVolume,
        minPct: f.minPct,
        maxPct: f.maxPct,
        direction: f.direction,
      );
});

// ── Walls: filter notional + side ───────────────────────────────────────────
class WallsFilter {
  final double minNotional;
  final String side; // ''=cả hai, Bid, Ask
  const WallsFilter({this.minNotional = 1000000, this.side = ''});
  WallsFilter copyWith({double? minNotional, String? side}) =>
      WallsFilter(minNotional: minNotional ?? this.minNotional, side: side ?? this.side);
}

final wallsFilterProvider =
    NotifierProvider<WallsFilterNotifier, WallsFilter>(WallsFilterNotifier.new);

class WallsFilterNotifier extends Notifier<WallsFilter> {
  @override
  WallsFilter build() => const WallsFilter();
  void set(WallsFilter f) => state = f;
}

final wallsProvider = FutureProvider.autoDispose<List<OrderBookWall>>((ref) {
  autoPoll(ref, _wallsEvery);
  final f = ref.watch(wallsFilterProvider);
  return ref.watch(marketRepositoryProvider).walls(minNotional: f.minNotional, side: f.side);
});

// ── Candles cho chart nến (symbol + interval + futures) — poll 10s ──────────────
typedef CandleKey = ({String symbol, String interval, bool futures});

final candlesProvider =
    FutureProvider.autoDispose.family<List<Candle>, CandleKey>((ref, key) {
  autoPoll(ref, const Duration(seconds: 10));
  return ref
      .watch(marketRepositoryProvider)
      .candles(key.symbol, key.interval, futures: key.futures);
});
