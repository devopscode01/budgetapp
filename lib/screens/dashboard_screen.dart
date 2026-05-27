import 'package:flutter/cupertino.dart';
import 'package:intl/intl.dart' as intl_lib;
import '../models/models.dart';
import '../services/api_service.dart';
import '../theme/app_theme.dart';

final _usd = intl_lib.NumberFormat.currency(locale: 'en_US', symbol: '\$');

const _categories = [
  (id: 0,  name: 'Uncategorized'),
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
  (id: 11, name: 'Income'),
  (id: 12, name: 'Savings'),
];

class DashboardScreen extends StatefulWidget {
  final ApiService api;
  const DashboardScreen({super.key, required this.api});

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  List<String> _months = [];
  MonthSummary? _summary;
  NetWorth? _netWorth;
  String? _selectedYm;
  bool _loading = true;
  String? _error;
  String? _txFilter;
  String? _llmInsight;
  bool _analyzing = false;
  LlmConfig? _llmConfig;

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
      final results = await Future.wait([
        widget.api.getSummary(ym),
        widget.api.getNetWorth(),
        widget.api.getLlmConfig(),
      ]);
      setState(() {
        _months = months;
        _selectedYm = ym;
        _summary = results[0] as MonthSummary;
        _netWorth = results[1] as NetWorth;
        _llmConfig = results[2] as LlmConfig;
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

  Future<void> _selectMonth(BuildContext context) async {
    if (_months.isEmpty) return;
    final current = _months.indexOf(_selectedYm ?? '');
    await showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => Container(
        height: 260,
        color: AppTheme.surface,
        child: Column(children: [
          SizedBox(
            height: 200,
            child: CupertinoPicker(
              scrollController: FixedExtentScrollController(initialItem: current < 0 ? 0 : current),
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
          CupertinoDialogAction(isDestructiveAction: true, onPressed: () => Navigator.of(dlgCtx).pop(true), child: const Text('Delete')),
          CupertinoDialogAction(onPressed: () => Navigator.of(dlgCtx).pop(false), child: const Text('Cancel')),
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
            title: const Text('Error'), content: Text(e.toString()),
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
        cancelButton: CupertinoActionSheetAction(onPressed: () => Navigator.pop(ctx), child: const Text('Cancel')),
      ),
    );
  }

  void _showEditSheet(ApiTransaction tx) {
    showCupertinoModalPopup<void>(
      context: context,
      builder: (ctx) => _EditTxSheet(api: widget.api, tx: tx, onSaved: _load),
    );
  }

  List<ApiTransaction> get _filteredTxs {
    final txs = _summary?.transactions ?? [];
    if (_txFilter == 'Income') return txs.where((t) => t.category == 'Income').toList();
    if (_txFilter == 'Spending') return txs.where((t) => t.category != 'Income').toList();
    return txs;
  }

  Future<void> _runAnalysis() async {
    setState(() { _analyzing = true; _llmInsight = null; });
    try {
      final insight = await widget.api.analyzeLlm();
      if (mounted) setState(() => _llmInsight = insight);
    } catch (e) {
      if (mounted) setState(() => _llmInsight = 'Error: ${e.toString().replaceFirst('Exception: ', '')}');
    } finally {
      if (mounted) setState(() => _analyzing = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return CupertinoPageScaffold(
      backgroundColor: AppTheme.background,
      navigationBar: CupertinoNavigationBar(
        backgroundColor: AppTheme.surface,
        middle: const Text('Dashboard'),
        trailing: _months.isNotEmpty
            ? CupertinoButton(
                padding: EdgeInsets.zero,
                onPressed: () => _selectMonth(context),
                child: const Icon(CupertinoIcons.calendar, color: AppTheme.primary),
              )
            : null,
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
    final s = _summary!;
    final txs = _filteredTxs;
    final nw = _netWorth;
    final llmEnabled = _llmConfig?.isEnabled == true;

    return CustomScrollView(
      physics: const AlwaysScrollableScrollPhysics(),
      slivers: [
        CupertinoSliverRefreshControl(onRefresh: _load),
        SliverToBoxAdapter(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              // Month label
              Text(s.monthLabel,
                  style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700, color: AppTheme.textPrimary)),
              const SizedBox(height: 16),

              // Income / Spending cards with delta
              Row(children: [
                Expanded(child: _StatCard(
                  label: 'Income',
                  amount: s.totalIncome,
                  delta: s.incomeVsPrev,
                  color: AppTheme.income,
                  active: _txFilter == 'Income',
                  onTap: () => setState(() => _txFilter = _txFilter == 'Income' ? null : 'Income'),
                )),
                const SizedBox(width: 12),
                Expanded(child: _StatCard(
                  label: 'Spending',
                  amount: s.totalSpend,
                  delta: s.spendVsPrev,
                  color: AppTheme.spend,
                  flipDelta: true,
                  active: _txFilter == 'Spending',
                  onTap: () => setState(() => _txFilter = _txFilter == 'Spending' ? null : 'Spending'),
                )),
              ]),
              const SizedBox(height: 12),

              // Net savings + savings rate
              _NetCard(income: s.totalIncome, spend: s.totalSpend),
              const SizedBox(height: 12),

              // Net worth summary
              if (nw != null) ...[
                _NetWorthCard(nw: nw),
                const SizedBox(height: 12),
              ],

              // AI Insights
              if (llmEnabled) ...[
                CupertinoButton(
                  color: AppTheme.primary,
                  borderRadius: BorderRadius.circular(12),
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                  onPressed: _analyzing ? null : _runAnalysis,
                  child: _analyzing
                      ? const Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                          CupertinoActivityIndicator(color: CupertinoColors.white),
                          SizedBox(width: 8),
                          Text('Analyzing…', style: TextStyle(color: CupertinoColors.white)),
                        ])
                      : const Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                          Icon(CupertinoIcons.sparkles, color: CupertinoColors.white, size: 16),
                          SizedBox(width: 8),
                          Text('Get AI Insight', style: TextStyle(color: CupertinoColors.white, fontWeight: FontWeight.w600)),
                        ]),
                ),
                if (_llmInsight != null) ...[
                  const SizedBox(height: 10),
                  Container(
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(
                      color: AppTheme.surface,
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(color: AppTheme.primary.withValues(alpha: 0.3)),
                    ),
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      const Row(children: [
                        Icon(CupertinoIcons.sparkles, color: AppTheme.primary, size: 14),
                        SizedBox(width: 6),
                        Text('AI Insight', style: TextStyle(color: AppTheme.primary, fontSize: 12, fontWeight: FontWeight.w600)),
                      ]),
                      const SizedBox(height: 8),
                      Text(_llmInsight!, style: const TextStyle(color: AppTheme.textPrimary, fontSize: 13, height: 1.5)),
                    ]),
                  ),
                ],
                const SizedBox(height: 12),
              ],

              // Categories
              if (s.categories.isNotEmpty) ...[
                const Text('By Category',
                    style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600, color: AppTheme.textPrimary)),
                const SizedBox(height: 8),
                ...s.categories.map((c) => _CategoryRow(c)),
                const SizedBox(height: 16),
              ],

              // Transactions header
              Row(children: [
                Text(
                  _txFilter != null ? '$_txFilter Transactions' : 'Transactions',
                  style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600, color: AppTheme.textPrimary),
                ),
                const SizedBox(width: 8),
                if (_txFilter != null)
                  GestureDetector(
                    onTap: () => setState(() => _txFilter = null),
                    child: const Icon(CupertinoIcons.xmark_circle_fill, color: AppTheme.textSecondary, size: 18),
                  ),
                const Spacer(),
                Text('${txs.length}', style: const TextStyle(fontSize: 13, color: AppTheme.textSecondary)),
              ]),
              const SizedBox(height: 8),
            ]),
          ),
        ),
        SliverList(
          delegate: SliverChildBuilderDelegate(
            (_, i) => _TxRow(tx: txs[i], onTap: () => _showTxActions(txs[i]), onDelete: () => _deleteTransaction(txs[i])),
            childCount: txs.length,
          ),
        ),
        const SliverToBoxAdapter(child: SizedBox(height: 40)),
      ],
    );
  }
}

