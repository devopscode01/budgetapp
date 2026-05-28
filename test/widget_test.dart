import 'package:flutter_test/flutter_test.dart';
import 'package:budget_mobile/main.dart';

void main() {
  testWidgets('App launches smoke test', (WidgetTester tester) async {
    await tester.pumpWidget(const BudgetApp());
    expect(find.byType(BudgetApp), findsOneWidget);
  });
}
