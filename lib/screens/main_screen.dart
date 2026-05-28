import 'dart:io';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/cupertino.dart';
import 'package:local_auth/local_auth.dart';
import '../models/models.dart';
import '../services/api_service.dart';
import '../services/auth_service.dart';
import '../services/biometric_service.dart';
import '../theme/app_theme.dart';
import 'dashboard_screen.dart';
import 'expenses_screen.dart';
import 'debts_screen.dart';
import 'assets_screen.dart';
import 'bills_screen.dart';

class MainScreen extends StatelessWidget {
  final ApiService api;
  final AuthService auth;
  final BiometricService biometrics;
  final VoidCallback onLogout;

  const MainScreen({
    super.key,
    required this.api,
    required this.auth,
    required this.biometrics,
    required this.onLogout,
  });

  @override
  Widget build(BuildContext context) {
    return CupertinoTabScaffold(
      backgroundColor: AppTheme.background,
      tabBar: CupertinoTabBar(
        backgroundColor: AppTheme.surface,
        activeColor: AppTheme.primary,
        inactiveColor: AppTheme.textSecondary,
        items: const [
          BottomNavigationBarItem(
            icon: Icon(CupertinoIcons.chart_bar),
            activeIcon: Icon(CupertinoIcons.chart_bar_fill),
            label: 'Dashboard',
          ),
          BottomNavigationBarItem(
            icon: Icon(CupertinoIcons.list_bullet),
            activeIcon: Icon(CupertinoIcons.list_bullet),
            label: 'Expenses',
          ),
          BottomNavigationBarItem(
            icon: Icon(CupertinoIcons.creditcard),
            activeIcon: Icon(CupertinoIcons.creditcard_fill),
            label: 'Debts',
          ),
          BottomNavigationBarItem(
            icon: Icon(CupertinoIcons.building_2_fill),
            activeIcon: Icon(CupertinoIcons.building_2_fill),
            label: 'Assets',
          ),
          BottomNavigationBarItem(
            icon: Icon(CupertinoIcons.bell),
            activeIcon: Icon(CupertinoIcons.bell_fill),
            label: 'Bills',
          ),
          BottomNavigationBarItem(
            icon: Icon(CupertinoIcons.person),
            activeIcon: Icon(CupertinoIcons.person_fill),
            label: 'Account',
          ),
        ],
      ),
      tabBuilder: (context, index) {
        switch (index) {
          case 0:
            return CupertinoTabView(builder: (_) => DashboardScreen(api: api));
          case 1:
            return CupertinoTabView(builder: (_) => ExpensesScreen(api: api));
          case 2:
            return CupertinoTabView(builder: (_) => DebtsScreen(api: api));
          case 3:
            return CupertinoTabView(builder: (_) => AssetsScreen(api: api));
          case 4:
            return CupertinoTabView(builder: (_) => BillsScreen(api: api));
          case 5:
            return CupertinoTabView(
                builder: (_) => _AccountTab(auth: auth, biometrics: biometrics, api: api, onLogout: onLogout));
          default:
            return const CupertinoTabView(builder: _empty);
        }
      },
    );
  }
}

Widget _empty(BuildContext context) => const SizedBox.shrink();

class _AccountTab extends StatefulWidget {
  final AuthService auth;
  final BiometricService biometrics;
  final ApiService api;
  final VoidCallback onLogout;
  const _AccountTab({required this.auth, required this.biometrics, required this.api, required this.onLogout});

  @override
  State<_AccountTab> createState() => _AccountTabState();
}

class _AccountTabState extends State<_AccountTab> {
  bool _biometricAvailable = false;
  bool _biometricEnabled = false;
  String _biometricLabel = 'Face ID';
  bool _importing = false;

  LlmConfig? _llmConfig;

  @override
  void initState() {
    super.initState();
    _loadBiometricState();
    _loadLlmConfig();
  }

  Future<void> _loadLlmConfig() async {
    try {
      final cfg = await widget.api.getLlmConfig();
      if (mounted) setState(() => _llmConfig = cfg);
    } catch (_) {}
  }

