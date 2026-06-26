import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/auth/auth_providers.dart';
import '../../../core/config/server_config.dart';
import '../../../core/theme/app_theme.dart';
import '../../settings/presentation/api_keys_screen.dart';
import '../../strategies/presentation/strategies_screen.dart';

class MoreScreen extends ConsumerWidget {
  const MoreScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(authControllerProvider).user;
    return Scaffold(
      appBar: AppBar(title: const Text('Thêm', style: TextStyle(fontWeight: FontWeight.bold)), titleSpacing: 16),
      body: ListView(children: [
        const SizedBox(height: 8),
        ListTile(
          leading: const CircleAvatar(backgroundColor: AppTheme.surface, child: Icon(Icons.person, color: AppTheme.accent)),
          title: Text(user?.displayName.isNotEmpty == true ? user!.displayName : (user?.email ?? '—')),
          subtitle: Text(user?.email ?? ''),
        ),
        const Divider(color: AppTheme.border),
        _tile(context, Icons.insights, 'Strategies', 'Tạo / sửa chiến lược', const StrategiesScreen()),
        _tile(context, Icons.vpn_key, 'API keys', 'Tài khoản Binance', const ApiKeysScreen()),
        ListTile(
          leading: const Icon(Icons.dns, color: AppTheme.accent),
          title: const Text('Server (API)'),
          subtitle: Text(ServerConfig.baseUrl, style: const TextStyle(fontSize: 12)),
          trailing: const Icon(Icons.edit, size: 18),
          onTap: () => _editServer(context),
        ),
        const Divider(color: AppTheme.border),
        ListTile(
          leading: const Icon(Icons.logout, color: AppTheme.down),
          title: const Text('Đăng xuất', style: TextStyle(color: AppTheme.down)),
          onTap: () => ref.read(authControllerProvider.notifier).logout(),
        ),
      ]),
    );
  }

  Future<void> _editServer(BuildContext context) async {
    final c = TextEditingController(text: ServerConfig.baseUrl);
    final saved = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Địa chỉ server API'),
        content: Column(mainAxisSize: MainAxisSize.min, children: [
          TextField(
            controller: c,
            decoration: const InputDecoration(hintText: 'http://192.168.x.x:5087', isDense: true),
          ),
          const SizedBox(height: 10),
          const Text(
            'USB: http://127.0.0.1:5087\nCùng Wi-Fi: http://<IP-PC>:5087\nInternet: https://<tunnel-hoặc-domain>',
            style: TextStyle(fontSize: 11, color: Colors.grey),
          ),
        ]),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Hủy')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Lưu')),
        ],
      ),
    );
    if (saved == true) {
      await ServerConfig.save(c.text);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Đã lưu server. Tải lại dữ liệu để áp dụng.')));
      }
    }
  }

  Widget _tile(BuildContext context, IconData icon, String title, String sub, Widget screen) => ListTile(
        leading: Icon(icon, color: AppTheme.accent),
        title: Text(title),
        subtitle: Text(sub, style: const TextStyle(fontSize: 12)),
        trailing: const Icon(Icons.chevron_right, size: 20),
        onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => screen)),
      );
}