// ── Net savings card ──────────────────────────────────────────────────────────

class _NetCard extends StatelessWidget {
  final double income;
  final double spend;
  const _NetCard({required this.income, required this.spend});

  @override
  Widget build(BuildContext context) {
    final net = income - spend;
    final rate = income > 0 ? (net / income * 100) : 0.0;
    final positive = net >= 0;
    final color = positive ? AppTheme.income : AppTheme.spend;
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppTheme.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: color.withValues(alpha: 0.25), width: 1),
      ),
      child: Row(children: [
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('Net Savings', style: TextStyle(fontSize: 12, color: AppTheme.textSecondary)),
          const SizedBox(height: 4),
          Text(_usd.format(net), style: TextStyle(fontSize: 22, fontWeight: FontWeight.w700, color: color)),
        ])),
        Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
          const Text('Savings Rate', style: TextStyle(fontSize: 11, color: AppTheme.textSecondary)),
          const SizedBox(height: 2),
          Text('${rate.toStringAsFixed(1)}%', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, color: color)),
        ]),
      ]),
    );
  }
}

// ── Net worth card ────────────────────────────────────────────────────────────

class _NetWorthCard extends StatelessWidget {
  final NetWorth nw;
  const _NetWorthCard({required this.nw});

  @override
  Widget build(BuildContext context) {
    final color = nw.netWorth >= 0 ? AppTheme.income : AppTheme.spend;
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(12)),
      child: Row(children: [
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('Net Worth', style: TextStyle(fontSize: 12, color: AppTheme.textSecondary)),
          const SizedBox(height: 4),
          Text(_usd.format(nw.netWorth), style: TextStyle(fontSize: 22, fontWeight: FontWeight.w700, color: color)),
        ])),
        Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
          Text(_usd.format(nw.totalAssets), style: const TextStyle(fontSize: 13, color: AppTheme.income, fontWeight: FontWeight.w600)),
          const SizedBox(height: 2),
          Text('−${_usd.format(nw.totalDebts)}', style: const TextStyle(fontSize: 13, color: AppTheme.spend, fontWeight: FontWeight.w600)),
        ]),
      ]),
    );
  }
}

