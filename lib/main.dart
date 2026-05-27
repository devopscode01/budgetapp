import 'package:flutter/cupertino.dart';
import 'services/auth_service.dart';
import 'services/api_service.dart';
import 'services/biometric_service.dart';
import 'services/notification_service.dart';
import 'screens/login_screen.dart';
import 'screens/main_screen.dart';
import 'theme/app_theme.dart';

// Update this to your server's public URL
const _baseUrl = 'https://budget.homeland-one.org';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await NotificationService.init();
  runApp(const BudgetApp());
}

class BudgetApp extends StatefulWidget {
  const BudgetApp({super.key});

  @override
  State<BudgetApp> createState() => _BudgetAppState();
}

class _BudgetAppState extends State<BudgetApp> {
  late final AuthService _auth;
  late final ApiService _api;
  late final BiometricService _biometrics;
  bool _loggedIn = false;
  bool _checking = true;

  @override
  void initState() {
    super.initState();
    _auth = AuthService(_baseUrl);
    _api = ApiService(_baseUrl, _auth);
    _biometrics = BiometricService();
    _checkAuth();
  }

  Future<void> _checkAuth() async {
    final ok = await _auth.isLoggedIn();
    if (ok) await NotificationService.requestPermission();
    if (mounted) setState(() { _loggedIn = ok; _checking = false; });
  }

  @override
  Widget build(BuildContext context) {
    return CupertinoApp(
      title: 'Budget',
      theme: AppTheme.theme,
      home: _checking
          ? const _Splash()
          : _loggedIn
              ? MainScreen(
                  api: _api,
                  auth: _auth,
                  biometrics: _biometrics,
                  onLogout: () => setState(() => _loggedIn = false),
                )
              : LoginScreen(
                  auth: _auth,
                  biometrics: _biometrics,
                  onLogin: () => setState(() => _loggedIn = true),
                ),
    );
  }
}

class _Splash extends StatelessWidget {
  const _Splash();

  @override
  Widget build(BuildContext context) => CupertinoPageScaffold(
        backgroundColor: AppTheme.background,
        child: Center(
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            ClipRRect(
              borderRadius: BorderRadius.circular(24),
              child: Image.asset('images/icon.png', width: 96, height: 96),
            ),
            const SizedBox(height: 18),
            const Text(
              'DollarCount',
              style: TextStyle(
                color: AppTheme.textPrimary,
                fontSize: 28,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 4),
            const Text(
              'Your personal budget tracker',
              style: TextStyle(color: AppTheme.textSecondary, fontSize: 14),
            ),
            const SizedBox(height: 36),
            const CupertinoActivityIndicator(),
          ]),
        ),
      );
}
