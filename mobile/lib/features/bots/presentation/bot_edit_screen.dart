import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../settings/data/settings_repository.dart';
import '../../strategies/data/strategy_repository.dart';
import '../application/bots_providers.dart';
import '../data/bot_models.dart';

/// Tạo mới (bot == null) hoặc sửa cấu hình risk (bot != null).
class BotEditScreen extends ConsumerStatefulWidget {
  final BotDto? bot;
  const BotEditScreen({super.key, this.bot});
  @override
  ConsumerState<BotEditScreen> createState() => _BotEditScreenState();
}

class _BotEditScreenState extends ConsumerState<BotEditScreen> {
  late final TextEditingController _name;
  late final TextEditingController _symbol;
  late final TextEditingController _baseEquity;
  late final TextEditingController _maxPos;
  late final TextEditingController _leverage;
  late final TextEditingController _sl;
  late final TextEditingController _tp;
  late final TextEditingController _dailyLoss;
  late final TextEditingController _maxOpen;

  String _market = 'Spot';
  String _runMode = 'PaperTrading';
  String? _strategyId;
  String? _apiKeyId;
  bool _busy = false;
  String? _error;

  bool get isEdit => widget.bot != null;

  @override
  void initState() {
    super.initState();
    final b = widget.bot;
    _name = TextEditingController(text: b?.name ?? '');
    _symbol = TextEditingController(text: b?.symbolCode ?? '');
    _baseEquity = TextEditingController(text: (b?.baseEquityUsdt ?? 1000).toStringAsFixed(0));
    _maxPos = TextEditingController(text: '0');
    _leverage = TextEditingController(text: (b?.leverage ?? 1).toString());
    _sl = TextEditingController();
    _tp = TextEditingController();
    _dailyLoss = TextEditingController(text: '4');
    _maxOpen = TextEditingController(text: '1');
    if (b != null) {
      _market = b.executionMarket;
      _runMode = b.runMode;
      _strategyId = b.strategyId;
      _apiKeyId = b.apiKeyId;
    }
  }

  @override
  void dispose() {
    for (final c in [_name, _symbol, _baseEquity, _maxPos, _leverage, _sl, _tp, _dailyLoss, _maxOpen]) {
      c.dispose();
    }
    super.dispose();
  }

  double? _num(TextEditingController c) => c.text.trim().isEmpty ? null : double.tryParse(c.text.trim());

