import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'package:quantflow_mobile/main.dart';

void main() {
  testWidgets('App khởi động vào splash/loading', (WidgetTester tester) async {
    await tester.pumpWidget(const ProviderScope(child: QuantFlowApp()));
    // Lúc bootstrap auth → màn splash có CircularProgressIndicator.
    expect(find.byType(CircularProgressIndicator), findsWidgets);
  });
}
