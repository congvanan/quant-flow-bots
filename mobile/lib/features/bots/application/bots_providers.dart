import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/bot_models.dart';
import '../data/bots_repository.dart';

final botsRepositoryProvider =
    Provider<BotsRepository>((ref) => BotsRepository(ref.watch(dioProvider)));

final botsListProvider =
    FutureProvider<List<BotDto>>((ref) => ref.watch(botsRepositoryProvider).list());

final botsStatsSummaryProvider =
    FutureProvider<List<BotStatsRow>>((ref) => ref.watch(botsRepositoryProvider).statsSummary());

final botByIdProvider =
    FutureProvider.family<BotDto?, String>((ref, id) => ref.watch(botsRepositoryProvider).byId(id));

final botPositionsProvider = FutureProvider.family<List<PositionDto>, String>(
    (ref, id) => ref.watch(botsRepositoryProvider).positions(id));

final botOrdersProvider = FutureProvider.family<List<OrderDto>, String>(
    (ref, id) => ref.watch(botsRepositoryProvider).orders(id));

final botAccountsProvider = FutureProvider.family<List<BotAccountDto>, String>(
    (ref, id) => ref.watch(botsRepositoryProvider).accounts(id));

final botRiskEventsProvider = FutureProvider.family<List<RiskEventDto>, String>(
    (ref, id) => ref.watch(botsRepositoryProvider).riskEvents(id));

final botSignalsProvider = FutureProvider.family<List<SignalDto>, String>(
    (ref, id) => ref.watch(botsRepositoryProvider).signals(id));
