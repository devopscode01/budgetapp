import 'dart:io';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/cupertino.dart';
import 'package:intl/intl.dart' as intl_lib;
import '../models/models.dart';
import '../services/api_service.dart';
import '../theme/app_theme.dart';
import 'ocr_import_sheet.dart';

final _usd = intl_lib.NumberFormat.currency(locale: 'en_US', symbol: '\$');


class ExpensesScreen extends StatefulWidget {
  final ApiService api;
  const ExpensesScreen({super.key, required this.api});

  @override
  State<ExpensesScreen> createState() => _ExpensesScreenState();
}

class _ExpensesScreenState extends State<ExpensesScreen> {
  List<String> _months = [];
  MonthSummary? _summary;
  String? _selectedYm;
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
      final months = await widget.api.getMonths();
      final ym = _selectedYm ?? (months.isNotEmpty ? months.first : null);
      final summary = ym != null ? await widget.api.getSummary(ym) : null;
      setState(() {
        _months = months;
        _selectedYm = ym;
        _summary = summary;
      });
    } catch (e) {
      setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  String _ymLabel(String ym) {
    try {
      final p = ym.split('-');
      return intl_lib.DateFormat('MMMM yyyy').format(DateTime(int.parse(p[0]), int.parse(p[1])));
    } catch (_) { return ym; }
  }

  Future<void> _selectMonth() async {
    if (_months.isEmpty) return;
    var idx = _months.indexOf(_selectedYm ?? '');
    if (idx < 0) idx = 0;
    await showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => Container(
        height: 260,
        color: AppTheme.surface,
        child: Column(children: [
          SizedBox(
            height: 200,
            child: CupertinoPicker(
              scrollController: FixedExtentScrollController(initialItem: idx),
              itemExtent: 36,
              onSelectedItemChanged: (i) => _selectedYm = _months[i],
              children: _months.map((m) => Center(
                child: Text(_ymLabel(m), style: const TextStyle(color: AppTheme.textPrimary)),
              )).toList(),
            ),
          ),
          CupertinoButton(
            child: const Text('Done'),
            onPressed: () { Navigator.pop(ctx); _load(); },
          ),
        ]),
      ),
    );
  }

  Future<void> _deleteTransaction(ApiTransaction tx) async {
    final confirm = await showCupertinoDialog<bool>(
      context: context,
      builder: (dlgCtx) => CupertinoAlertDialog(
        title: const Text('Delete Transaction'),
        content: Text('Delete "${tx.description}"?'),
        actions: [
          CupertinoDialogAction(
            isDestructiveAction: true,
            onPressed: () => Navigator.of(dlgCtx).pop(true),
            child: const Text('Delete'),
          ),
          CupertinoDialogAction(
            onPressed: () => Navigator.of(dlgCtx).pop(false),
            child: const Text('Cancel'),
          ),
        ],
      ),
    );
    if (confirm == true) {
      try {
        await widget.api.deleteTransaction(tx.id);
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
  }

  void _showTxActions(ApiTransaction tx) {
    showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => CupertinoActionSheet(
        title: Text(tx.description, maxLines: 2, overflow: TextOverflow.ellipsis),
        message: Text('${tx.category} · ${tx.date} · ${_usd.format(tx.amount)}'),
        actions: [
          CupertinoActionSheetAction(
            onPressed: () { Navigator.pop(ctx); _showEditSheet(tx); },
            child: const Text('Edit / Rename'),
          ),
          CupertinoActionSheetAction(
            isDestructiveAction: true,
            onPressed: () { Navigator.pop(ctx); _deleteTransaction(tx); },
            child: const Text('Delete'),
          ),
        ],
        cancelButton: CupertinoActionSheetAction(
          onPressed: () => Navigator.pop(ctx),
          child: const Text('Cancel'),
        ),
      ),
    );
  }

  void _showEditSheet(ApiTransaction tx) {
    showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => _EditTxSheet(api: widget.api, tx: tx, onSaved: _load),
    );
  }

  void _showAddExpense() {
    showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => _AddExpenseSheet(api: widget.api, ym: _selectedYm, onSaved: _load),
    );
  }

  void _showImport() {
    showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => CupertinoActionSheet(
        title: const Text('Import Transactions'),
        actions: [
          CupertinoActionSheetAction(
            onPressed: () {
              Navigator.pop(ctx);
              showCupertinoModalPopup<void>(
                context: context,
                builder: (_) => _ImportSheet(api: widget.api, ym: _selectedYm, onDone: _load),
              );
            },
            child: const Text('Upload PDF Statement'),
          ),
          CupertinoActionSheetAction(
            onPressed: () { Navigator.pop(ctx); _importTxt(); },
            child: const Text('Import .txt Statement'),
          ),
          CupertinoActionSheetAction(
            onPressed: () {
              Navigator.pop(ctx);
              Navigator.push(
                context,
                CupertinoPageRoute(builder: (_) => OcrImportSheet(api: widget.api)),
              ).then((_) => _load());
            },
            child: const Text('Scan Statement (Camera / Photo)'),
          ),
        ],
        cancelButton: CupertinoActionSheetAction(
          onPressed: () => Navigator.pop(ctx),
          child: const Text('Cancel'),
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
    if (result == null || result.files.isEmpty || result.files.first.path == null) return;
    try {
      final res = await widget.api.importTxt(File(result.files.first.path!));
      final inserted = res['inserted'] as int? ?? 0;
      final skipped  = res['skipped']  as int? ?? 0;
      if (!mounted) return;
      await showCupertinoDialog<void>(
        context: context,
        builder: (d) => CupertinoAlertDialog(
          title: const Text('Import Complete'),
          content: Text('$inserted transaction(s) added\n$skipped duplicate(s) skipped'),
          actions: [CupertinoDialogAction(onPressed: () => Navigator.pop(d), child: const Text('OK'))],
        ),
      );
      if (inserted > 0) _load();
    } catch (e) {
      if (!mounted) return;
      showCupertinoDialog<void>(
        context: context,
        builder: (d) => CupertinoAlertDialog(
          title: const Text('Import Failed'),
          content: Text(e.toString().replaceFirst('Exception: ', '')),
          actions: [CupertinoDialogAction(onPressed: () => Navigator.pop(d), child: const Text('OK'))],
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return CupertinoPageScaffold(
      backgroundColor: AppTheme.background,
      navigationBar: CupertinoNavigationBar(
        backgroundColor: AppTheme.surface,
        middle: CupertinoButton(
          padding: EdgeInsets.zero,
          onPressed: _selectMonth,
          child: Row(mainAxisSize: MainAxisSize.min, children: [
            Text(
              _selectedYm != null ? _ymLabel(_selectedYm!) : 'Expenses',
              style: const TextStyle(color: AppTheme.textPrimary, fontWeight: FontWeight.w600, fontSize: 17),
            ),
            const SizedBox(width: 4),
            const Icon(CupertinoIcons.chevron_down, color: AppTheme.textSecondary, size: 14),
          ]),
        ),
        leading: CupertinoButton(
          padding: EdgeInsets.zero,
          onPressed: _showImport,
          child: const Icon(CupertinoIcons.arrow_up_doc, color: AppTheme.primary),
        ),
        trailing: CupertinoButton(
          padding: EdgeInsets.zero,
          onPressed: _showAddExpense,
          child: const Icon(CupertinoIcons.add_circled, color: AppTheme.primary),
        ),
      ),
      child: _loading
          ? const Center(child: CupertinoActivityIndicator())
          : _error != null
              ? _ErrorView(error: _error!, onRetry: _load)
              : _summary == null
                  ? const Center(child: Text('No data', style: TextStyle(color: AppTheme.textSecondary)))
                  : _buildBody(),
    );
  }

  Widget _buildBody() {
    final txs = _summary!.transactions;
    return CustomScrollView(
      slivers: [
        CupertinoSliverRefreshControl(onRefresh: _load),
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
            child: Row(children: [
              Expanded(child: _StatChip('Income', _summary!.totalIncome, AppTheme.income)),
              const SizedBox(width: 10),
              Expanded(child: _StatChip('Spending', _summary!.totalSpend, AppTheme.spend)),
            ]),
          ),
        ),
        if (txs.isEmpty)
          const SliverFillRemaining(
            child: Center(child: Text('No transactions this month',
                style: TextStyle(color: AppTheme.textSecondary))),
          )
        else ...[
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
              child: Text('${txs.length} transactions',
                  style: const TextStyle(fontSize: 13, color: AppTheme.textSecondary)),
            ),
          ),
          SliverList(
            delegate: SliverChildBuilderDelegate(
              (_, i) {
                final tx = txs[i];
                return _TxTile(tx: tx, onTap: () => _showTxActions(tx), onDelete: () => _deleteTransaction(tx));
              },
              childCount: txs.length,
            ),
          ),
        ],
        const SliverToBoxAdapter(child: SizedBox(height: 40)),
      ],
    );
  }
}

