import 'package:flutter/cupertino.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart';
import '../models/models.dart';
import '../services/api_service.dart';
import '../services/notification_service.dart';
import '../theme/app_theme.dart';

final _usd = NumberFormat.currency(locale: 'en_US', symbol: '\$');

class BillsScreen extends StatefulWidget {
  final ApiService api;
  const BillsScreen({super.key, required this.api});

  @override
  State<BillsScreen> createState() => _BillsScreenState();
}

class _BillsScreenState extends State<BillsScreen> {
  List<ApiBill> _bills = [];
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
      final bills = await widget.api.getBills();
      setState(() => _bills = bills);
      await NotificationService.scheduleBillNotifications(bills);
    } catch (e) {
      setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _showAddSheet() => _showBillSheet(null);

  Future<void> _showBillSheet(ApiBill? existing) async {
    final nameCtrl = TextEditingController(text: existing?.name ?? '');
    final amtCtrl  = TextEditingController(
        text: existing?.amount != null ? existing!.amount!.toStringAsFixed(2) : '');
    final notesCtrl = TextEditingController(text: existing?.notes ?? '');
    bool variableAmount = existing?.amount == null;
    int dayOfMonth = existing?.dayOfMonth ?? 1;
    bool isEndOfMonth = existing?.isEndOfMonth ?? false;
    int? linkedDebtId = existing?.linkedDebtId;
    String? err;

    // Fetch debts for linking
    List<ApiDebt> debts = [];
    try { debts = await widget.api.getDebts(); } catch (_) {}

    if (!mounted) return;

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
                    Text(existing == null ? 'Add Bill' : 'Edit Bill',
                        style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: AppTheme.textPrimary)),
                    const Spacer(),
                    CupertinoButton(
                      padding: EdgeInsets.zero,
                      child: const Icon(CupertinoIcons.xmark_circle_fill, color: AppTheme.textSecondary),
                      onPressed: () => Navigator.pop(ctx),
                    ),
                  ]),
                  const SizedBox(height: 16),
                  // Name
                  CupertinoTextField(
                    controller: nameCtrl,
                    placeholder: 'Bill name (e.g. CoServ Electricity)',
                    style: const TextStyle(color: AppTheme.textPrimary),
                    placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                  ),
                  const SizedBox(height: 10),
                  // Amount toggle
                  GestureDetector(
                    onTap: () => setSt(() => variableAmount = !variableAmount),
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                      decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                      child: Row(children: [
                        Expanded(child: Text('Variable amount',
                            style: const TextStyle(color: AppTheme.textPrimary, fontSize: 15))),
                        CupertinoSwitch(
                          value: variableAmount,
                          activeTrackColor: AppTheme.primary,
                          onChanged: (v) => setSt(() => variableAmount = v),
                        ),
                      ]),
                    ),
                  ),
                  if (!variableAmount) ...[
                    const SizedBox(height: 10),
                    CupertinoTextField(
                      controller: amtCtrl,
                      placeholder: 'Amount',
                      keyboardType: const TextInputType.numberWithOptions(decimal: true),
                      style: const TextStyle(color: AppTheme.textPrimary),
                      placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                      padding: const EdgeInsets.all(14),
                      prefix: const Padding(padding: EdgeInsets.only(left: 12),
                          child: Text('\$', style: TextStyle(color: AppTheme.textSecondary))),
                      decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                    ),
                  ],
                  const SizedBox(height: 10),
                  // Due day
                  GestureDetector(
                    onTap: () => setSt(() => isEndOfMonth = !isEndOfMonth),
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                      decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                      child: Row(children: [
                        Expanded(child: Text('Due at end of month',
                            style: const TextStyle(color: AppTheme.textPrimary, fontSize: 15))),
                        CupertinoSwitch(
                          value: isEndOfMonth,
                          activeTrackColor: AppTheme.primary,
                          onChanged: (v) => setSt(() => isEndOfMonth = v),
                        ),
                      ]),
                    ),
                  ),
                  if (!isEndOfMonth) ...[
                    const SizedBox(height: 10),
                    GestureDetector(
                      onTap: () {
                        var temp = dayOfMonth;
                        showCupertinoModalPopup<void>(
                          context: ctx,
                          builder: (_) => Container(
                            height: 260,
                            color: AppTheme.surface,
                            child: Column(children: [
                              SizedBox(
                                height: 200,
                                child: CupertinoPicker(
                                  scrollController: FixedExtentScrollController(initialItem: dayOfMonth - 1),
                                  itemExtent: 36,
                                  onSelectedItemChanged: (i) => temp = i + 1,
                                  children: List.generate(28, (i) => Center(
                                    child: Text('${i + 1}${_ordinal(i + 1)} of month',
                                        style: const TextStyle(color: AppTheme.textPrimary)),
                                  )),
                                ),
                              ),
                              CupertinoButton(
                                child: const Text('Done'),
                                onPressed: () { setSt(() => dayOfMonth = temp); Navigator.pop(ctx); },
                              ),
                            ]),
                          ),
                        );
                      },
                      child: Container(
                        padding: const EdgeInsets.all(14),
                        decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                        child: Row(children: [
                          Expanded(child: Text('Due day: $dayOfMonth${_ordinal(dayOfMonth)} of month',
                              style: const TextStyle(color: AppTheme.textPrimary))),
                          const Icon(CupertinoIcons.chevron_down, color: AppTheme.textSecondary, size: 16),
                        ]),
                      ),
                    ),
                  ],
                  // Link to debt
                  if (debts.isNotEmpty) ...[
                    const SizedBox(height: 10),
                    GestureDetector(
                      onTap: () {
                        int? tempId = linkedDebtId;
                        final items = [null, ...debts.map((d) => d.id)];
                        final idx = items.indexOf(linkedDebtId).clamp(0, items.length - 1);
                        showCupertinoModalPopup<void>(
                          context: ctx,
                          builder: (_) => Container(
                            height: 260,
                            color: AppTheme.surface,
                            child: Column(children: [
                              SizedBox(
                                height: 200,
                                child: CupertinoPicker(
                                  scrollController: FixedExtentScrollController(initialItem: idx),
                                  itemExtent: 36,
                                  onSelectedItemChanged: (i) => tempId = items[i],
                                  children: [
                                    const Center(child: Text('None', style: TextStyle(color: AppTheme.textSecondary))),
                                    ...debts.map((d) => Center(
                                      child: Text(d.creditorName, style: const TextStyle(color: AppTheme.textPrimary)),
                                    )),
                                  ],
                                ),
                              ),
                              CupertinoButton(
                                child: const Text('Done'),
                                onPressed: () { setSt(() => linkedDebtId = tempId); Navigator.pop(ctx); },
                              ),
                            ]),
                          ),
                        );
                      },
                      child: Container(
                        padding: const EdgeInsets.all(14),
                        decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                        child: Row(children: [
                          Expanded(child: Text(
                            linkedDebtId != null
                                ? 'Links to: ${debts.firstWhere((d) => d.id == linkedDebtId, orElse: () => debts.first).creditorName}'
                                : 'Link to debt (optional)',
                            style: TextStyle(
                                color: linkedDebtId != null ? AppTheme.textPrimary : AppTheme.textSecondary),
                          )),
                          const Icon(CupertinoIcons.chevron_down, color: AppTheme.textSecondary, size: 16),
                        ]),
                      ),
                    ),
                  ],
                  const SizedBox(height: 10),
                  CupertinoTextField(
                    controller: notesCtrl,
                    placeholder: 'Notes (optional)',
                    style: const TextStyle(color: AppTheme.textPrimary),
                    placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                  ),
                  if (err != null) Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(err!, style: const TextStyle(color: AppTheme.spend, fontSize: 13)),
                  ),
                  const SizedBox(height: 16),
                  CupertinoButton.filled(
                    borderRadius: BorderRadius.circular(12),
                    onPressed: () async {
                      if (nameCtrl.text.trim().isEmpty) {
                        setSt(() => err = 'Enter a bill name');
                        return;
                      }
                      double? amt;
                      if (!variableAmount) {
                        amt = double.tryParse(amtCtrl.text.replaceAll(',', ''));
                        if (amt == null || amt <= 0) { setSt(() => err = 'Enter a valid amount'); return; }
                      }
                      final day = isEndOfMonth ? 31 : dayOfMonth;
                      try {
                        if (existing == null) {
                          await widget.api.addBill(
                            name: nameCtrl.text.trim(), amount: amt, dayOfMonth: day,
                            linkedDebtId: linkedDebtId, notes: notesCtrl.text.trim());
                        } else {
                          await widget.api.updateBill(existing.id,
                            name: nameCtrl.text.trim(), amount: amt, dayOfMonth: day,
                            linkedDebtId: linkedDebtId, notes: notesCtrl.text.trim());
                        }
                        if (ctx.mounted) Navigator.pop(ctx);
                        await _load();
                      } catch (e) {
                        setSt(() => err = 'Save failed: $e');
                      }
                    },
                    child: Text(existing == null ? 'Add Bill' : 'Save Changes',
                        style: const TextStyle(fontWeight: FontWeight.w600)),
                  ),
                ]),
              ),
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _acknowledge(ApiBill bill) async {
    if (bill.isPaidThisMonth) return;

    // If amount is variable, ask for the amount
    double amount = bill.amount ?? 0.0;
    if (bill.amount == null) {
      final ctrl = TextEditingController();
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
                child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                  Text('Acknowledge: ${bill.name}',
                      style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: AppTheme.textPrimary)),
                  const SizedBox(height: 8),
                  if (bill.linkedDebtName != null)
                    Text('Will deduct from: ${bill.linkedDebtName}',
                        style: const TextStyle(color: AppTheme.textSecondary, fontSize: 13)),
                  const SizedBox(height: 16),
                  CupertinoTextField(
                    controller: ctrl,
                    placeholder: 'Amount paid',
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    autofocus: true,
                    style: const TextStyle(color: AppTheme.textPrimary),
                    placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                    padding: const EdgeInsets.all(14),
                    prefix: const Padding(padding: EdgeInsets.only(left: 12),
                        child: Text('\$', style: TextStyle(color: AppTheme.textSecondary))),
                    decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                  ),
                  if (err != null) Padding(
                    padding: const EdgeInsets.only(top: 6),
                    child: Text(err!, style: const TextStyle(color: AppTheme.spend, fontSize: 13)),
                  ),
                  const SizedBox(height: 16),
                  CupertinoButton.filled(
                    borderRadius: BorderRadius.circular(12),
                    onPressed: () {
                      final v = double.tryParse(ctrl.text.replaceAll(',', ''));
                      if (v == null || v <= 0) { setSt(() => err = 'Enter a valid amount'); return; }
                      amount = v;
                      Navigator.pop(ctx);
                    },
                    child: const Text('Confirm Paid', style: TextStyle(fontWeight: FontWeight.w600)),
                  ),
                ]),
              ),
            ),
          ),
        ),
      );
      if (amount <= 0) return;
    } else {
      // Confirm for fixed-amount bills
      final confirm = await showCupertinoDialog<bool>(
        context: context,
        builder: (ctx) => CupertinoAlertDialog(
          title: const Text('Mark as Paid'),
          content: Text(
            '${bill.name} — ${_usd.format(amount)}'
            '${bill.linkedDebtName != null ? '\n\nThis will deduct ${_usd.format(amount)} from ${bill.linkedDebtName}.' : ''}',
          ),
          actions: [
            CupertinoDialogAction(
              onPressed: () => Navigator.pop(ctx, true),
              child: const Text('Mark Paid'),
            ),
            CupertinoDialogAction(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('Cancel'),
            ),
          ],
        ),
      );
      if (confirm != true) return;
    }

    try {
      await widget.api.acknowledgeBill(bill.id, amount: amount);
      HapticFeedback.mediumImpact();
      await _load();
    } catch (e) {
      if (!mounted) return;
      showCupertinoDialog<void>(
        context: context,
        builder: (d) => CupertinoAlertDialog(
          title: const Text('Error'),
          content: Text(e.toString()),
          actions: [CupertinoDialogAction(onPressed: () => Navigator.pop(d), child: const Text('OK'))],
        ),
      );
    }
  }

  Future<void> _delete(ApiBill bill) async {
    final confirm = await showCupertinoDialog<bool>(
      context: context,
      builder: (ctx) => CupertinoAlertDialog(
        title: const Text('Remove Bill'),
        content: Text('Remove "${bill.name}" from your bill tracker?'),
        actions: [
          CupertinoDialogAction(
            isDestructiveAction: true,
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Remove'),
          ),
          CupertinoDialogAction(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Cancel'),
          ),
        ],
      ),
    );
    if (confirm == true) {
      await widget.api.deleteBill(bill.id);
      await _load();
    }
  }

  @override
  Widget build(BuildContext context) {
    final overdue  = _bills.where((b) => !b.isPaidThisMonth && b.daysUntilDue < 0).toList();
    final dueSoon  = _bills.where((b) => !b.isPaidThisMonth && b.daysUntilDue >= 0 && b.daysUntilDue <= 3).toList();
    final upcoming = _bills.where((b) => !b.isPaidThisMonth && b.daysUntilDue > 3).toList();
    final paid     = _bills.where((b) => b.isPaidThisMonth).toList();
    final overdueCount = overdue.length;

    final unpaid = [...overdue, ...dueSoon, ...upcoming];
    final unpaidKnown = unpaid.where((b) => b.amount != null).toList();
    final unpaidTotal = unpaidKnown.fold(0.0, (s, b) => s + b.amount!);
    final hasVariable = unpaid.any((b) => b.amount == null);

    return CupertinoPageScaffold(
      backgroundColor: AppTheme.background,
      navigationBar: CupertinoNavigationBar(
        backgroundColor: AppTheme.surface,
        middle: Row(mainAxisSize: MainAxisSize.min, children: [
          const Text('Bills'),
          if (overdueCount > 0) ...[
            const SizedBox(width: 6),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
              decoration: BoxDecoration(color: AppTheme.spend, borderRadius: BorderRadius.circular(8)),
              child: Text('$overdueCount', style: const TextStyle(color: CupertinoColors.white, fontSize: 11, fontWeight: FontWeight.w700)),
            ),
          ],
        ]),
        trailing: CupertinoButton(
          padding: EdgeInsets.zero,
          onPressed: _showAddSheet,
          child: const Icon(CupertinoIcons.add, color: AppTheme.primary),
        ),
      ),
      child: _loading
          ? const Center(child: CupertinoActivityIndicator())
          : _error != null
              ? _ErrorView(error: _error!, onRetry: _load)
              : _bills.isEmpty
                  ? _EmptyView(onAdd: _showAddSheet)
                  : CustomScrollView(
                      slivers: [
                        CupertinoSliverRefreshControl(onRefresh: _load),
                        SliverToBoxAdapter(
                          child: Padding(
                            padding: const EdgeInsets.fromLTRB(16, 16, 16, 12),
                            child: Container(
                              padding: const EdgeInsets.all(16),
                              decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(12)),
                              child: Row(children: [
                                Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                                  const Text('Unpaid This Month',
                                      style: TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
                                  const SizedBox(height: 2),
                                  Text(
                                    unpaid.isEmpty
                                        ? 'All paid!'
                                        : '${_usd.format(unpaidTotal)}${hasVariable ? '+' : ''}',
                                    style: TextStyle(
                                      color: unpaid.isEmpty ? AppTheme.income : AppTheme.spend,
                                      fontSize: 22, fontWeight: FontWeight.w700,
                                    ),
                                  ),
                                ])),
                                Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
                                  Text('${unpaid.length} remaining',
                                      style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
                                  Text('${paid.length} paid',
                                      style: const TextStyle(color: AppTheme.income, fontSize: 12)),
                                ]),
                              ]),
                            ),
                          ),
                        ),
                        SliverPadding(
                          padding: const EdgeInsets.fromLTRB(16, 0, 16, 40),
                          sliver: SliverList(
                            delegate: SliverChildListDelegate([
                              if (overdue.isNotEmpty) ...[
                                _SectionHeader('Overdue', color: AppTheme.spend),
                                ...overdue.map((b) => _BillCard(
                                    bill: b, onTap: () => _acknowledge(b),
                                    onEdit: () => _showBillSheet(b), onDelete: () => _delete(b))),
                                const SizedBox(height: 12),
                              ],
                              if (dueSoon.isNotEmpty) ...[
                                _SectionHeader('Due Soon', color: const Color(0xFFF97316)),
                                ...dueSoon.map((b) => _BillCard(
                                    bill: b, onTap: () => _acknowledge(b),
                                    onEdit: () => _showBillSheet(b), onDelete: () => _delete(b))),
                                const SizedBox(height: 12),
                              ],
                              if (upcoming.isNotEmpty) ...[
                                _SectionHeader('Upcoming'),
                                ...upcoming.map((b) => _BillCard(
                                    bill: b, onTap: () => _acknowledge(b),
                                    onEdit: () => _showBillSheet(b), onDelete: () => _delete(b))),
                                const SizedBox(height: 12),
                              ],
                              if (paid.isNotEmpty) ...[
                                _SectionHeader('Paid This Month', color: AppTheme.income),
                                ...paid.map((b) => _BillCard(
                                    bill: b, onTap: () {},
                                    onEdit: () => _showBillSheet(b), onDelete: () => _delete(b))),
                              ],
                            ]),
                          ),
                        ),
                      ],
                    ),
    );
  }
}

