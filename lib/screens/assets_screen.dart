import 'package:flutter/cupertino.dart';
import 'package:intl/intl.dart';
import '../models/models.dart';
import '../services/api_service.dart';
import '../theme/app_theme.dart';

final _usd = NumberFormat.currency(locale: 'en_US', symbol: '\$');

const _assetTypes = ['Real Estate', 'Stocks/ETFs', 'Retirement', 'Vehicle', 'Cash/Savings', 'Bonds', 'Other'];

class AssetsScreen extends StatefulWidget {
  final ApiService api;
  const AssetsScreen({super.key, required this.api});

  @override
  State<AssetsScreen> createState() => _AssetsScreenState();
}

class _AssetsScreenState extends State<AssetsScreen> {
  NetWorth? _netWorth;
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
      final nw = await widget.api.getNetWorth();
      setState(() => _netWorth = nw);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _showSheet(ApiAsset? existing) async {
    final nameCtrl = TextEditingController(text: existing?.name ?? '');
    final valCtrl = TextEditingController(text: existing?.value.toStringAsFixed(2) ?? '');
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
                  Text(existing == null ? 'Add Asset' : 'Update Asset',
                      style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w600, color: AppTheme.textPrimary)),
                  const Spacer(),
                  CupertinoButton(
                    padding: EdgeInsets.zero,
                    child: const Icon(CupertinoIcons.xmark_circle_fill, color: AppTheme.textSecondary),
                    onPressed: () => Navigator.pop(ctx),
                  ),
                ]),
                const SizedBox(height: 16),
                _Field(ctrl: nameCtrl, placeholder: 'Asset name'),
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
                          children: _assetTypes
                              .map((t) => Center(child: Text(t, style: const TextStyle(color: AppTheme.textPrimary))))
                              .toList(),
                        ),
                      ),
                    ),
                    child: Row(children: [
                      Text(_assetTypes[typeIdx], style: const TextStyle(color: AppTheme.textPrimary, fontSize: 15)),
                      const Spacer(),
                      const Icon(CupertinoIcons.chevron_up_chevron_down, size: 16, color: AppTheme.textSecondary),
                    ]),
                  ),
                  const SizedBox(height: 10),
                ],
                _Field(ctrl: valCtrl, placeholder: 'Value', numeric: true),
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
                    final val = double.tryParse(valCtrl.text);
                    if (val == null) { setSt(() => err = 'Enter a valid value'); return; }
                    if (nameCtrl.text.trim().isEmpty) { setSt(() => err = 'Enter asset name'); return; }
                    try {
                      if (existing == null) {
                        await widget.api.addAsset(
                          name: nameCtrl.text.trim(), type: typeIdx, value: val,
                          notes: notesCtrl.text.trim());
                      } else {
                        await widget.api.updateAsset(existing.id,
                          name: nameCtrl.text.trim(), value: val, notes: notesCtrl.text.trim());
                      }
                      if (ctx.mounted) Navigator.pop(ctx);
                      await _load();
                    } catch (e) {
                      setSt(() => err = 'Save failed');
                    }
                  },
                  child: Text(existing == null ? 'Add Asset' : 'Update'),
                ),
              ]),
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _delete(ApiAsset a) async {
    final confirm = await showCupertinoDialog<bool>(
      context: context,
      builder: (dlgCtx) => CupertinoAlertDialog(
        title: const Text('Delete Asset'),
        content: Text('Delete "${a.name}"?'),
        actions: [
          CupertinoDialogAction(isDestructiveAction: true, onPressed: () => Navigator.of(dlgCtx).pop(true), child: const Text('Delete')),
          CupertinoDialogAction(onPressed: () => Navigator.of(dlgCtx).pop(false), child: const Text('Cancel')),
        ],
      ),
    );
    if (confirm == true) {
      await widget.api.deleteAsset(a.id);
      await _load();
    }
  }

  @override
  Widget build(BuildContext context) {
    final nw = _netWorth;
    return CupertinoPageScaffold(
      backgroundColor: AppTheme.background,
      navigationBar: CupertinoNavigationBar(
        backgroundColor: AppTheme.surface,
        middle: const Text('Assets & Net Worth'),
        trailing: CupertinoButton(
          padding: EdgeInsets.zero,
          onPressed: () => _showSheet(null),
          child: const Icon(CupertinoIcons.add, color: AppTheme.primary),
        ),
      ),
      child: _loading
          ? const Center(child: CupertinoActivityIndicator())
          : _error != null
              ? Center(child: Text(_error!, style: const TextStyle(color: AppTheme.textSecondary)))
              : ListView(
                  padding: const EdgeInsets.all(16),
                  children: [
                    if (nw != null) ...[
                      Row(children: [
                        Expanded(child: _NwCard('Assets', nw.totalAssets, AppTheme.income)),
                        const SizedBox(width: 10),
                        Expanded(child: _NwCard('Debts', nw.totalDebts, AppTheme.spend)),
                        const SizedBox(width: 10),
                        Expanded(child: _NwCard('Net Worth', nw.netWorth,
                            nw.netWorth >= 0 ? AppTheme.income : AppTheme.spend)),
                      ]),
                      const SizedBox(height: 16),
                      ...nw.assets.map((a) => _AssetCard(asset: a,
                          onEdit: () => _showSheet(a), onDelete: () => _delete(a))),
                    ],
                    const SizedBox(height: 40),
                  ],
                ),
    );
  }
}

class _NwCard extends StatelessWidget {
  final String label;
  final double amount;
  final Color color;
  const _NwCard(this.label, this.amount, this.color);

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(12)),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(label, style: const TextStyle(color: AppTheme.textSecondary, fontSize: 11)),
          const SizedBox(height: 4),
          Text(_usd.format(amount),
              style: TextStyle(color: color, fontSize: 14, fontWeight: FontWeight.w700),
              maxLines: 1, overflow: TextOverflow.ellipsis),
        ]),
      );
}

class _AssetCard extends StatelessWidget {
  final ApiAsset asset;
  final VoidCallback onEdit;
  final VoidCallback onDelete;
  const _AssetCard({required this.asset, required this.onEdit, required this.onDelete});

  @override
  Widget build(BuildContext context) => GestureDetector(
        onTap: onEdit,
        child: Container(
          margin: const EdgeInsets.only(bottom: 10),
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(color: AppTheme.surface, borderRadius: BorderRadius.circular(12)),
          child: Row(children: [
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(asset.name, style: const TextStyle(color: AppTheme.textPrimary, fontSize: 15, fontWeight: FontWeight.w600)),
                Text(asset.type, style: const TextStyle(color: AppTheme.textSecondary, fontSize: 12)),
                if (asset.addedByName.isNotEmpty)
                  Text('Added by ${asset.addedByName}', style: const TextStyle(color: AppTheme.textSecondary, fontSize: 11)),
              ]),
            ),
            Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
              Text(_usd.format(asset.value),
                  style: const TextStyle(color: AppTheme.income, fontSize: 16, fontWeight: FontWeight.w700)),
              const SizedBox(height: 4),
              CupertinoButton(
                padding: EdgeInsets.zero,
                onPressed: onDelete,
                child: const Icon(CupertinoIcons.trash, color: AppTheme.spend, size: 16),
              ),
            ]),
          ]),
        ),
      );
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