// ── Stat card ─────────────────────────────────────────────────────────────────

class _StatCard extends StatelessWidget {
  final String label;
  final double amount;
  final double delta;
  final Color color;
  final bool active;
  final bool flipDelta;
  final VoidCallback onTap;
  const _StatCard({required this.label, required this.amount, required this.delta,
      required this.color, required this.active, required this.onTap, this.flipDelta = false});

  @override
  Widget build(BuildContext context) {
    final deltaPositive = flipDelta ? delta <= 0 : delta >= 0;
    final deltaColor = delta == 0 ? AppTheme.textSecondary : (deltaPositive ? AppTheme.income : AppTheme.spend);
    final deltaIcon = delta > 0 ? '↑' : delta < 0 ? '↓' : '→';
    return GestureDetector(
      onTap: onTap,
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 180),
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: active ? color.withValues(alpha: 0.12) : AppTheme.surface,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: active ? color : const Color(0x00000000), width: 1.5),
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Text(label, style: const TextStyle(fontSize: 12, color: AppTheme.textSecondary)),
            const Spacer(),
            Icon(CupertinoIcons.line_horizontal_3_decrease,
                size: 13, color: active ? color : AppTheme.textSecondary),
          ]),
          const SizedBox(height: 4),
          Text(_usd.format(amount),
              style: TextStyle(fontSize: 20, fontWeight: FontWeight.w700, color: color)),
          if (delta != 0)
            Text('$deltaIcon ${_usd.format(delta.abs())} vs prev',
                style: TextStyle(fontSize: 10, color: deltaColor)),
        ]),
      ),
    );
  }
}

// ── Category row ──────────────────────────────────────────────────────────────

class _CategoryRow extends StatelessWidget {
  final ApiCategory cat;
  const _CategoryRow(this.cat);

  @override
  Widget build(BuildContext context) {
    final color = AppTheme.hexColor(cat.color);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(children: [
        Container(width: 12, height: 12, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
        const SizedBox(width: 8),
        Expanded(child: Text(cat.name, style: const TextStyle(color: AppTheme.textPrimary, fontSize: 14))),
        Text('${cat.pct}%', style: const TextStyle(color: AppTheme.textSecondary, fontSize: 13)),
        const SizedBox(width: 8),
        Text(_usd.format(cat.amount),
            style: const TextStyle(color: AppTheme.textPrimary, fontSize: 14, fontWeight: FontWeight.w600)),
      ]),
    );
  }
}

// ── Transaction row ───────────────────────────────────────────────────────────

class _TxRow extends StatelessWidget {
  final ApiTransaction tx;
  final VoidCallback onTap;
  final VoidCallback onDelete;
  const _TxRow({required this.tx, required this.onTap, required this.onDelete});