String _ordinal(int n) {
  if (n >= 11 && n <= 13) return 'th';
  return switch (n % 10) { 1 => 'st', 2 => 'nd', 3 => 'rd', _ => 'th' };
}

class _SectionHeader extends StatelessWidget {
  final String title;
  final Color color;
  const _SectionHeader(this.title, {this.color = AppTheme.textSecondary});

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 8),
    child: Text(title.toUpperCase(),
        style: TextStyle(color: color, fontSize: 11, fontWeight: FontWeight.w600, letterSpacing: 0.8)),
  );
}

class _BillCard extends StatelessWidget {
  final ApiBill bill;
  final VoidCallback onTap;
  final VoidCallback onEdit;
  final VoidCallback onDelete;
  const _BillCard({required this.bill, required this.onTap, required this.onEdit, required this.onDelete});

  @override
  Widget build(BuildContext context) {
    final statusColor = bill.isPaidThisMonth
        ? AppTheme.income
        : bill.daysUntilDue < 0
            ? AppTheme.spend
            : bill.daysUntilDue <= 3
                ? const Color(0xFFF97316)
                : AppTheme.primary;

    final dueLabel = bill.isPaidThisMonth
        ? 'Paid'
        : bill.isEndOfMonth
            ? 'Due month-end'
            : bill.daysUntilDue == 0
                ? 'Due today'
                : bill.daysUntilDue < 0
                    ? '${(-bill.daysUntilDue)}d overdue'
                    : 'Due in ${bill.daysUntilDue}d';

    return Dismissible(
      key: Key('bill-${bill.id}'),
      direction: DismissDirection.endToStart,
      background: Container(
        alignment: Alignment.centerRight,
        padding: const EdgeInsets.only(right: 16),
        decoration: BoxDecoration(
          color: AppTheme.spend,
          borderRadius: BorderRadius.circular(12),
        ),
        child: const Icon(CupertinoIcons.trash, color: CupertinoColors.white),
      ),
      confirmDismiss: (_) async { onDelete(); return false; },
      child: GestureDetector(
        onTap: onTap,
        onLongPress: onEdit,
        child: Container(
          margin: const EdgeInsets.only(bottom: 10),
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: AppTheme.surface,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: bill.isPaidThisMonth
                  ? AppTheme.income.withValues(alpha: 0.3)
                  : bill.daysUntilDue < 0
                      ? AppTheme.spend.withValues(alpha: 0.4)
                      : const Color(0x00000000),
              width: 1.0,
            ),
          ),
          child: Row(children: [
            // Status indicator
            Container(
              width: 40, height: 40,
              decoration: BoxDecoration(
                color: statusColor.withValues(alpha: 0.12),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Icon(
                bill.isPaidThisMonth
                    ? CupertinoIcons.checkmark_circle_fill
                    : bill.daysUntilDue < 0
                        ? CupertinoIcons.exclamationmark_circle_fill
                        : CupertinoIcons.clock,
                color: statusColor, size: 22,
              ),
            ),
            const SizedBox(width: 12),
            Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(bill.name,
                  style: const TextStyle(color: AppTheme.textPrimary, fontSize: 15, fontWeight: FontWeight.w600),
                  maxLines: 1, overflow: TextOverflow.ellipsis),
              Row(children: [
                Text(dueLabel, style: TextStyle(color: statusColor, fontSize: 12, fontWeight: FontWeight.w500)),
                if (bill.linkedDebtName != null) ...[
                  const Text(' · ', style: TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
                  Flexible(child: Text(bill.linkedDebtName!,
                      style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12),
                      maxLines: 1, overflow: TextOverflow.ellipsis)),
                ],
              ]),
            ])),
            const SizedBox(width: 8),
            Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
              Text(
                bill.isPaidThisMonth && bill.paymentThisMonth != null
                    ? _usd.format(bill.paymentThisMonth!.amount)
                    : bill.amount != null
                        ? _usd.format(bill.amount!)
                        : 'Variable',
                style: TextStyle(
                    color: bill.isPaidThisMonth ? AppTheme.income : AppTheme.textPrimary,
                    fontSize: 15, fontWeight: FontWeight.w700),
              ),
              if (!bill.isPaidThisMonth)
                Text('Tap to pay', style: TextStyle(color: statusColor, fontSize: 10)),
            ]),
          ]),
        ),
      ),
    );
  }
}