class _StatChip extends StatelessWidget {
  final String label;
  final double amount;
  final Color color;
  const _StatChip(this.label, this.amount, this.color);

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
    decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(12)),
    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text(label, style: const TextStyle(fontSize: 11, color: AppTheme.textSecondary)),
      const SizedBox(height: 2),
      Text(_usd.format(amount),
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, color: color)),
    ]),
  );
}

class _TxTile extends StatelessWidget {
  final ApiTransaction tx;
  final VoidCallback onTap;
  final VoidCallback onDelete;
  const _TxTile({required this.tx, required this.onTap, required this.onDelete});

  @override
  Widget build(BuildContext context) {
    final color = AppTheme.hexColor(tx.color);
    return Dismissible(
      key: Key('exp-tx-${tx.id}'),
      direction: DismissDirection.endToStart,
      background: Container(
        alignment: Alignment.centerRight,
        padding: const EdgeInsets.only(right: 20),
        color: AppTheme.spend,
        child: const Icon(CupertinoIcons.trash, color: CupertinoColors.white),
      ),
      confirmDismiss: (_) async { onDelete(); return false; },
      child: GestureDetector(
        onTap: onTap,
        child: Container(
          margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(10)),
          child: Row(children: [
            Container(
              width: 8, height: 40,
              decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(4)),
            ),
            const SizedBox(width: 12),
            Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(tx.description,
                  style: const TextStyle(color: AppTheme.textPrimary, fontSize: 14, fontWeight: FontWeight.w500),
                  maxLines: 1, overflow: TextOverflow.ellipsis),
              if (tx.hasAlias && tx.originalDescription != null)
                Text(tx.originalDescription!,
                    style: const TextStyle(color: AppTheme.textSecondary, fontSize: 11),
                    maxLines: 1, overflow: TextOverflow.ellipsis),
              Text('${tx.category} · ${tx.date}',
                  style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
            ])),
            const SizedBox(width: 8),
            Text(_usd.format(tx.amount),
                style: TextStyle(
                  color: tx.category == 'Income' ? AppTheme.income : AppTheme.textPrimary,
                  fontWeight: FontWeight.w600, fontSize: 14)),
            const SizedBox(width: 4),
            const Icon(CupertinoIcons.chevron_right, color: AppTheme.textSecondary, size: 14),
          ]),
        ),
      ),
    );
  }
}

