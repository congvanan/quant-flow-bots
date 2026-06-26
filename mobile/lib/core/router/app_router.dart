import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../auth/auth_providers.dart';
import '../../features/auth/login_screen.dart';
import '../../features/shell/home_shell.dart';
import '../../features/bots/presentation/bot_detail_screen.dart';

/// Router + auth guard. Trong lúc bootstrap (đọc token) → /splash. Xong: chưa đăng nhập →
/// /login, đã đăng nhập → /bots. Reactive với authControllerProvider qua refreshListenable.
final routerProvider = Provider<GoRouter>((ref) {
  final refresh = ValueNotifier(0);
  ref.listen(authControllerProvider, (_, _) => refresh.value++);
  ref.onDispose(refresh.dispose);

  return GoRouter(
    initialLocation: '/splash',
    refreshListenable: refresh,
    redirect: (context, state) {
      final auth = ref.read(authControllerProvider);
      final loc = state.matchedLocation;
      if (auth.loading) return loc == '/splash' ? null : '/splash';
      if (!auth.isAuthenticated) return loc == '/login' ? null : '/login';
      if (loc == '/splash' || loc == '/login') return '/home';
      return null;
    },
    routes: [
      GoRoute(path: '/splash', builder: (_, _) => const _Splash()),
      GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
      GoRoute(path: '/home', builder: (_, _) => const HomeShell()),
      GoRoute(
        path: '/bots/:id',
        builder: (_, s) => BotDetailScreen(botId: s.pathParameters['id']!),
      ),
    ],
  );
});

class _Splash extends StatelessWidget {
  const _Splash();
  @override
  Widget build(BuildContext context) =>
      const Scaffold(body: Center(child: CircularProgressIndicator()));
}
