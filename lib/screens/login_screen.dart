import 'package:flutter/cupertino.dart';
import 'package:local_auth/local_auth.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:url_launcher/url_launcher.dart';
import '../services/auth_service.dart';
import '../services/biometric_service.dart';
import '../theme/app_theme.dart';

const _prefLastUsername = 'last_username';

const _forgotPasswordUrl = 'https://budget.homeland-one.org/Account/ForgotPassword';

class LoginScreen extends StatefulWidget {
  final AuthService auth;
  final BiometricService biometrics;
  final VoidCallback onLogin;

  const LoginScreen({
    super.key,
    required this.auth,
    required this.biometrics,
    required this.onLogin,
  });

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _userCtrl = TextEditingController();
  final _passCtrl = TextEditingController();
  bool _loading = false;
  bool _obscurePassword = true;
  String? _error;
  bool _biometricAvailable = false;
  bool _biometricEnabled = false;
  List<BiometricType> _biometricTypes = [];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      await _restoreUsername();
      await _checkBiometrics();
    });
  }

  Future<void> _restoreUsername() async {
    final prefs = await SharedPreferences.getInstance();
    final saved = prefs.getString(_prefLastUsername);
    if (saved != null && mounted) {
      _userCtrl.text = saved;
    }
  }

  Future<void> _saveUsername(String username) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_prefLastUsername, username);
  }

  @override
  void dispose() {
    _userCtrl.dispose();
    _passCtrl.dispose();
    super.dispose();
  }

  Future<void> _checkBiometrics() async {
    final available = await widget.biometrics.isAvailable();
    final enabled = await widget.biometrics.isEnabled();
    final types = await widget.biometrics.availableTypes();
    if (mounted) {
      setState(() {
        _biometricAvailable = available;
        _biometricEnabled = enabled;
        _biometricTypes = types;
      });
    }
    if (available && enabled) _loginWithBiometrics();
  }

  Future<void> _loginWithBiometrics() async {
    setState(() { _loading = true; _error = null; });
    try {
      final creds = await widget.biometrics.authenticate();
      if (creds == null) {
        setState(() => _loading = false);
        return;
      }
      final (username, password) = creds;
      await widget.auth.login(username, password);
      widget.onLogin();
    } catch (e) {
      setState(() {
        _error = e.toString().replaceFirst('Exception: ', '');
        _loading = false;
      });
    }
  }

  Future<void> _submit() async {
    final u = _userCtrl.text.trim();
    final p = _passCtrl.text;
    if (u.isEmpty || p.isEmpty) {
      setState(() => _error = 'Enter username and password');
      return;
    }
    setState(() { _loading = true; _error = null; });
    try {
      await widget.auth.login(u, p);
      await _saveUsername(u);
      final alreadyEnabled = await widget.biometrics.isEnabled();
      if (_biometricAvailable && !alreadyEnabled && mounted) {
        await _offerBiometrics(u, p);
      }
      if (mounted) widget.onLogin();
    } catch (e) {
      setState(() {
        _error = e.toString().replaceFirst('Exception: ', '');
        _loading = false;
      });
    }
  }

  Future<void> _offerBiometrics(String username, String password) async {
    final label = _biometricLabel;
    if (!mounted) return;
    await showCupertinoDialog<void>(
      context: context,
      builder: (dlgCtx) => CupertinoAlertDialog(
        title: Text('Enable $label?'),
        content: Text('Sign in faster next time using $label.'),
        actions: [
          CupertinoDialogAction(
            onPressed: () async {
              await widget.biometrics.enable(username, password);
              if (dlgCtx.mounted) Navigator.pop(dlgCtx);
            },
            child: Text('Enable $label'),
          ),
          CupertinoDialogAction(
            isDestructiveAction: true,
            onPressed: () => Navigator.pop(dlgCtx),
            child: const Text('Not Now'),
          ),
        ],
      ),
    );
  }

  String get _biometricLabel {
    if (_biometricTypes.contains(BiometricType.face)) return 'Face ID';
    if (_biometricTypes.contains(BiometricType.fingerprint)) return 'Touch ID';
    return 'Biometrics';
  }

  IconData get _biometricIcon => CupertinoIcons.lock_open_fill;

  @override
  Widget build(BuildContext context) {
    return CupertinoPageScaffold(
      backgroundColor: AppTheme.background,
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 32),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              ClipRRect(
                borderRadius: BorderRadius.circular(20),
                child: Image.asset('images/icon.png', width: 96, height: 96),
              ),
              const SizedBox(height: 16),
              const Text(
                'DollarCount',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 32, fontWeight: FontWeight.w700, color: AppTheme.textPrimary),
              ),
              const SizedBox(height: 8),
              const Text(
                'Family Finance Tracker',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 15, color: AppTheme.textSecondary),
              ),
              const SizedBox(height: 48),
              CupertinoTextField(
                controller: _userCtrl,
                placeholder: 'Username',
                autocorrect: false,
                textInputAction: TextInputAction.next,
                style: const TextStyle(color: AppTheme.textPrimary),
                placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: AppTheme.surface,
                  borderRadius: BorderRadius.circular(12),
                ),
                prefix: const Padding(
                  padding: EdgeInsets.only(left: 12),
                  child: Icon(CupertinoIcons.person, color: AppTheme.textSecondary, size: 20),
                ),
              ),
              const SizedBox(height: 12),
              CupertinoTextField(
                controller: _passCtrl,
                placeholder: 'Password',
                obscureText: _obscurePassword,
                textInputAction: TextInputAction.done,
                onSubmitted: (_) => _submit(),
                style: const TextStyle(color: AppTheme.textPrimary),
                placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: AppTheme.surface,
                  borderRadius: BorderRadius.circular(12),
                ),
                prefix: const Padding(
                  padding: EdgeInsets.only(left: 12),
                  child: Icon(CupertinoIcons.lock, color: AppTheme.textSecondary, size: 20),
                ),
                suffix: CupertinoButton(
                  padding: const EdgeInsets.only(right: 8),
                  onPressed: () => setState(() => _obscurePassword = !_obscurePassword),
                  child: Icon(
                    _obscurePassword ? CupertinoIcons.eye : CupertinoIcons.eye_slash,
                    color: AppTheme.textSecondary,
                    size: 20,
                  ),
                ),
              ),
              const SizedBox(height: 8),
              Align(
                alignment: Alignment.centerRight,
                child: CupertinoButton(
                  padding: EdgeInsets.zero,
                  onPressed: () => launchUrl(Uri.parse(_forgotPasswordUrl),
                      mode: LaunchMode.externalApplication),
                  child: const Text('Forgot Password?',
                      style: TextStyle(color: AppTheme.primary, fontSize: 14)),
                ),
              ),
              if (_error != null) ...[
                const SizedBox(height: 4),
                Text(_error!,
                    style: const TextStyle(color: AppTheme.spend, fontSize: 13),
                    textAlign: TextAlign.center),
              ],
              const SizedBox(height: 16),
              CupertinoButton.filled(
                onPressed: _loading ? null : _submit,
                borderRadius: BorderRadius.circular(12),
                child: _loading
                    ? const CupertinoActivityIndicator(color: CupertinoColors.white)
                    : const Text('Sign In', style: TextStyle(fontWeight: FontWeight.w600)),
              ),
              if (_biometricAvailable && _biometricEnabled) ...[
                const SizedBox(height: 16),
                CupertinoButton(
                  onPressed: _loading ? null : _loginWithBiometrics,
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(_biometricIcon, color: AppTheme.primary),
                      const SizedBox(width: 8),
                      Text('Sign in with $_biometricLabel',
                          style: const TextStyle(color: AppTheme.primary)),
                    ],
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