  Future<void> _submit() async {
    if (_name.text.trim().isEmpty) return setState(() => _error = 'Nhập tên bot');
    if (!isEdit && (_strategyId == null || _symbol.text.trim().isEmpty)) {
      return setState(() => _error = 'Chọn strategy + nhập symbol');
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final repo = ref.read(botsRepositoryProvider);
      final common = <String, dynamic>{
        'runMode': _runMode,
        'executionMarket': _market,
        'apiKeyId': _apiKeyId,
        'leverage': int.tryParse(_leverage.text) ?? 1,
        'baseEquityUsdt': _num(_baseEquity),
        'maxPositionSize': _num(_maxPos) ?? 0,
        'defaultStopLossPercent': _num(_sl),
        'defaultTakeProfitPercent': _num(_tp),
        'dailyLossStopPercent': _num(_dailyLoss),
        'maxOpenPositions': int.tryParse(_maxOpen.text) ?? 1,
      };
      if (isEdit) {
        await repo.updateRisk(widget.bot!.id, common);
        ref.invalidate(botByIdProvider(widget.bot!.id));
      } else {
        await repo.create({
          ...common,
          'name': _name.text.trim(),
          'strategyId': _strategyId,
          'symbolCode': _symbol.text.trim().toUpperCase(),
        });
      }
      ref.invalidate(botsListProvider);
      ref.invalidate(botsStatsSummaryProvider);
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final strategies = ref.watch(strategiesProvider);
    final keys = ref.watch(apiKeysProvider);
    final marketKeys = keys.maybeWhen(
      data: (list) => list.where((k) =>
          (_market == 'Futures' && k.exchangeCode == 'binance-futures-testnet') ||
          (_market == 'Spot' && k.exchangeCode == 'binance-spot-testnet') ||
          (k.exchangeCode != 'binance-futures-testnet' && k.exchangeCode != 'binance-spot-testnet')).toList(),
      orElse: () => const <ApiKeyDto>[],
    );

    return Scaffold(
      appBar: AppBar(title: Text(isEdit ? 'Sửa bot' : 'Tạo bot')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        if (_error != null) ...[
          Container(
            padding: const EdgeInsets.all(10),
            decoration: BoxDecoration(
              color: AppTheme.down.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: AppTheme.down.withValues(alpha: 0.4)),
            ),
            child: Text(_error!, style: const TextStyle(color: AppTheme.down)),
          ),
          const SizedBox(height: 12),
        ],
        _field('Tên bot', _name),
        const SizedBox(height: 12),
        if (!isEdit) ...[
          strategies.when(
            loading: () => const LinearProgressIndicator(),
            error: (e, _) => Text('Lỗi tải strategy: $e', style: const TextStyle(color: AppTheme.down)),
            data: (list) => DropdownButtonFormField<String>(
              initialValue: _strategyId,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'Strategy'),
              items: list.map((s) => DropdownMenuItem(value: s.id, child: Text('${s.name} (${s.kind})'))).toList(),
              onChanged: (v) => setState(() => _strategyId = v),
            ),
          ),
          const SizedBox(height: 12),
          _field('Symbol (vd BTCUSDT)', _symbol, caps: true),
          const SizedBox(height: 12),
        ],
        _segmented('Thị trường', const ['Spot', 'Futures'], _market, (v) => setState(() => _market = v)),
        const SizedBox(height: 12),
        DropdownButtonFormField<String>(
          initialValue: _runMode,
          decoration: const InputDecoration(labelText: 'Chế độ chạy'),
          items: const [
            DropdownMenuItem(value: 'Off', child: Text('Off')),
            DropdownMenuItem(value: 'ScanOnly', child: Text('ScanOnly (chỉ tín hiệu)')),
            DropdownMenuItem(value: 'PaperTrading', child: Text('Paper')),
            DropdownMenuItem(value: 'LiveTrading', child: Text('Live')),
          ],
          onChanged: (v) => setState(() => _runMode = v ?? 'PaperTrading'),
        ),
        const SizedBox(height: 12),
        DropdownButtonFormField<String>(
          initialValue: _apiKeyId,
          isExpanded: true,
          decoration: const InputDecoration(labelText: 'API key (tùy chọn)'),
          items: [
            const DropdownMenuItem(value: null, child: Text('— không gắn —')),
            ...marketKeys.map((k) => DropdownMenuItem(value: k.id, child: Text('${k.label} (${k.mode})'))),
          ],
          onChanged: (v) => setState(() => _apiKeyId = v),
        ),
        const SizedBox(height: 12),
        Row(children: [
          Expanded(child: _field('Vốn (USDT)', _baseEquity, number: true)),
          const SizedBox(width: 12),
          Expanded(child: _field('Max position size', _maxPos, number: true)),
        ]),
        const SizedBox(height: 12),
        Row(children: [
          if (_market == 'Futures') ...[
            Expanded(child: _field('Đòn bẩy', _leverage, number: true)),
            const SizedBox(width: 12),
          ],
          Expanded(child: _field('Max lệnh mở', _maxOpen, number: true)),
        ]),
        const SizedBox(height: 12),
        Row(children: [
          Expanded(child: _field('Stop-loss %', _sl, number: true)),
          const SizedBox(width: 12),
          Expanded(child: _field('Take-profit %', _tp, number: true)),
        ]),
        const SizedBox(height: 12),
        _field('Daily loss stop %', _dailyLoss, number: true),
        const SizedBox(height: 24),
        FilledButton(
          onPressed: _busy ? null : _submit,
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 12),
            child: _busy
                ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.black))
                : Text(isEdit ? 'Lưu thay đổi' : 'Tạo bot'),
          ),
        ),
      ]),
    );
  }

  Widget _field(String label, TextEditingController c, {bool number = false, bool caps = false}) => TextField(
        controller: c,
        keyboardType: number ? const TextInputType.numberWithOptions(decimal: true) : TextInputType.text,
        textCapitalization: caps ? TextCapitalization.characters : TextCapitalization.none,
        decoration: InputDecoration(labelText: label),
      );

  Widget _segmented(String label, List<String> options, String value, ValueChanged<String> onChange) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: const TextStyle(fontSize: 12, color: Colors.grey)),
          const SizedBox(height: 6),
          SegmentedButton<String>(
            segments: options.map((o) => ButtonSegment(value: o, label: Text(o))).toList(),
            selected: {value},
            onSelectionChanged: (s) => onChange(s.first),
          ),
        ],
      );
}
