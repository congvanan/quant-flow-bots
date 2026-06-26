// Models thị trường — mirror api.ts + alpha.tsx (JSON camelCase từ BE).

double _d(dynamic v) => v == null ? 0 : (v is num ? v.toDouble() : double.tryParse('$v') ?? 0);
int _i(dynamic v) => v == null ? 0 : (v is num ? v.toInt() : int.tryParse('$v') ?? 0);

class AccountStats {
  final int openPositions;
  final double todayPnl;
  AccountStats(this.openPositions, this.todayPnl);
  factory AccountStats.fromJson(Map<String, dynamic> j) =>
      AccountStats(_i(j['openPositions']), _d(j['todayPnl']));
}

class MarketTicker {
  final String symbol;
  final double lastPrice;
  final double priceChangePercent;
  final double quoteVolume;
  MarketTicker(this.symbol, this.lastPrice, this.priceChangePercent, this.quoteVolume);
  factory MarketTicker.fromJson(Map<String, dynamic> j) => MarketTicker(
        (j['symbol'] ?? '').toString(),
        _d(j['lastPrice']),
        _d(j['priceChangePercent']),
        _d(j['quoteVolume']),
      );
}

class MarketOverview {
  final List<MarketTicker> topGainers;
  final List<MarketTicker> topVolume;
  MarketOverview(this.topGainers, this.topVolume);
  factory MarketOverview.fromJson(Map<String, dynamic> j) => MarketOverview(
        ((j['topGainers'] ?? []) as List).map((e) => MarketTicker.fromJson(e)).toList(),
        ((j['topVolume'] ?? []) as List).map((e) => MarketTicker.fromJson(e)).toList(),
      );
}

class ScannerResult {
  final String symbol;
  final double price;
  final double priceChangePercent;
  final double quoteVolume;
  ScannerResult(this.symbol, this.price, this.priceChangePercent, this.quoteVolume);
  factory ScannerResult.fromJson(Map<String, dynamic> j) => ScannerResult(
        (j['symbol'] ?? '').toString(),
        _d(j['price']),
        _d(j['priceChangePercent']),
        _d(j['quoteVolume']),
      );
}

class ScannerResponse {
  final int count;
  final List<ScannerResult> results;
  ScannerResponse(this.count, this.results);
  factory ScannerResponse.fromJson(Map<String, dynamic> j) => ScannerResponse(
        _i(j['count']),
        ((j['results'] ?? []) as List).map((e) => ScannerResult.fromJson(e)).toList(),
      );
}

/// Alpha token đầy đủ (endpoint /api/market/alpha) — name, marketCap, chain,
/// sparkline... Ghép với [AlphaPriceTick] (live giá/pct/funding) qua futuresSymbol.
class AlphaToken {
  final String symbol; // base, vd LAB
  final String futuresSymbol; // vd LABUSDT
  final String name;
  final String? chain;
  final String? iconUrl;
  final double price;
  final double percentChange24h;
  final double marketCap;
  final double volume24h;
  final double? liquidity;
  final int? holders;
  final List<double> sparkline;
  AlphaToken({
    required this.symbol,
    required this.futuresSymbol,
    required this.name,
    required this.chain,
    required this.iconUrl,
    required this.price,
    required this.percentChange24h,
    required this.marketCap,
    required this.volume24h,
    required this.liquidity,
    required this.holders,
    required this.sparkline,
  });
  factory AlphaToken.fromJson(Map<String, dynamic> j) => AlphaToken(
        symbol: (j['symbol'] ?? '').toString(),
        futuresSymbol: (j['futuresSymbol'] ?? j['symbol'] ?? '').toString(),
        name: (j['name'] ?? '').toString(),
        chain: (j['chain'] as String?)?.isEmpty == true ? null : j['chain'] as String?,
        iconUrl: (j['iconUrl'] as String?)?.isEmpty == true ? null : j['iconUrl'] as String?,
        price: _d(j['price']),
        percentChange24h: _d(j['percentChange24h']),
        marketCap: _d(j['marketCap']),
        volume24h: _d(j['volume24h']),
        liquidity: j['liquidity'] == null ? null : _d(j['liquidity']),
        holders: j['holders'] == null ? null : _i(j['holders']),
        sparkline: ((j['sparkline'] ?? []) as List).map(_d).toList(),
      );
}

class AlphaPriceTick {
  final String symbol;
  final double price;
  final double percentChange24h;
  final double quoteVolume24h;
  final double fundingRate;
  AlphaPriceTick(this.symbol, this.price, this.percentChange24h, this.quoteVolume24h, this.fundingRate);
  factory AlphaPriceTick.fromJson(Map<String, dynamic> j) => AlphaPriceTick(
        (j['symbol'] ?? '').toString(),
        _d(j['price']),
        _d(j['percentChange24h']),
        _d(j['quoteVolume24h']),
        _d(j['fundingRate']),
      );
}

class NewListing {
  final String code;
  final String baseAsset;
  final double price;
  final double priceChangePercent;
  final double quoteVolume;
  final DateTime listedAt;
  NewListing(this.code, this.baseAsset, this.price, this.priceChangePercent, this.quoteVolume, this.listedAt);
  factory NewListing.fromJson(Map<String, dynamic> j) => NewListing(
        (j['code'] ?? '').toString(),
        (j['baseAsset'] ?? '').toString(),
        _d(j['price']),
        _d(j['priceChangePercent']),
        _d(j['quoteVolume']),
        DateTime.tryParse((j['listedAt'] ?? '').toString()) ?? DateTime.now(),
      );
}

class OrderBookWall {
  final String symbol;
  final String side; // Bid/Ask
  final double price;
  final double quoteNotional;
  final double distanceFromMidPercent;
  final double multiplier;
  OrderBookWall(this.symbol, this.side, this.price, this.quoteNotional, this.distanceFromMidPercent, this.multiplier);
  factory OrderBookWall.fromJson(Map<String, dynamic> j) => OrderBookWall(
        (j['symbol'] ?? '').toString(),
        (j['side'] ?? '').toString(),
        _d(j['price']),
        _d(j['quoteNotional']),
        _d(j['distanceFromMidPercent']),
        _d(j['multiplier']),
      );
}

class Candle {
  final DateTime openTime;
  final double open;
  final double high;
  final double low;
  final double close;
  final double volume;
  Candle(this.openTime, this.open, this.high, this.low, this.close, this.volume);
  factory Candle.fromJson(Map<String, dynamic> j) => Candle(
        DateTime.tryParse((j['openTime'] ?? '').toString())?.toLocal() ?? DateTime.now(),
        _d(j['open']),
        _d(j['high']),
        _d(j['low']),
        _d(j['close']),
        _d(j['volume']),
      );
}

class SentimentItem {
  final String id;
  final String symbolCode;
  final String source;
  final String headline;
  final double score;
  final DateTime at;
  SentimentItem(this.id, this.symbolCode, this.source, this.headline, this.score, this.at);
  factory SentimentItem.fromJson(Map<String, dynamic> j) => SentimentItem(
        (j['id'] ?? '').toString(),
        (j['symbolCode'] ?? '').toString(),
        (j['source'] ?? '').toString(),
        (j['headline'] ?? '').toString(),
        _d(j['score']),
        DateTime.tryParse((j['at'] ?? j['ingestedAt'] ?? '').toString()) ?? DateTime.now(),
      );
}
