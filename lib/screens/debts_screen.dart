import 'dart:math';
import 'package:flutter/cupertino.dart';
import 'package:intl/intl.dart';
import '../models/models.dart';
import '../services/api_service.dart';
import '../theme/app_theme.dart';

final _usd = NumberFormat.currency(locale: 'en_US', symbol: '\$');

const _debtTypes = ['Credit Card', 'Auto Loan', 'Mortgage', 'Personal Loan', 'Other'];
const _debtTypeValues = [0, 1, 2, 3, 4];

class DebtsScreen extends StatefulWidget {
  final ApiService api;
  const DebtsScreen({super.key, required this.api});

  @override
  State<DebtsScreen> createState() => _DebtsScreenState();
}

class _DebtsScreenState extends State<DebtsScreen> {
  List<ApiDebt> _debts = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final debts = await widget.api.getDebts();
      setState(() => _debts = debts);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _showAddSheet() async {
    await _showDebtSheet(null);
  }

  Future<void> _showDebtSheet(ApiDebt? existing) async {
    final nameCtrl = TextEditingController(text: existing?.creditorName ?? '');
    final balCtrl = TextEditingController(text: existing?.balance.toStringAsFixed(2) ?? '');
    final minCtrl = TextEditingController(text: existing?.minPayment.toStringAsFixed(2) ?? '');
    final aprCtrl = TextEditingController(text: existing?.interestRate.toStringAsFixed(2) ?? '');
    final notesCtrl = TextEditingController(text: existing?.notes ?? '');
    int typeIdx = 0;
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
                Row(children: [
                  Text(existing == null ? 'Add Debt' : 'Update Debt',
                      style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w600, color: AppTheme.textPrimary)),
                  const Spacer(),
                  CupertinoButton(
                    padding: EdgeInsets.zero,
                    child: const Icon(CupertinoIcons.xmark_circle_fill, color: AppTheme.textSecondary),
                    onPressed: () => Navigator.pop(ctx),
                  ),
                ]),
                const SizedBox(height: 16),
                _Field(ctrl: nameCtrl, placeholder: 'Creditor name'),
                const SizedBox(height: 10),
                if (existing == null) ...[
                  CupertinoButton(
                    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                    color: AppTheme.surfaceLight,
                    borderRadius: BorderRadius.circular(10),
                    onPressed: () => showCupertinoModalPopup<void>(
                      context: ctx,
                      builder: (_) => Container(
                        height: 220,
                        color: AppTheme.surface,
                        child: CupertinoPicker(
                          itemExtent: 36,
                          onSelectedItemChanged: (i) => typeIdx = i,
                          children: _debtTypes
                              .map((t) => Center(child: Text(t, style: const TextStyle(color: AppTheme.textPrimary))))
                              .toList(),
                        ),
                      ),
                    ),
                    child: Row(children: [
                      Text(_debtTypes[typeIdx], style: const TextStyle(color: AppTheme.textPrimary, fontSize: 15)),
                      const Spacer(),
                      const Icon(CupertinoIcons.chevron_up_chevron_down, size: 16, color: AppTheme.textSecondary),
                    ]),
                  ),
                  const SizedBox(height: 10),
                ],
                Row(children: [
                  Expanded(child: _Field(ctrl: balCtrl, placeholder: 'Balance', numeric: true)),
                  const SizedBox(width: 10),
                  Expanded(child: _Field(ctrl: minCtrl, placeholder: 'Min payment', numeric: true)),
                ]),
                const SizedBox(height: 10),
                _Field(ctrl: aprCtrl, placeholder: 'APR %', numeric: true),
                const SizedBox(height: 10),
                _Field(ctrl: notesCtrl, placeholder: 'Notes (optional)'),
                if (err != null) Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(err!, style: const TextStyle(color: AppTheme.spend, fontSize: 13)),
                ),
                const SizedBox(height: 16),
                CupertinoButton.filled(
                  borderRadius: BorderRadius.circular(12),
                  onPressed: () async {
                    final bal = double.tryParse(balCtrl.text);
                    final min = double.tryParse(minCtrl.text);
                    final apr = double.tryParse(aprCtrl.text);
                    if (bal == null || min == null || apr == null) {
                      setSt(() => err = 'Enter valid numbers');
                      return;
                    }
                    if (nameCtrl.text.trim().isEmpty) {
                      setSt(() => err = 'Enter creditor name');
                      return;
                    }
                    try {
                      if (existing == null) {
                        await widget.api.addDebt(
                          creditorName: nameCtrl.text.trim(),
                          type: _debtTypeValues[typeIdx],
                          balance: bal,
                          minPayment: min,
                          interestRate: apr,
                          notes: notesCtrl.text.trim(),
                        );
                      } else {
                        await widget.api.updateDebt(existing.id,
                          creditorName: nameCtrl.text.trim(),
                          balance: bal, minPayment: min, interestRate: apr,
                          notes: notesCtrl.text.trim());
                      }
                      if (ctx.mounted) Navigator.pop(ctx);
                      await _load();
                    } catch (e) {
                      setSt(() => err = 'Save failed');
                    }
                  },
                  child: Text(existing == null ? 'Add Debt' : 'Update'),
                ),
              ]),
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _delete(ApiDebt d) async {
    final confirm = await showCupertinoDialog<bool>(
      context: context,
      builder: (dlgCtx) => CupertinoAlertDialog(
        title: const Text('Delete Debt'),
        content: Text('Delete "${d.creditorName}"?'),
        actions: [
          CupertinoDialogAction(isDestructiveAction: true, onPressed: () => Navigator.of(dlgCtx).pop(true), child: const Text('Delete')),
          CupertinoDialogAction(onPressed: () => Navigator.of(dlgCtx).pop(false), child: const Text('Cancel')),
        ],
      ),
    );
    if (confirm == true) {
      await widget.api.deleteDebt(d.id);
      await _load();
    }
  }

  @override
  Widget build(BuildContext context) {
    final active = _debts.where((d) => d.isActive).toList();
    final inactive = _debts.where((d) => !d.isActive).toList();
    final totalBalance = active.fold(0.0, (s, d) => s + d.balance);

    return CupertinoPageScaffold(
      backgroundColor: AppTheme.background,
      navigationBar: CupertinoNavigationBar(
        backgroundColor: AppTheme.surface,
        middle: const Text('Debts'),
        trailing: CupertinoButton(
          padding: EdgeInsets.zero,
          onPressed: _showAddSheet,
          child: const Icon(CupertinoIcons.add, color: AppTheme.primary),
        ),
      ),
      child: _loading
          ? const Center(child: CupertinoActivityIndicator())
          : _error != null
              ? Center(child: Text(_error!, style: const TextStyle(color: AppTheme.textSecondary)))
              : CustomScrollView(
                  slivers: [
                    CupertinoSliverRefreshControl(onRefresh: _load),
                    SliverPadding(
                      padding: const EdgeInsets.all(16),
                      sliver: SliverList(
                        delegate: SliverChildListDelegate([
                          Container(
                            padding: const EdgeInsets.all(16),
                            decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(12)),
                            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                              const Text('Total Balance', style: TextStyle(color: AppTheme.textSecondary, fontSize: 13)),
                              const SizedBox(height: 4),
                              Text(_usd.format(totalBalance),
                                  style: const TextStyle(color: AppTheme.spend, fontSize: 28, fontWeight: FontWeight.w700)),
                            ]),
                          ),
                          const SizedBox(height: 16),
                          ...active.map((d) => _DebtCard(debt: d, onEdit: () => _showDebtSheet(d), onDelete: () => _delete(d))),
                          if (inactive.isNotEmpty) ...[
                            const Padding(
                              padding: EdgeInsets.symmetric(vertical: 12),
                              child: Text('Inactive', style: TextStyle(color: AppTheme.textSecondary, fontSize: 13)),
                            ),
                            ...inactive.map((d) => _DebtCard(debt: d, onEdit: () => _showDebtSheet(d), onDelete: () => _delete(d))),
                          ],
                          const SizedBox(height: 40),
                        ]),
                      ),
                    ),
                  ],
                ),
    );
  }
}