// ── Add Expense Sheet ─────────────────────────────────────────────────────────

class _AddExpenseSheet extends StatefulWidget {
  final ApiService api;
  final String? ym;
  final VoidCallback onSaved;
  const _AddExpenseSheet({required this.api, required this.ym, required this.onSaved});

  @override
  State<_AddExpenseSheet> createState() => _AddExpenseSheetState();
}

class _AddExpenseSheetState extends State<_AddExpenseSheet> {
  final _descCtrl = TextEditingController();
  final _amtCtrl = TextEditingController();
  int _categoryIdx = 5; // defaults to Groceries
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _descCtrl.dispose();
    _amtCtrl.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final desc = _descCtrl.text.trim();
    final amtStr = _amtCtrl.text.trim().replaceAll(',', '');
    final amt = double.tryParse(amtStr);
    if (desc.isEmpty) { setState(() => _error = 'Enter a description'); return; }
    if (amt == null || amt <= 0) { setState(() => _error = 'Enter a valid amount'); return; }
    setState(() { _saving = true; _error = null; });
    try {
      await widget.api.addManual(
        description: desc,
        amount: amt,
        category: _expenseCategories[_categoryIdx].id,
        ym: widget.ym,
      );
      if (mounted) Navigator.pop(context);
      widget.onSaved();
    } catch (e) {
      setState(() { _error = e.toString().replaceFirst('Exception: ', ''); _saving = false; });
    }
  }

  void _pickCategory() {
    var tempIdx = _categoryIdx;
    showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => Container(
        height: 260,
        color: AppTheme.surface,
        child: Column(children: [
          SizedBox(
            height: 200,
            child: CupertinoPicker(
              scrollController: FixedExtentScrollController(initialItem: _categoryIdx),
              itemExtent: 36,
              onSelectedItemChanged: (i) => tempIdx = i,
              children: _expenseCategories.map((c) => Center(
                child: Text(c.name, style: const TextStyle(color: AppTheme.textPrimary)),
              )).toList(),
            ),
          ),
          CupertinoButton(
            child: const Text('Done'),
            onPressed: () { setState(() => _categoryIdx = tempIdx); Navigator.pop(ctx); },
          ),
        ]),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      decoration: const BoxDecoration(
        color: AppTheme.surface,
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 20, 20, 8),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            const Text('Add Expense',
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: AppTheme.textPrimary)),
            const SizedBox(height: 16),
            CupertinoTextField(
              controller: _descCtrl,
              placeholder: 'Description',
              textInputAction: TextInputAction.next,
              style: const TextStyle(color: AppTheme.textPrimary),
              placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
            ),
            const SizedBox(height: 10),
            CupertinoTextField(
              controller: _amtCtrl,
              placeholder: 'Amount (e.g. 45.00)',
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              textInputAction: TextInputAction.done,
              onSubmitted: (_) => _save(),
              style: const TextStyle(color: AppTheme.textPrimary),
              placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
              padding: const EdgeInsets.all(14),
              prefix: const Padding(
                padding: EdgeInsets.only(left: 12),
                child: Text('\$', style: TextStyle(color: AppTheme.textSecondary)),
              ),
              decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
            ),
            const SizedBox(height: 10),
            GestureDetector(
              onTap: _pickCategory,
              child: Container(
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                child: Row(children: [
                  Expanded(child: Text(_expenseCategories[_categoryIdx].name,
                      style: const TextStyle(color: AppTheme.textPrimary))),
                  const Icon(CupertinoIcons.chevron_down, color: AppTheme.textSecondary, size: 16),
                ]),
              ),
            ),
            if (_error != null) ...[
              const SizedBox(height: 8),
              Text(_error!, style: const TextStyle(color: AppTheme.spend, fontSize: 13)),
            ],
            const SizedBox(height: 16),
            CupertinoButton.filled(
              borderRadius: BorderRadius.circular(12),
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const CupertinoActivityIndicator(color: CupertinoColors.white)
                  : const Text('Add Expense', style: TextStyle(fontWeight: FontWeight.w600)),
            ),
          ]),
        ),
      ),
    );
  }
}

