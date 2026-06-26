import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import 'market_models.dart';

class MarketRepository {
  final Dio _dio;
  MarketRepository(this._dio);

  Future<AccountStats> accountStats() => _one('/api/market/account-stats', AccountStats.fromJson);
  Future<MarketOverview> overview() => _one('/api/market/overview', MarketOverview.fromJson);

  Future<List<AlphaPriceTick>> alphaPrices() async {
    final j = await _map('/api/market/alpha/prices');
    return ((j['items'] ?? []) as List).map((e) => AlphaPriceTick.fromJson(e)).toList();
  }

  Future<List<AlphaToken>> alphaTokens() async {
    final j = await _map('/api/market/alpha');
    return ((j['items'] ?? []) as List).map((e) => AlphaToken.fromJson(e)).toList();
  }

  Future<ScannerResponse> scanner({
    required double minVolume,
    required double minPct,
    required double maxPct,
    String window = '1d',
    String direction = 'any',
    int maxSymbols = 100,
  }) async {
    final j = await _map('/api/market/scanner', query: {
      'minVolume': minVolume,
      'minPct': minPct,
      'maxPct': maxPct,
      'windowSize': window,
      'direction': direction,
      'maxSymbols': maxSymbols,
    });
    return ScannerResponse.fromJson(j);
  }

  Future<List<NewListing>> newListings({int limit = 30}) async {
    final res = await _dioGet('/api/market/new-listings', query: {'limit': limit});
    return (res as List).map((e) => NewListing.fromJson(e)).toList();
  }

  Future<List<OrderBookWall>> walls({
    double minNotional = 500000,
    double maxDistancePct = 2,
    String side = '', // '' = cả hai, 'Bid', 'Ask'
    int limit = 100,
  }) async {
    final j = await _map('/api/market/order-book-walls', query: {
      'minNotional': minNotional,
      'maxDistancePct': maxDistancePct,
      if (side.isNotEmpty) 'side': side,
      'limit': limit,
    });
    return ((j['results'] ?? []) as List).map((e) => OrderBookWall.fromJson(e)).toList();
  }

  Future<List<Candle>> candles(String symbol, String interval,
      {int limit = 200, bool futures = false}) async {
    final res = await _dioGet('/api/market/candles', query: {
      'symbol': symbol.toUpperCase(),
      'interval': interval,
      'limit': limit,
      if (futures) 'market': 'futures',
    });
    return (res as List).map((e) => Candle.fromJson(e)).toList();
  }

  Future<List<SentimentItem>> sentimentRecent({int limit = 30}) async {
    final res = await _dioGet('/api/sentiment/recent', query: {'limit': limit});
    return (res as List).map((e) => SentimentItem.fromJson(e)).toList();
  }

  Future<T> _one<T>(String path, T Function(Map<String, dynamic>) parse) async =>
      parse(await _map(path));

  Future<Map<String, dynamic>> _map(String path, {Map<String, dynamic>? query}) async {
    final res = await _dioGet(path, query: query);
    return res as Map<String, dynamic>;
  }

  Future<dynamic> _dioGet(String path, {Map<String, dynamic>? query}) async {
    try {
      final res = await _dio.get(path, queryParameters: query);
      return res.data;
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }
}