String _payoffSummary(ApiDebt debt) {
  if (debt.balance <= 0 || debt.minPayment <= 0) return '';
  final monthlyRate = debt.interestRate / 12 / 100;
  if (monthlyRate <= 0) {
    final months = (debt.balance / debt.minPayment).ceil();
    return '${months}mo to pay off · no interest';
  }
  if (debt.minPayment <= debt.balance * monthlyRate) return 'Min payment too low to pay off';
  final months = (-log(1 - debt.balance * monthlyRate / debt.minPayment) / log(1 + monthlyRate)).ceil();
  final totalPaid = months * debt.minPayment;
  final interest = totalPaid - debt.balance;
  final years = months ~/ 12;
  final rem = months % 12;
  final timeLabel = years > 0
      ? '${years}yr${rem > 0 ? ' ${rem}mo' : ''}'
      : '${months}mo';
  return '$timeLabel to pay off · ${_usd.format(interest)} interest';
}

class _DebtCard extends StatelessWidget {
  final ApiDebt debt;
  final VoidCallback onEdit;
  final VoidCallback onDelete;
  const _DebtCard({required this.debt, required this.onEdit, required this.onDelete});

  @override
  Widget build(BuildContext context) {
    final payoff = _payoffSummary(debt);
    return GestureDetector(
      onTap: onEdit,
      child: Container(
        margin: const EdgeInsets.only(bottom: 10),
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: debt.isActive ? AppTheme.surface : AppTheme.surfaceLight.withValues(alpha: 0.5),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(
              child: Text(debt.creditorName,
                  style: const TextStyle(color: AppTheme.textPrimary, fontSize: 16, fontWeight: FontWeight.w600)),
            ),
            CupertinoButton(
              padding: EdgeInsets.zero,
              onPressed: onDelete,
              child: const Icon(CupertinoIcons.trash, color: AppTheme.spend, size: 18),
            ),
          ]),
          Text(debt.type, style: const TextStyle(color: AppTheme.textSecondary, fontSize: 13)),
          const SizedBox(height: 10),
          Row(children: [
            _Stat('Balance', _usd.format(debt.balance), AppTheme.spend),
            const SizedBox(width: 20),
            _Stat('Min Payment', _usd.format(debt.minPayment), AppTheme.textPrimary),
            const SizedBox(width: 20),
            _Stat('APR', '${debt.interestRate.toStringAsFixed(1)}%', AppTheme.primary),
          ]),
          if (payoff.isNotEmpty) ...[
            const SizedBox(height: 8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
              decoration: BoxDecoration(
                color: AppTheme.background,
                borderRadius: BorderRadius.circular(8),
              ),
              child: Row(children: [
                const Icon(CupertinoIcons.calendar, color: AppTheme.textSecondary, size: 13),
                const SizedBox(width: 6),
                Expanded(child: Text(payoff,
                    style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12))),
              ]),
            ),
          ],
          if (debt.addedByName.isNotEmpty) Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Text('Added by ${debt.addedByName}',
                style: const TextStyle(color: AppTheme.textSecondary, fontSize: 11)),
          ),
        ]),
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  final String label;
  final String value;
  final Color color;
  const _Stat(this.label, this.value, this.color);

  @override
  Widget build(BuildContext context) => Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(label, style: const TextStyle(color: AppTheme.textSecondary, fontSize: 11)),
        Text(value, style: TextStyle(color: color, fontSize: 14, fontWeight: FontWeight.w600)),
      ]);
}

class _Field extends StatelessWidget {
  final TextEditingController ctrl;
  final String placeholder;
  final bool numeric;
  const _Field({required this.ctrl, required this.placeholder, this.numeric = false});

  @override
  Widget build(BuildContext context) => CupertinoTextField(
        controller: ctrl,
        placeholder: placeholder,
        keyboardType: numeric ? const TextInputType.numberWithOptions(decimal: true) : TextInputType.text,
        style: const TextStyle(color: AppTheme.textPrimary),
        placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: AppTheme.surfaceLight, borderRadius: BorderRadius.circular(10)),
      );
}
