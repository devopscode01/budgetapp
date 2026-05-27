import 'package:flutter/cupertino.dart';

class AppTheme {
  static const primary = Color(0xFF5B6EF7);
  static const background = Color(0xFF0F172A);
  static const surface = Color(0xFF1E293B);
  static const surfaceLight = Color(0xFF334155);
  static const income = Color(0xFF22C55E);
  static const spend = Color(0xFFEF4444);
  static const textPrimary = Color(0xFFF1F5F9);
  static const textSecondary = Color(0xFF94A3B8);

  static Color hexColor(String hex) {
    final h = hex.replaceFirst('#', '');
    return Color(int.parse('FF$h', radix: 16));
  }

  static CupertinoThemeData get theme => const CupertinoThemeData(
        brightness: Brightness.dark,
        primaryColor: primary,
        scaffoldBackgroundColor: background,
        barBackgroundColor: surface,
        textTheme: CupertinoTextThemeData(
          primaryColor: textPrimary,
          textStyle: TextStyle(color: textPrimary, fontFamily: '.SF Pro Text'),
          navTitleTextStyle: TextStyle(
            color: textPrimary,
            fontSize: 17,
            fontWeight: FontWeight.w600,
            fontFamily: '.SF Pro Text',
          ),
          navLargeTitleTextStyle: TextStyle(
            color: textPrimary,
            fontSize: 34,
            fontWeight: FontWeight.w700,
            fontFamily: '.SF Pro Display',
          ),
        ),
      );
}
