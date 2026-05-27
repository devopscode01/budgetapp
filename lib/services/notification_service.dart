import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:timezone/timezone.dart' as tz;
import 'package:timezone/data/latest.dart' as tz_data;
import '../models/models.dart';

class NotificationService {
  static final _plugin = FlutterLocalNotificationsPlugin();
  static bool _initialized = false;

  static Future<void> init() async {
    if (_initialized) return;
    tz_data.initializeTimeZones();

    const ios = DarwinInitializationSettings(
      requestAlertPermission: true,
      requestBadgePermission: true,
      requestSoundPermission: true,
    );
    const settings = InitializationSettings(iOS: ios);
    await _plugin.initialize(settings);
    _initialized = true;
  }

  static Future<void> requestPermission() async {
    await _plugin
        .resolvePlatformSpecificImplementation<IOSFlutterLocalNotificationsPlugin>()
        ?.requestPermissions(alert: true, badge: true, sound: true);
  }

  static Future<void> scheduleBillNotifications(List<ApiBill> bills) async {
    await _plugin.cancelAll();

    final now = DateTime.now();
    var notifId = 100; // start from 100 to avoid conflicts

    for (final bill in bills) {
      if (bill.isPaidThisMonth) continue;

      final daysInMonth = _daysInMonth(now.year, now.month);
      final dueDay = bill.isEndOfMonth ? daysInMonth : bill.dayOfMonth.clamp(1, daysInMonth);
      final dueDate = DateTime(now.year, now.month, dueDay, 9, 0);

      // Notification day before
      final dayBefore = dueDate.subtract(const Duration(days: 1));
      if (dayBefore.isAfter(now)) {
        await _schedule(
          notifId++,
          'Bill due tomorrow',
          '${bill.name}${bill.amount != null ? " — \$${bill.amount!.toStringAsFixed(2)}" : ""}',
          dayBefore,
        );
      }

      // Notification on due date
      if (dueDate.isAfter(now)) {
        await _schedule(
          notifId++,
          bill.daysUntilDue == 0 ? '${bill.name} is due today' : 'Upcoming bill',
          '${bill.name}${bill.amount != null ? " — \$${bill.amount!.toStringAsFixed(2)}" : " (variable amount)"}',
          dueDate,
        );
      } else if (!bill.isPaidThisMonth && bill.daysUntilDue < 0) {
        // Already overdue — fire immediately (or at next 9am if before today's 9am)
        final alertTime = now.hour < 9
            ? DateTime(now.year, now.month, now.day, 9, 0)
            : now.add(const Duration(seconds: 5));
        await _schedule(
          notifId++,
          '⚠️ Overdue bill',
          '${bill.name} was due ${(-bill.daysUntilDue)} day${bill.daysUntilDue < -1 ? "s" : ""} ago',
          alertTime,
        );
      }
    }
  }

  static Future<void> _schedule(int id, String title, String body, DateTime when) async {
    final tzWhen = tz.TZDateTime.from(when, tz.local);
    await _plugin.zonedSchedule(
      id,
      title,
      body,
      tzWhen,
      const NotificationDetails(
        iOS: DarwinNotificationDetails(
          presentAlert: true,
          presentBadge: true,
          presentSound: true,
        ),
      ),
      androidScheduleMode: AndroidScheduleMode.exactAllowWhileIdle,
      uiLocalNotificationDateInterpretation: UILocalNotificationDateInterpretation.absoluteTime,
      matchDateTimeComponents: null,
    );
  }

  static int _daysInMonth(int year, int month) =>
      DateTime(year, month + 1, 0).day;
}
