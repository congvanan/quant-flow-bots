import 'package:flutter/material.dart';

/// Dark theme theo brand QUANT FLOW (nền tối, nhấn vàng/amber như logo ⚡).
class AppTheme {
  static const Color accent = Color(0xFFF5B027); // amber
  static const Color bg = Color(0xFF0B0E11);
  static const Color surface = Color(0xFF151A21);
  static const Color border = Color(0xFF252C36);
  static const Color up = Color(0xFF2EBD85);
  static const Color down = Color(0xFFF6465D);

  static ThemeData get dark {
    final scheme = ColorScheme.fromSeed(
      seedColor: accent,
      brightness: Brightness.dark,
      surface: surface,
    );
    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      scaffoldBackgroundColor: bg,
      colorScheme: scheme.copyWith(primary: accent, surface: surface),
      cardTheme: const CardThemeData(
        color: surface,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.all(Radius.circular(12)),
          side: BorderSide(color: border),
        ),
      ),
      appBarTheme: const AppBarTheme(backgroundColor: bg, surfaceTintColor: Colors.transparent),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: surface,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: const BorderSide(color: border),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: const BorderSide(color: border),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: accent,
          foregroundColor: Colors.black,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        ),
      ),
    );
  }
}