// ── Import Sheet ──────────────────────────────────────────────────────────────

class _ImportSheet extends StatefulWidget {
  final ApiService api;
  final String? ym;
  final VoidCallback onDone;
  const _ImportSheet({required this.api, required this.ym, required this.onDone});

  @override
  State<_ImportSheet> createState() => _ImportSheetState();
}

class _ImportSheetState extends State<_ImportSheet> {
  String? _uploadedFile;
  bool _uploading = false;
  bool _running = false;
  String? _status;
  bool _statusOk = false;

  Future<void> _pickAndUpload() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: ['pdf'],
    );
    if (result == null || result.files.single.path == null) return;
    final path = result.files.single.path!;
    setState(() { _uploading = true; _status = null; });
    try {
      final res = await widget.api.uploadPdf(File(path));
      setState(() {
        _uploading = false;
        _uploadedFile = result.files.single.name;
        _statusOk = (res['saved'] as int? ?? 0) > 0 || (res['skipped'] as int? ?? 0) == 0;
        final saved = res['saved'] as int? ?? 0;
        final skipped = res['skipped'] as int? ?? 0;
        _status = saved > 0
            ? 'Uploaded "$_uploadedFile" ($skipped duplicate${skipped != 1 ? 's' : ''} skipped)'
            : skipped > 0
                ? '"$_uploadedFile" already uploaded (skipped)'
                : 'Uploaded "$_uploadedFile"';
      });
    } catch (e) {
      setState(() {
        _uploading = false;
        _statusOk = false;
        _status = e.toString().replaceFirst('Exception: ', '');
      });
    }
  }

  Future<void> _runImport() async {
    setState(() { _running = true; _status = null; });
    try {
      final res = await widget.api.runImport(widget.ym);
      final inserted = res['inserted'] as int? ?? 0;
      final skipped = res['skipped'] as int? ?? 0;
      final files = res['files'] as int? ?? 0;
      setState(() {
        _running = false;
        _statusOk = res['success'] as bool? ?? false;
        _status = _statusOk
            ? 'Imported $inserted transaction${inserted != 1 ? 's' : ''} from $files file${files != 1 ? 's' : ''} ($skipped duplicates skipped)'
            : 'Import failed — check your PDFs';
      });
      if (_statusOk && inserted > 0) widget.onDone();
    } catch (e) {
      setState(() {
        _running = false;
        _statusOk = false;
        _status = e.toString().replaceFirst('Exception: ', '');
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: const BoxDecoration(
        color: AppTheme.surface,
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 20, 20, 8),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            const Text('Import Bank Statement',
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: AppTheme.textPrimary)),
            const SizedBox(height: 6),
            const Text(
              'Upload a PDF statement, then tap Import to process it.',
              style: TextStyle(fontSize: 13, color: AppTheme.textSecondary),
            ),
            const SizedBox(height: 20),
            CupertinoButton(
              color: AppTheme.surface,
              borderRadius: BorderRadius.circular(12),
              onPressed: _uploading || _running ? null : _pickAndUpload,
              child: _uploading
                  ? const Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                      CupertinoActivityIndicator(),
                      SizedBox(width: 8),
                      Text('Uploading…', style: TextStyle(color: AppTheme.textPrimary)),
                    ])
                  : Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                      const Icon(CupertinoIcons.arrow_up_doc, color: AppTheme.primary),
                      const SizedBox(width: 8),
                      Text(
                        _uploadedFile ?? 'Choose PDF',
                        style: TextStyle(color: _uploadedFile != null ? AppTheme.textPrimary : AppTheme.primary),
                        overflow: TextOverflow.ellipsis,
                      ),
                    ]),
            ),
            const SizedBox(height: 12),
            CupertinoButton.filled(
              borderRadius: BorderRadius.circular(12),
              onPressed: (_uploading || _running) ? null : _runImport,
              child: _running
                  ? const CupertinoActivityIndicator(color: CupertinoColors.white)
                  : const Text('Import Transactions', style: TextStyle(fontWeight: FontWeight.w600)),
            ),
            if (_status != null) ...[
              const SizedBox(height: 12),
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: _statusOk ? AppTheme.income.withValues(alpha: 0.1) : AppTheme.spend.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Text(_status!,
                    style: TextStyle(
                        color: _statusOk ? AppTheme.income : AppTheme.spend, fontSize: 13)),
              ),
            ],
            const SizedBox(height: 8),
          ]),
        ),
      ),
    );
  }
}

