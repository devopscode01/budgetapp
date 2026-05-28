import 'dart:io';
import 'package:flutter/cupertino.dart';
import 'package:image_picker/image_picker.dart';
import 'package:intl/intl.dart';
import '../services/api_service.dart';
import '../theme/app_theme.dart';

class OcrImportSheet extends StatefulWidget {
  final ApiService api;
  const OcrImportSheet({super.key, required this.api});

  @override
  State<OcrImportSheet> createState() => _OcrImportSheetState();
}

class _OcrTx {
  final String date;
  final String description;
  final double amount;
  bool selected = true;
  _OcrTx({required this.date, required this.description, required this.amount});
}

class _OcrImportSheetState extends State<OcrImportSheet> {
  final _picker = ImagePicker();
  File? _image;
  bool _scanning = false;
  List<_OcrTx>? _transactions;
  bool _importing = false;
  String? _result;
  bool _resultSuccess = false;

  int get _selectedCount => _transactions?.where((t) => t.selected).length ?? 0;

  Future<void> _pick(ImageSource source) async {
    final picked = await _picker.pickImage(source: source, imageQuality: 90, maxWidth: 2000);
    if (picked == null) return;

    final file = File(picked.path);
    setState(() { _image = file; _scanning = true; _transactions = null; _result = null; });

    try {
      final txns = await widget.api.previewScreenshot(file);
      setState(() {
        _scanning = false;
        _transactions = txns
            .map((t) => _OcrTx(date: t.date, description: t.description, amount: t.amount))
            .toList();
      });
    } catch (e) {
      setState(() {
        _scanning = false;
        _result = e.toString().replaceFirst('Exception: ', '');
        _resultSuccess = false;
      });
    }
  }

  Future<void> _import() async {
    final selected = _transactions!.where((t) => t.selected).toList();
    if (selected.isEmpty) return;
    setState(() { _importing = true; _result = null; });
    try {
      final txns = selected
          .map((t) => OcrTransaction(date: t.date, description: t.description, amount: t.amount))
          .toList();
      final res = await widget.api.confirmScreenshot(txns);
      final inserted = res['inserted'] as int? ?? 0;
      final skipped  = res['skipped']  as int? ?? 0;
      setState(() {
        _importing = false;
        _result = '$inserted transaction(s) added, $skipped duplicate(s) skipped.';
        _resultSuccess = true;
        _transactions = null;
        _image = null;
      });
    } catch (e) {
      setState(() {
        _importing = false;
        _result = e.toString().replaceFirst('Exception: ', '');
        _resultSuccess = false;
      });
    }
  }

  void _reset() => setState(() { _image = null; _transactions = null; _result = null; _scanning = false; });

  @override
  Widget build(BuildContext context) {
    return CupertinoPageScaffold(
      backgroundColor: AppTheme.background,
      navigationBar: CupertinoNavigationBar(
        backgroundColor: AppTheme.surface,
        middle: const Text('Scan Statement'),
        trailing: CupertinoButton(
          padding: EdgeInsets.zero,
          onPressed: () => Navigator.pop(context),
          child: const Text('Cancel'),
        ),
      ),
      child: SafeArea(child: _buildBody()),
    );
  }

  Widget _buildBody() {
    // Success/failure full-screen result (after import completes or scanning error with no image)
    if (_result != null && _transactions == null && !_scanning) {
      return _buildResultPage();
    }
    // No image yet
    if (_image == null) return _buildPickerPage();
    // Image captured — scanning or reviewing
    return _buildReviewPage();
  }

  // ── Picker page ─────────────────────────────────────────────────────────────

