import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_providers.dart';
import '../../core/config/server_config.dart';
import '../../core/theme/app_theme.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});
  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _busy = false;
  bool _obscure = true;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(authControllerProvider.notifier)
          .login(_email.text.trim(), _password.text);
      // Điều hướng do router redirect tự xử lý khi auth state đổi.
    } catch (e) {
      // Lỗi kết nối → có thể URL tunnel đã đổi. Thử tự lấy URL mới từ Gist rồi login lại 1 lần.
      if (e.toString().contains('Không kết nối') && await ServerConfig.syncFromPointer()) {
        try {
          await ref.read(authControllerProvider.notifier)
              .login(_email.text.trim(), _password.text);
          return;
        } catch (e2) {
          if (mounted) setState(() => _error = e2.toString());
        }
      } else {
        if (mounted) setState(() => _error = e.toString());
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _syncServer() async {
    setState(() => _busy = true);
    final updated = await ServerConfig.syncFromPointer();
    if (!mounted) return;
    setState(() {
      _busy = false;
      if (updated) _error = null;
    });
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(updated
          ? 'Đã lấy server mới: ${_shortUrl(ServerConfig.baseUrl)}'
          : 'Không lấy được URL mới (kiểm tra mạng / tunnel chưa chạy)'),
    ));
  }

  String _shortUrl(String u) =>
      u.replaceFirst(RegExp(r'^https?://'), '').replaceFirst(RegExp(r'/+$'), '');

  Future<void> _editServer() async {
    final c = TextEditingController(text: ServerConfig.baseUrl);
    final saved = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Địa chỉ server API'),
        content: Column(mainAxisSize: MainAxisSize.min, children: [
          TextField(
            controller: c,
            autocorrect: false,
            keyboardType: TextInputType.url,
            decoration: const InputDecoration(hintText: 'https://...trycloudflare.com', isDense: true),
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
      if (mounted) {
        setState(() => _error = null);
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('Đã lưu server. Thử đăng nhập lại.')));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const Icon(Icons.bolt, color: AppTheme.accent, size: 28),
                      const SizedBox(width: 8),
                      Text('QUANT FLOW',
                          style: Theme.of(context).textTheme.titleLarge?.copyWith(
                              fontWeight: FontWeight.bold, letterSpacing: 1.5)),
                    ],
                  ),
                  const SizedBox(height: 4),
                  const Text('PAPER + LIVE · BINANCE TESTNET',
                      textAlign: TextAlign.center,
                      style: TextStyle(fontSize: 11, color: Colors.grey)),
                  const SizedBox(height: 32),
                  TextField(
                    controller: _email,
                    keyboardType: TextInputType.emailAddress,
                    autocorrect: false,
                    decoration: const InputDecoration(labelText: 'Email'),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _password,
                    obscureText: _obscure,
                    onSubmitted: (_) => _submit(),
                    decoration: InputDecoration(
                      labelText: 'Mật khẩu',
                      suffixIcon: IconButton(
                        icon: Icon(_obscure ? Icons.visibility : Icons.visibility_off),
                        onPressed: () => setState(() => _obscure = !_obscure),
                      ),
                    ),
                  ),
                  if (_error != null) ...[
                    const SizedBox(height: 12),
                    Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                        color: AppTheme.down.withValues(alpha: 0.12),
                        borderRadius: BorderRadius.circular(8),
                        border: Border.all(color: AppTheme.down.withValues(alpha: 0.4)),
                      ),
                      child: Text(_error!, style: const TextStyle(color: AppTheme.down)),
                    ),
                  ],
                  const SizedBox(height: 20),
                  FilledButton(
                    onPressed: _busy ? null : _submit,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 12),
                      child: _busy
                          ? const SizedBox(
                              height: 18, width: 18,
                              child: CircularProgressIndicator(strokeWidth: 2, color: Colors.black))
                          : const Text('Đăng nhập'),
                    ),
                  ),
                  const SizedBox(height: 16),
                  // Đổi server tại màn login + nút tự lấy URL tunnel mới nhất từ Gist.
                  Row(children: [
                    Expanded(
                      child: OutlinedButton.icon(
                        onPressed: _busy ? null : _editServer,
                        icon: const Icon(Icons.dns_outlined, size: 16),
                        label: Text('Server: ${_shortUrl(ServerConfig.baseUrl)}',
                            overflow: TextOverflow.ellipsis),
                      ),
                    ),
                    const SizedBox(width: 8),
                    IconButton(
                      tooltip: 'Tự lấy URL mới nhất',
                      onPressed: _busy ? null : _syncServer,
                      icon: const Icon(Icons.cloud_sync_outlined),
                    ),
                  ]),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