// ── Edit transaction sheet ────────────────────────────────────────────────────

class _EditTxSheet extends StatefulWidget {
  final ApiService api;
  final ApiTransaction tx;
  final VoidCallback onSaved;
  const _EditTxSheet({required this.api, required this.tx, required this.onSaved});

  @override
  State<_EditTxSheet> createState() => _EditTxSheetState();
}

// Expense-only categories (Income is handled by the toggle separately)
const _expenseCategories = [
  (id: 1,  name: 'Utilities'),
  (id: 2,  name: 'Insurance'),
  (id: 3,  name: 'Water'),
  (id: 4,  name: 'Subscriptions'),
  (id: 5,  name: 'Mortgage'),
  (id: 6,  name: 'Groceries'),
  (id: 7,  name: 'Dining'),
  (id: 8,  name: 'Transport'),
  (id: 9,  name: 'Healthcare'),
  (id: 10, name: 'Other'),
  (id: 12, name: 'Savings'),
  (id: 13, name: 'Chase Credit'),
  (id: 14, name: 'Texans Credit Union'),
];

class _EditTxSheetState extends State<_EditTxSheet> {
  late final TextEditingController _aliasCtrl;
  late final TextEditingController _amtCtrl;
  late int _categoryIdx;
  late bool _isIncome;
  bool _saving = false;
  bool _suggesting = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _aliasCtrl = TextEditingController(text: widget.tx.hasAlias ? widget.tx.description : '');
    _amtCtrl   = TextEditingController(text: widget.tx.amount.toStringAsFixed(2));
    _isIncome  = widget.tx.category == 'Income';
    _categoryIdx = _expenseCategories.indexWhere((c) => c.name == widget.tx.category);
    if (_categoryIdx < 0) _categoryIdx = _expenseCategories.indexWhere((c) => c.id == 10); // Other
    if (_categoryIdx < 0) _categoryIdx = 0;
  }

  @override
  void dispose() {
    _aliasCtrl.dispose();
    _amtCtrl.dispose();
    super.dispose();
  }

  Future<void> _suggestAlias() async {
    setState(() => _suggesting = true);
    final suggestion = await widget.api.suggestAlias(widget.tx.id);
    if (mounted && suggestion != null) _aliasCtrl.text = suggestion;
    if (mounted) setState(() => _suggesting = false);
  }

  Future<void> _save() async {
    final alias = _aliasCtrl.text.trim();
    final amt   = double.tryParse(_amtCtrl.text.replaceAll(',', ''));
    if (amt == null || amt <= 0) { setState(() => _error = 'Enter a valid amount'); return; }
    setState(() { _saving = true; _error = null; });
    try {
      await widget.api.updateTransaction(widget.tx.id,
        alias: alias.isEmpty ? null : alias,
        amount: amt,
        category: _isIncome ? 11 : _expenseCategories[_categoryIdx].id);
      if (mounted) Navigator.pop(context);
      widget.onSaved();
    } catch (e) {
      setState(() { _error = e.toString().replaceFirst('Exception: ', ''); _saving = false; });
    }
  }

  void _pickCategory() {
    var tempIdx = _categoryIdx;
    showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => Container(
        height: 260,
        color: AppTheme.surface,
        child: Column(children: [
          SizedBox(
            height: 200,
            child: CupertinoPicker(
              scrollController: FixedExtentScrollController(initialItem: _categoryIdx),
              itemExtent: 36,
              onSelectedItemChanged: (i) => tempIdx = i,
              children: _expenseCategories.map((c) => Center(
                child: Text(c.name, style: const TextStyle(color: AppTheme.textPrimary)),
              )).toList(),
            ),
          ),
          CupertinoButton(
            child: const Text('Done'),
            onPressed: () { setState(() => _categoryIdx = tempIdx); Navigator.pop(ctx); },
          ),
        ]),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final bankName = widget.tx.hasAlias ? widget.tx.originalDescription : widget.tx.description;
    return Container(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      decoration: const BoxDecoration(
        color: AppTheme.surface,
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 20, 20, 8),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
              const Text('Edit Transaction',
                  style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: AppTheme.textPrimary)),
              CupertinoButton(
                padding: EdgeInsets.zero,
                onPressed: () => Navigator.pop(context),
                child: const Icon(CupertinoIcons.xmark_circle_fill, color: AppTheme.textSecondary),
              ),
            ]),
            const SizedBox(height: 12),
            // Income / Expense toggle
            Container(
              decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
              child: Row(children: [
                Expanded(
                  child: GestureDetector(
                    onTap: () => setState(() => _isIncome = false),
                    child: Container(
                      padding: const EdgeInsets.symmetric(vertical: 10),
                      decoration: BoxDecoration(
                        color: !_isIncome ? AppTheme.spend.withValues(alpha: 0.15) : CupertinoColors.transparent,
                        borderRadius: BorderRadius.circular(10),
                      ),
                      child: Center(child: Text('Expense',
                          style: TextStyle(
                            fontSize: 14, fontWeight: FontWeight.w600,
                            color: !_isIncome ? AppTheme.spend : AppTheme.textSecondary))),
                    ),
                  ),
                ),
                Expanded(
                  child: GestureDetector(
                    onTap: () => setState(() => _isIncome = true),
                    child: Container(
                      padding: const EdgeInsets.symmetric(vertical: 10),
                      decoration: BoxDecoration(
                        color: _isIncome ? AppTheme.income.withValues(alpha: 0.15) : CupertinoColors.transparent,
                        borderRadius: BorderRadius.circular(10),
                      ),
                      child: Center(child: Text('Income',
                          style: TextStyle(
                            fontSize: 14, fontWeight: FontWeight.w600,
                            color: _isIncome ? AppTheme.income : AppTheme.textSecondary))),
                    ),
                  ),
                ),
              ]),
            ),
            if (bankName != null) ...[
              const SizedBox(height: 10),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  const Text('BANK DESCRIPTION',
                      style: TextStyle(color: AppTheme.textSecondary, fontSize: 10, letterSpacing: 0.6)),
                  const SizedBox(height: 2),
                  Text(bankName, style: const TextStyle(color: AppTheme.textSecondary, fontSize: 13)),
                ]),
              ),
            ],
            const SizedBox(height: 12),
            Row(children: [
              Expanded(
                child: CupertinoTextField(
                  controller: _aliasCtrl,
                  placeholder: 'Friendly name (optional)',
                  textInputAction: TextInputAction.next,
                  style: const TextStyle(color: AppTheme.textPrimary),
                  placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                ),
              ),
              const SizedBox(width: 8),
              GestureDetector(
                onTap: _suggesting ? null : _suggestAlias,
                child: Container(
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(
                    color: AppTheme.primary.withValues(alpha: 0.12),
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: _suggesting
                      ? const SizedBox(width: 20, height: 20, child: CupertinoActivityIndicator())
                      : const Icon(CupertinoIcons.sparkles, color: AppTheme.primary, size: 20),
                ),
              ),
            ]),
            const SizedBox(height: 10),
            CupertinoTextField(
              controller: _amtCtrl,
              placeholder: 'Amount',
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              textInputAction: TextInputAction.done,
              style: const TextStyle(color: AppTheme.textPrimary),
              placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
              padding: const EdgeInsets.all(14),
              prefix: const Padding(
                padding: EdgeInsets.only(left: 12),
                child: Text('\$', style: TextStyle(color: AppTheme.textSecondary)),
              ),
              decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
            ),
            if (!_isIncome) ...[
              const SizedBox(height: 10),
              GestureDetector(
                onTap: _pickCategory,
                child: Container(
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                  child: Row(children: [
                    Expanded(child: Text(_expenseCategories[_categoryIdx].name,
                        style: const TextStyle(color: AppTheme.textPrimary))),
                    const Icon(CupertinoIcons.chevron_down, color: AppTheme.textSecondary, size: 16),
                  ]),
                ),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 8),
              Text(_error!, style: const TextStyle(color: AppTheme.spend, fontSize: 13)),
            ],
            const SizedBox(height: 16),
            CupertinoButton.filled(
              borderRadius: BorderRadius.circular(12),
              onPressed: _saving ? null : _save,
              child: _saving
                  ? const CupertinoActivityIndicator(color: CupertinoColors.white)
                  : const Text('Save', style: TextStyle(fontWeight: FontWeight.w600)),
            ),
          ]),
        ),
      ),
    );
  }
}

// ── Error view ────────────────────────────────────────────────────────────────

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