class _EmptyView extends StatelessWidget {
  final VoidCallback onAdd;
  const _EmptyView({required this.onAdd});

  @override
  Widget build(BuildContext context) => Center(
    child: Column(mainAxisSize: MainAxisSize.min, children: [
      const Icon(CupertinoIcons.bell_slash, color: AppTheme.textSecondary, size: 56),
      const SizedBox(height: 12),
      const Text('No bills tracked', style: TextStyle(color: AppTheme.textSecondary, fontSize: 17)),
      const SizedBox(height: 4),
      const Text('Add your recurring payments to monitor them',
          style: TextStyle(color: AppTheme.textSecondary, fontSize: 13), textAlign: TextAlign.center),
      const SizedBox(height: 24),
      CupertinoButton.filled(onPressed: onAdd, child: const Text('Add First Bill')),
    ]),
  );
}

class _ErrorView extends StatelessWidget {
  final String error;
  final VoidCallback onRetry;
  const _ErrorView({required this.error, required this.onRetry});

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(32),
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        const Icon(CupertinoIcons.exclamationmark_circle, color: AppTheme.spend, size: 48),
        const SizedBox(height: 12),
        Text(error, textAlign: TextAlign.center, style: const TextStyle(color: AppTheme.textSecondary)),
        const SizedBox(height: 16),
        CupertinoButton.filled(onPressed: onRetry, child: const Text('Retry')),
      ]),
    ),
  );
}
