/// Người dùng đã đăng nhập (rút từ AuthResponse của BE hoặc giải mã JWT khi bootstrap).
class AuthUser {
  final String id;
  final String email;
  final String displayName;
  const AuthUser({required this.id, required this.email, required this.displayName});

  factory AuthUser.fromAuthJson(Map<String, dynamic> j) => AuthUser(
        id: (j['userId'] ?? '').toString(),
        email: (j['email'] ?? '').toString(),
        displayName: (j['displayName'] ?? j['email'] ?? '').toString(),
      );
}

/// Trạng thái auth toàn cục. loading = đang bootstrap (đọc token từ secure storage).
class AuthState {
  final bool loading;
  final AuthUser? user;
  const AuthState({this.loading = true, this.user});

  bool get isAuthenticated => user != null;

  AuthState copyWith({bool? loading, AuthUser? user, bool clearUser = false}) => AuthState(
        loading: loading ?? this.loading,
        user: clearUser ? null : (user ?? this.user),
      );
}