  Widget _buildPickerPage() {
    return Padding(
      padding: const EdgeInsets.all(32),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const Icon(CupertinoIcons.doc_text_viewfinder, color: AppTheme.primary, size: 72),
          const SizedBox(height: 20),
          const Text(
            'Scan a bank statement',
            textAlign: TextAlign.center,
            style: TextStyle(color: AppTheme.textPrimary, fontSize: 20, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 10),
          const Text(
            'Take a photo or pick a screenshot.\nAI will read the transactions automatically.',
            textAlign: TextAlign.center,
            style: TextStyle(color: AppTheme.textSecondary, fontSize: 14, height: 1.5),
          ),
          const SizedBox(height: 48),
          _PickButton(
            icon: CupertinoIcons.camera_fill,
            label: 'Camera',
            onTap: () => _pick(ImageSource.camera),
          ),
          const SizedBox(height: 14),
          _PickButton(
            icon: CupertinoIcons.photo,
            label: 'Photo Library',
            onTap: () => _pick(ImageSource.gallery),
          ),
        ],
      ),
    );
  }

  // ── Review page (scanning + transaction list) ────────────────────────────────

  Widget _buildReviewPage() {
    final fmt = NumberFormat.currency(locale: 'en_US', symbol: '\$');

    return Column(
      children: [
        // Thumbnail
        Container(
          height: _transactions != null ? 110 : 220,
          width: double.infinity,
          margin: const EdgeInsets.fromLTRB(16, 16, 16, 0),
          child: ClipRRect(
            borderRadius: BorderRadius.circular(14),
            child: Image.file(_image!, fit: BoxFit.cover),
          ),
        ),

        // Scanning state
        if (_scanning) ...[
          const SizedBox(height: 32),
          const CupertinoActivityIndicator(radius: 14),
          const SizedBox(height: 14),
          const Text('Analyzing with AI…',
              style: TextStyle(color: AppTheme.textSecondary, fontSize: 15)),
        ],

        // Error while scanning (image is still visible, show error + rescan)
        if (!_scanning && _result != null && _transactions == null) ...[
          const SizedBox(height: 16),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16),
            child: Text(_result!,
                textAlign: TextAlign.center,
                style: const TextStyle(color: AppTheme.spend, fontSize: 14)),
          ),
          const SizedBox(height: 16),
          CupertinoButton(
            onPressed: _reset,
            child: const Text('Try Again', style: TextStyle(color: AppTheme.primary)),
          ),
        ],

        // Transaction list
        if (_transactions != null) ...[
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
            child: Row(children: [
              Text(
                '${_transactions!.length} transaction${_transactions!.length == 1 ? '' : 's'} detected',
                style: const TextStyle(color: AppTheme.textSecondary, fontSize: 13),
              ),
              const Spacer(),
              CupertinoButton(
                padding: EdgeInsets.zero,
                onPressed: () {
                  final allOn = _transactions!.every((t) => t.selected);
                  setState(() { for (final t in _transactions!) { t.selected = !allOn; } });
                },
                child: Text(
                  _transactions!.every((t) => t.selected) ? 'Deselect All' : 'Select All',
                  style: const TextStyle(color: AppTheme.primary, fontSize: 13),
                ),
              ),
            ]),
          ),
          Expanded(
            child: ListView.builder(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              itemCount: _transactions!.length,
              itemBuilder: (_, i) {
                final tx = _transactions![i];
                final isExpense = tx.amount <= 0;
                final amtColor = isExpense ? AppTheme.spend : AppTheme.income;
                final sign = isExpense ? '' : '+';
                return GestureDetector(
                  onTap: () => setState(() => tx.selected = !tx.selected),
                  child: AnimatedOpacity(
                    duration: const Duration(milliseconds: 150),
                    opacity: tx.selected ? 1.0 : 0.45,
                    child: Container(
                      margin: const EdgeInsets.only(bottom: 8),
                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                      decoration: BoxDecoration(
                        color: AppTheme.surface,
                        borderRadius: BorderRadius.circular(12),
                        border: Border.all(
                          color: tx.selected
                              ? AppTheme.primary.withValues(alpha: 0.4)
                              : AppTheme.surfaceLight,
                        ),
                      ),
                      child: Row(children: [
                        Icon(
                          tx.selected
                              ? CupertinoIcons.checkmark_circle_fill
                              : CupertinoIcons.circle,
                          color: tx.selected ? AppTheme.primary : AppTheme.textSecondary,
                          size: 20,
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                            Text(tx.description,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(
                                    color: AppTheme.textPrimary,
                                    fontSize: 14,
                                    fontWeight: FontWeight.w500)),
                            const SizedBox(height: 2),
                            Text(tx.date,
                                style: const TextStyle(
                                    color: AppTheme.textSecondary, fontSize: 11)),
                          ]),
                        ),
                        const SizedBox(width: 8),
                        Text(
                          '$sign${fmt.format(tx.amount.abs())}',
                          style: TextStyle(
                              color: amtColor,
                              fontWeight: FontWeight.w600,
                              fontSize: 14),
                        ),
                      ]),
                    ),
                  ),
                );
              },
            ),
          ),
          // Bottom bar
          Container(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
            decoration: const BoxDecoration(
              color: AppTheme.surface,
              border: Border(top: BorderSide(color: AppTheme.surfaceLight)),
            ),
            child: Column(children: [
              if (_result != null)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: Text(_result!,
                      textAlign: TextAlign.center,
                      style: TextStyle(
                          color: _resultSuccess ? AppTheme.income : AppTheme.spend,
                          fontSize: 13)),
                ),
              Row(children: [
                CupertinoButton(
                  padding: const EdgeInsets.symmetric(horizontal: 8),
                  onPressed: _importing ? null : _reset,
                  child: const Text('Rescan',
                      style: TextStyle(color: AppTheme.textSecondary, fontSize: 14)),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: CupertinoButton.filled(
                    borderRadius: BorderRadius.circular(12),
                    onPressed: (_selectedCount == 0 || _importing) ? null : _import,
                    child: _importing
                        ? const CupertinoActivityIndicator(color: CupertinoColors.white)
                        : Text(
                            _selectedCount == 0
                                ? 'None selected'
                                : 'Import $_selectedCount',
                            style: const TextStyle(fontWeight: FontWeight.w600),
                          ),
                  ),
                ),
              ]),
            ]),
          ),
        ],
      ],
    );
  }

  // ── Full-screen result (after successful import) ─────────────────────────────

  Widget _buildResultPage() {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(40),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(
              _resultSuccess
                  ? CupertinoIcons.checkmark_circle_fill
                  : CupertinoIcons.xmark_circle_fill,
              color: _resultSuccess ? AppTheme.income : AppTheme.spend,
              size: 72,
            ),
            const SizedBox(height: 20),
            Text(
              _result!,
              textAlign: TextAlign.center,
              style: const TextStyle(color: AppTheme.textPrimary, fontSize: 17),
            ),
            const SizedBox(height: 36),
            CupertinoButton.filled(
              borderRadius: BorderRadius.circular(12),
              onPressed: _resultSuccess ? () => Navigator.pop(context) : _reset,
              child: Text(_resultSuccess ? 'Done' : 'Try Again'),
            ),
          ],
        ),
      ),
    );
  }
}

// ── Reusable pick button ─────────────────────────────────────────────────────

class _PickButton extends StatelessWidget {
  const _PickButton({required this.icon, required this.label, required this.onTap});
  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.symmetric(vertical: 20),
        decoration: BoxDecoration(
          color: AppTheme.surface,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: AppTheme.primary.withValues(alpha: 0.35)),
        ),
        child: Column(children: [
          Icon(icon, color: AppTheme.primary, size: 34),
          const SizedBox(height: 8),
          Text(label,
              style: const TextStyle(
                  color: AppTheme.primary,
                  fontSize: 15,
                  fontWeight: FontWeight.w600)),
        ]),
      ),
    );
  }
}
