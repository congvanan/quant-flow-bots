import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui.dart';
import '../data/strategy_repository.dart';

class StrategiesScreen extends ConsumerWidget {
  const StrategiesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(strategiesProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Strategies')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _edit(context, ref, null),
        icon: const Icon(Icons.add),
        label: const Text('Tạo'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(strategiesProvider),
        child: async.when(
          loading: () => const LoadingBlock(height: 300),
          error: (e, _) => ListView(children: [ErrorRetry(e, () => ref.invalidate(strategiesProvider))]),
          data: (list) {
            if (list.isEmpty) return ListView(children: const [EmptyState('Chưa có strategy')]);
            return ListView.separated(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 88),
              itemCount: list.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (_, i) {
                final s = list[i];
                return Card(
                  child: ListTile(
                    title: Text(s.name, style: const TextStyle(fontWeight: FontWeight.w600)),
                    subtitle: Text('${s.kind}${s.runningBotCount > 0 ? ' · ${s.runningBotCount} bot live' : ''}',
                        style: const TextStyle(fontSize: 12)),
                    trailing: PopupMenuButton<String>(
                      onSelected: (v) async {
                        if (v == 'edit') {
                          _edit(context, ref, s);
                        } else if (v == 'del') {
                          await ref.read(strategyRepositoryProvider).remove(s.id);
                          ref.invalidate(strategiesProvider);
                        }
                      },
                      itemBuilder: (_) => const [
                        PopupMenuItem(value: 'edit', child: Text('Sửa')),
                        PopupMenuItem(value: 'del', child: Text('Xóa')),
                      ],
                    ),
                  ),
                );
              },
            );
          },
        ),
      ),
    );
  }

  Future<void> _edit(BuildContext context, WidgetRef ref, StrategyDto? s) async {
    final kinds = await ref.read(strategyKindsProvider.future);
    if (!context.mounted) return;
    final name = TextEditingController(text: s?.name ?? '');
    final params = TextEditingController(text: s?.parametersJson ?? '{}');
    String kind = s?.kind ?? (kinds.isNotEmpty ? kinds.first : '');
    String? err;
    await showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surface,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setLocal) => Padding(
          padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Text(s == null ? 'Tạo strategy' : 'Sửa strategy', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
            const SizedBox(height: 12),
            TextField(controller: name, decoration: const InputDecoration(labelText: 'Tên')),
            const SizedBox(height: 10),
            if (s == null)
              DropdownButtonFormField<String>(
                initialValue: kind.isEmpty ? null : kind,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Loại (kind)'),
                items: kinds.map((k) => DropdownMenuItem(value: k, child: Text(k))).toList(),
                onChanged: (v) => setLocal(() => kind = v ?? kind),
              ),
            const SizedBox(height: 10),
            TextField(
              controller: params,
              maxLines: 6,
              style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
              decoration: const InputDecoration(labelText: 'Tham số (JSON)', alignLabelWithHint: true),
            ),
            if (err != null) ...[const SizedBox(height: 8), Text(err!, style: const TextStyle(color: AppTheme.down))],
            const SizedBox(height: 14),
            SizedBox(
              width: double.infinity,
              child: FilledButton(
                onPressed: () async {
                  try {
                    final repo = ref.read(strategyRepositoryProvider);
                    if (s == null) {
                      await repo.create(name.text.trim(), kind, params.text.trim());
                    } else {
                      await repo.update(s.id, name.text.trim(), params.text.trim());
                    }
                    ref.invalidate(strategiesProvider);
                    if (ctx.mounted) Navigator.pop(ctx);
                  } catch (e) {
                    setLocal(() => err = '$e');
                  }
                },
                child: const Text('Lưu'),
              ),
            ),
            const SizedBox(height: 16),
          ]),
        ),
      ),
    );
  }
}