  @override
  Widget build(BuildContext context) {
    final color = AppTheme.hexColor(tx.color);
    return Dismissible(
      key: Key('tx-${tx.id}'),
      direction: DismissDirection.endToStart,
      background: Container(
        alignment: Alignment.centerRight,
        padding: const EdgeInsets.only(right: 16),
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
              Text(
                tx.hasAlias
                    ? '${tx.category} · ${tx.date} · ${tx.originalDescription}'
                    : '${tx.category} · ${tx.date}',
                style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12),
                maxLines: 1, overflow: TextOverflow.ellipsis,
              ),
            ])),
            Text(_usd.format(tx.amount),
                style: TextStyle(
                    color: tx.category == 'Income' ? AppTheme.income : AppTheme.textPrimary,
                    fontWeight: FontWeight.w600, fontSize: 14)),
            const SizedBox(width: 6),
            const Icon(CupertinoIcons.chevron_right, color: AppTheme.textSecondary, size: 14),
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

class _EditTxSheetState extends State<_EditTxSheet> {
  late final TextEditingController _aliasCtrl;
  late final TextEditingController _amtCtrl;
  late int _categoryIdx;
  bool _saving = false;
  bool _suggesting = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _aliasCtrl = TextEditingController(text: widget.tx.description);
    _amtCtrl   = TextEditingController(text: widget.tx.amount.toStringAsFixed(2));
    _categoryIdx = _categories.indexWhere((c) => c.name == widget.tx.category);
    if (_categoryIdx < 0) _categoryIdx = 10;
  }

  @override
  void dispose() {
    _aliasCtrl.dispose();
    _amtCtrl.dispose();
    super.dispose();
  }

  Future<void> _suggestAlias() async {
    setState(() { _suggesting = true; _error = null; });
    final suggestion = await widget.api.suggestAlias(widget.tx.id);
    if (mounted) {
      setState(() => _suggesting = false);
      if (suggestion != null) _aliasCtrl.text = suggestion;
    }
  }

  Future<void> _save() async {
    final alias = _aliasCtrl.text.trim();
    final amt   = double.tryParse(_amtCtrl.text.replaceAll(',', ''));
    if (alias.isEmpty) { setState(() => _error = 'Name is required'); return; }
    if (amt == null || amt <= 0) { setState(() => _error = 'Enter a valid amount'); return; }
    setState(() { _saving = true; _error = null; });
    try {
      await widget.api.updateTransaction(widget.tx.id,
        alias: alias,
        amount: amt,
        category: _categories[_categoryIdx].id,
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
              children: _categories.map((c) => Center(
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
            Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
              const Text('Edit Transaction',
                  style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: AppTheme.textPrimary)),
              CupertinoButton(
                padding: EdgeInsets.zero,
                onPressed: () => Navigator.pop(context),
                child: const Icon(CupertinoIcons.xmark_circle_fill, color: AppTheme.textSecondary),
              ),
            ]),
            // Show original bank name if this is an imported transaction
            if (widget.tx.originalDescription != null || !widget.tx.hasAlias) ...[
              const SizedBox(height: 8),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(8)),
                child: Row(children: [
                  const Icon(CupertinoIcons.building_2_fill, color: AppTheme.textSecondary, size: 12),
                  const SizedBox(width: 6),
                  Expanded(child: Text(
                    widget.tx.originalDescription ?? widget.tx.description,
                    style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12),
                    maxLines: 2, overflow: TextOverflow.ellipsis,
                  )),
                ]),
              ),
            ],
            const SizedBox(height: 12),
            Row(children: [
              Expanded(
                child: CupertinoTextField(
                  controller: _aliasCtrl,
                  placeholder: 'Display name',
                  textInputAction: TextInputAction.next,
                  style: const TextStyle(color: AppTheme.textPrimary),
                  placeholderStyle: const TextStyle(color: AppTheme.textSecondary),
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                ),
              ),
              const SizedBox(width: 8),
              CupertinoButton(
                padding: const EdgeInsets.all(10),
                color: AppTheme.primary.withValues(alpha: 0.15),
                borderRadius: BorderRadius.circular(10),
                onPressed: _suggesting ? null : _suggestAlias,
                child: _suggesting
                    ? const CupertinoActivityIndicator()
                    : const Icon(CupertinoIcons.sparkles, color: AppTheme.primary, size: 18),
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
              prefix: const Padding(padding: EdgeInsets.only(left: 12),
                  child: Text('\$', style: TextStyle(color: AppTheme.textSecondary))),
              decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
            ),
            const SizedBox(height: 10),
            GestureDetector(
              onTap: _pickCategory,
              child: Container(
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(color: AppTheme.background, borderRadius: BorderRadius.circular(10)),
                child: Row(children: [
                  Expanded(child: Text(_categories[_categoryIdx].name,
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