  Future<void> _showLlmSettings() async {
    final cfg = _llmConfig ?? LlmConfig(provider: 0, endpoint: 'http://localhost:11434', apiKey: '', model: 'llama3.2', isEnabled: false);
    final endpointCtrl = TextEditingController(text: cfg.endpoint);
    final keyCtrl     = TextEditingController(text: cfg.apiKey);
    final modelCtrl   = TextEditingController(text: cfg.model);
    int provider = cfg.provider;
    bool enabled = cfg.isEnabled;
    String? err;

    await showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setSt) => Container(
          padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom),
          decoration: const BoxDecoration(
            color: AppTheme.surface,
            borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
          ),
          child: SafeArea(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: SingleChildScrollView(
                child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  Row(children: [
                    const Text('AI Advisor Settings',
                        style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: AppTheme.textPrimary)),
                    const Spacer(),
                    CupertinoButton(
                      padding: EdgeInsets.zero,
                      child: const Icon(CupertinoIcons.xmark_circle_fill, color: AppTheme.textSecondary),
                      onPressed: () => Navigator.pop(ctx),
                    ),
                  ]),
                  const SizedBox(height: 4),
                  const Text('Connect a local Ollama instance or cloud LLM to get spending insights.',
                      style: TextStyle(fontSize: 13, color: AppTheme.textSecondary)),
                  const SizedBox(height: 16),
                  // Provider picker
                  GestureDetector(
                    onTap: () {
                      int temp = provider;
                      showCupertinoModalPopup<void>(
                        context: ctx,
                        builder: (_) => Container(
                          height: 220,
                          color: AppTheme.surface,
                          child: CupertinoPicker(
                            scrollController: FixedExtentScrollController(initialItem: provider),
                            itemExtent: 36,
                            onSelectedItemChanged: (i) => temp = i,
                            children: const [
                              Center(child: Text('Ollama (local)', style: TextStyle(color: AppTheme.textPrimary))),
                              Center(child: Text('OpenAI', style: TextStyle(color: AppTheme.textPrimary))),
                              Center(child: Text('Gemini', style: TextStyle(color: AppTheme.textPrimary))),
                            ],
                          ),
                        ),
                      ).then((_) => setSt(() => provider = temp));
                    },
                    child: Container(
                      padding: const EdgeInsets.all(14),
                      decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                      child: Row(children: [
                        Expanded(child: Text(
                          ['Ollama (local)', 'OpenAI', 'Gemini'][provider],
                          style: const TextStyle(color: AppTheme.textPrimary),
                        )),
                        const Icon(CupertinoIcons.chevron_down, color: AppTheme.textSecondary, size: 16),
                      ]),
                    ),
                  ),
                  const SizedBox(height: 10),
                  if (provider == 0) ...[
                    CupertinoTextField(
                      controller: endpointCtrl,
                      placeholder: 'Ollama URL (e.g. http://localhost:11434)',
                      autocorrect: false,
                      style: const TextStyle(color: AppTheme.textPrimary),
                      placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                      padding: const EdgeInsets.all(14),
                      decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                    ),
                    const SizedBox(height: 10),
                  ],
                  if (provider == 1 || provider == 2) ...[
                    CupertinoTextField(
                      controller: keyCtrl,
                      placeholder: provider == 1 ? 'OpenAI API key' : 'Gemini API key',
                      obscureText: true,
                      autocorrect: false,
                      style: const TextStyle(color: AppTheme.textPrimary),
                      placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                      padding: const EdgeInsets.all(14),
                      decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                    ),
                    const SizedBox(height: 10),
                  ],
                  CupertinoTextField(
                    controller: modelCtrl,
                    placeholder: provider == 0 ? 'Model (e.g. llama3.2)' : provider == 1 ? 'Model (e.g. gpt-4o)' : 'Model (e.g. gemini-1.5-flash)',
                    autocorrect: false,
                    style: const TextStyle(color: AppTheme.textPrimary),
                    placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                  ),
                  const SizedBox(height: 10),
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                    decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                    child: Row(children: [
                      const Expanded(child: Text('Enable AI advisor', style: TextStyle(color: AppTheme.textPrimary, fontSize: 15))),
                      CupertinoSwitch(
                        value: enabled,
                        activeTrackColor: AppTheme.primary,
                        onChanged: (v) => setSt(() => enabled = v),
                      ),
                    ]),
                  ),
                  if (err != null) Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(err!, style: const TextStyle(color: AppTheme.spend, fontSize: 13)),
                  ),
                  const SizedBox(height: 16),
                  CupertinoButton.filled(
                    borderRadius: BorderRadius.circular(12),
                    onPressed: () async {
                      if (modelCtrl.text.trim().isEmpty) { setSt(() => err = 'Enter a model name'); return; }
                      try {
                        final updated = await widget.api.saveLlmConfig(LlmConfig(
                          provider: provider,
                          endpoint: endpointCtrl.text.trim(),
                          apiKey:   keyCtrl.text.trim(),
                          model:    modelCtrl.text.trim(),
                          isEnabled: enabled,
                        ));
                        if (ctx.mounted) Navigator.pop(ctx);
                        setState(() => _llmConfig = updated);
                      } catch (e) {
                        setSt(() => err = 'Save failed');
                      }
                    },
                    child: const Text('Save Settings', style: TextStyle(fontWeight: FontWeight.w600)),
                  ),
                ]),
              ),
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _importTxt() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: ['txt'],
      allowMultiple: false,
    );
    if (result == null || result.files.isEmpty) return;
    final path = result.files.first.path;
    if (path == null) return;

    setState(() => _importing = true);
    try {
      final res = await widget.api.importTxt(File(path));
      final inserted = res['inserted'] as int? ?? 0;
      final skipped  = res['skipped']  as int? ?? 0;
      if (!mounted) return;
      await showCupertinoDialog<void>(
        context: context,
        builder: (ctx) => CupertinoAlertDialog(
          title: const Text('Import Complete'),
          content: Text('$inserted transaction(s) added\n$skipped duplicate(s) skipped'),
          actions: [
            CupertinoDialogAction(onPressed: () => Navigator.pop(ctx), child: const Text('OK')),
          ],
        ),
      );
    } catch (e) {
      if (!mounted) return;
      await showCupertinoDialog<void>(
        context: context,
        builder: (ctx) => CupertinoAlertDialog(
          title: const Text('Import Failed'),
          content: Text(e.toString().replaceFirst('Exception: ', '')),
          actions: [
            CupertinoDialogAction(onPressed: () => Navigator.pop(ctx), child: const Text('OK')),
          ],
        ),
      );
    } finally {
      if (mounted) setState(() => _importing = false);
    }
  }

  Future<void> _loadBiometricState() async {
    final available = await widget.biometrics.isAvailable();
    final enabled = await widget.biometrics.isEnabled();
    final types = await widget.biometrics.availableTypes();
    if (mounted) {
      setState(() {
        _biometricAvailable = available;
        _biometricEnabled = enabled;
        _biometricLabel = types.contains(BiometricType.face) ? 'Face ID' : 'Touch ID';
      });
    }
  }

  Future<void> _toggleBiometric(bool value) async {
    if (value) {
      await showCupertinoDialog<void>(
        context: context,
        builder: (dlgCtx) => CupertinoAlertDialog(
          title: Text('Enable $_biometricLabel'),
          content: const Text('Sign in next time using biometrics. You\'ll need to enter your password once to confirm.'),
          actions: [
            CupertinoDialogAction(
              onPressed: () async {
                Navigator.pop(dlgCtx);
                await _enrollBiometric();
              },
              child: const Text('Continue'),
            ),
            CupertinoDialogAction(
              isDestructiveAction: true,
              onPressed: () => Navigator.pop(dlgCtx),
              child: const Text('Cancel'),
            ),
          ],
        ),
      );
    } else {
      await widget.biometrics.disable();
      setState(() => _biometricEnabled = false);
    }
  }

  Future<void> _enrollBiometric() async {
    String username = '';
    String password = '';
    String? err;

    await showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setSt) => Container(
          padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom),
          decoration: const BoxDecoration(
            color: AppTheme.surface,
            borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
          ),
          child: SafeArea(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                Text('Confirm Password for $_biometricLabel',
                    style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w600, color: AppTheme.textPrimary)),
                const SizedBox(height: 16),
                CupertinoTextField(
                  placeholder: 'Username',
                  autocorrect: false,
                  onChanged: (v) => username = v,
                  style: const TextStyle(color: AppTheme.textPrimary),
                  placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(color: AppTheme.surfaceLight, borderRadius: BorderRadius.circular(10)),
                ),
                const SizedBox(height: 10),
                CupertinoTextField(
                  placeholder: 'Password',
                  obscureText: true,
                  onChanged: (v) => password = v,
                  style: const TextStyle(color: AppTheme.textPrimary),
                  placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(color: AppTheme.surfaceLight, borderRadius: BorderRadius.circular(10)),
                ),
                if (err != null) Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(err!, style: const TextStyle(color: AppTheme.spend, fontSize: 13)),
                ),
                const SizedBox(height: 16),
                CupertinoButton.filled(
                  borderRadius: BorderRadius.circular(12),
                  onPressed: () async {
                    try {
                      await widget.auth.login(username.trim(), password);
                      await widget.biometrics.enable(username.trim(), password);
                      if (ctx.mounted) Navigator.pop(ctx);
                      setState(() => _biometricEnabled = true);
                    } catch (e) {
                      setSt(() => err = 'Incorrect credentials');
                    }
                  },
                  child: Text('Enable $_biometricLabel'),
                ),
              ]),
            ),
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => CupertinoPageScaffold(
        backgroundColor: AppTheme.background,
        navigationBar: const CupertinoNavigationBar(
          backgroundColor: AppTheme.surface,
          middle: Text('Account'),
        ),
        child: SafeArea(
          child: ListView(
            padding: const EdgeInsets.all(16),
            children: [
              const SizedBox(height: 16),
              Center(
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(20),
                  child: Image.asset('images/icon.png', width: 80, height: 80),
                ),
              ),
              const SizedBox(height: 8),
              const Center(
                child: Text('DollarCount',
                    style: TextStyle(color: AppTheme.textSecondary, fontSize: 14)),
              ),
              const SizedBox(height: 32),
              if (_biometricAvailable) ...[
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                  decoration: BoxDecoration(
                      color: AppTheme.surface, borderRadius: BorderRadius.circular(12)),
                  child: Row(children: [
                    Icon(
                      _biometricLabel == 'Face ID'
                          ? CupertinoIcons.lock_shield_fill
                          : CupertinoIcons.lock_shield,
                      color: AppTheme.primary,
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(_biometricLabel,
                          style: const TextStyle(color: AppTheme.textPrimary, fontSize: 16)),
                    ),
                    CupertinoSwitch(
                      value: _biometricEnabled,
                      activeTrackColor: AppTheme.primary,
                      onChanged: _toggleBiometric,
                    ),
                  ]),
                ),
                const SizedBox(height: 16),
              ],
              // AI Advisor
              GestureDetector(
                onTap: _showLlmSettings,
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                  decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(12)),
                  child: Row(children: [
                    const Icon(CupertinoIcons.sparkles, color: AppTheme.primary),
                    const SizedBox(width: 12),
                    Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      const Text('AI Advisor', style: TextStyle(color: AppTheme.textPrimary, fontSize: 16)),
                      Text(
                        _llmConfig == null
                            ? 'Not configured'
                            : _llmConfig!.isEnabled
                                ? '${['Ollama', 'OpenAI', 'Gemini'][_llmConfig!.provider]} · ${_llmConfig!.model}'
                                : 'Disabled',
                        style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12),
                      ),
                    ])),
                    const Icon(CupertinoIcons.chevron_right, color: AppTheme.textSecondary, size: 16),
                  ]),
                ),
              ),
              const SizedBox(height: 16),
              // Import Statement
              GestureDetector(
                onTap: _importing ? null : _importTxt,
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                  decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(12)),
                  child: Row(children: [
                    _importing
                        ? const CupertinoActivityIndicator()
                        : const Icon(CupertinoIcons.doc_text, color: AppTheme.primary),
                    const SizedBox(width: 12),
                    const Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text('Import Statement', style: TextStyle(color: AppTheme.textPrimary, fontSize: 16)),
                      Text('Upload a bank statement .txt file', style: TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
                    ])),
                    if (!_importing)
                      const Icon(CupertinoIcons.chevron_right, color: AppTheme.textSecondary, size: 16),
                  ]),
                ),
              ),
              const SizedBox(height: 16),
              CupertinoButton(
                color: AppTheme.spend,
                borderRadius: BorderRadius.circular(12),
                onPressed: () async {
                  final confirm = await showCupertinoDialog<bool>(
                    context: context,
                    builder: (dlgCtx) => CupertinoAlertDialog(
                      title: const Text('Sign Out'),
                      content: const Text('Are you sure you want to sign out?'),
                      actions: [
                        CupertinoDialogAction(
                          isDestructiveAction: true,
                          onPressed: () => Navigator.of(dlgCtx).pop(true),
                          child: const Text('Sign Out'),
                        ),
                        CupertinoDialogAction(
                          onPressed: () => Navigator.of(dlgCtx).pop(false),
                          child: const Text('Cancel'),
                        ),
                      ],
                    ),
                  );
                  if (confirm == true) {
                    await widget.auth.logout();
                    widget.onLogout();
                  }
                },
                child: const Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Icon(CupertinoIcons.square_arrow_left, color: CupertinoColors.white),
                    SizedBox(width: 8),
                    Text('Sign Out',
                        style: TextStyle(color: CupertinoColors.white, fontWeight: FontWeight.w600)),
                  ],
                ),
              ),
            ],
          ),
        ),
      );
}
