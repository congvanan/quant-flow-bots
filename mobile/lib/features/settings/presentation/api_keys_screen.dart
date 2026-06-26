import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_theme.dart';
import '../../../shared/widgets/ui.dart';
import '../data/settings_repository.dart';

class ApiKeysScreen extends ConsumerWidget {
  const ApiKeysScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(apiKeysProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('API keys')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _add(context, ref),
        icon: const Icon(Icons.add),
        label: const Text('Thêm key'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(apiKeysProvider),
        child: async.when(
          loading: () => const LoadingBlock(height: 300),
          error: (e, _) => ListView(children: [ErrorRetry(e, () => ref.invalidate(apiKeysProvider))]),
          data: (list) {
            if (list.isEmpty) return ListView(children: const [EmptyState('Chưa có API key', icon: Icons.vpn_key_outlined)]);
            return ListView.separated(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 88),
              itemCount: list.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (_, i) {
                final k = list[i];
                return Card(
                  child: ListTile(
                    title: Row(children: [
                      Flexible(child: Text(k.label, style: const TextStyle(fontWeight: FontWeight.w600), overflow: TextOverflow.ellipsis)),
                      const SizedBox(width: 8),
                      StatusPill(k.isActive ? 'active' : 'tắt', k.isActive ? AppTheme.up : Colors.grey),
                    ]),
                    subtitle: Text('${k.exchangeCode} · ${k.mode} · ${k.keyPreview}${k.lastError != null ? '\n⚠ ${k.lastError}' : ''}',
                        style: const TextStyle(fontSize: 11)),
                    isThreeLine: k.lastError != null,
                    trailing: PopupMenuButton<String>(
                      onSelected: (v) async {
                        final repo = ref.read(settingsRepositoryProvider);
                        try {
                          if (v == 'validate') {
                            await repo.validateKey(k.id);
                          } else if (v == 'toggle') {
                            await repo.toggleKey(k.id, k.isActive);
                          } else if (v == 'del') {
                            await repo.deleteKey(k.id);
                          }
                          ref.invalidate(apiKeysProvider);
                        } catch (e) {
                          if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('$e')));
                        }
                      },
                      itemBuilder: (_) => [
                        const PopupMenuItem(value: 'validate', child: Text('Kiểm tra (validate)')),
                        PopupMenuItem(value: 'toggle', child: Text(k.isActive ? 'Tắt' : 'Bật')),
                        const PopupMenuItem(value: 'del', child: Text('Xóa')),
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

  Future<void> _add(BuildContext context, WidgetRef ref) async {
    final exchanges = await ref.read(exchangesProvider.future);
    if (!context.mounted) return;
    int? exchangeId = exchanges.isNotEmpty ? exchanges.first.id : null;
    String mode = 'Paper';
    final label = TextEditingController();
    final key = TextEditingController();
    final secret = TextEditingController();
    final perms = TextEditingController(text: '{"spot":true,"trade":false,"withdraw":false}');
    String? err;
    await showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppTheme.surface,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setLocal) => Padding(
          padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
          child: SingleChildScrollView(
            child: Column(mainAxisSize: MainAxisSize.min, children: [
              const Text('Thêm API key', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16)),
              const SizedBox(height: 12),
              DropdownButtonFormField<int>(
                initialValue: exchangeId,
                isExpanded: true,
                decoration: const InputDecoration(labelText: 'Sàn'),
                items: exchanges.map((e) => DropdownMenuItem(value: e.id, child: Text(e.name))).toList(),
                onChanged: (v) => setLocal(() => exchangeId = v),
              ),
              const SizedBox(height: 10),
              TextField(controller: label, decoration: const InputDecoration(labelText: 'Nhãn (vd Account 1)')),
              const SizedBox(height: 10),
              DropdownButtonFormField<String>(
                initialValue: mode,
                decoration: const InputDecoration(labelText: 'Mode'),
                items: const [DropdownMenuItem(value: 'Paper', child: Text('Paper')), DropdownMenuItem(value: 'Live', child: Text('Live'))],
                onChanged: (v) => setLocal(() => mode = v ?? 'Paper'),
              ),
              const SizedBox(height: 10),
              TextField(controller: key, decoration: const InputDecoration(labelText: 'API key')),
              const SizedBox(height: 10),
              TextField(controller: secret, obscureText: true, decoration: const InputDecoration(labelText: 'API secret')),
              const SizedBox(height: 10),
              TextField(controller: perms, maxLines: 2, style: const TextStyle(fontFamily: 'monospace', fontSize: 12), decoration: const InputDecoration(labelText: 'Permissions JSON')),
              if (err != null) ...[const SizedBox(height: 8), Text(err!, style: const TextStyle(color: AppTheme.down))],
              const SizedBox(height: 14),
              SizedBox(
                width: double.infinity,
                child: FilledButton(
                  onPressed: () async {
                    try {
                      await ref.read(settingsRepositoryProvider).createKey({
                        'exchangeId': exchangeId,
                        'label': label.text.trim(),
                        'mode': mode,
                        'apiKey': key.text.trim(),
                        'apiSecret': secret.text.trim(),
                        'permissionsJson': perms.text.trim(),
                      });
                      ref.invalidate(apiKeysProvider);
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
      ),
    );
  }
}
