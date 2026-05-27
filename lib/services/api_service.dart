import 'dart:convert';
import 'dart:io';
import 'package:http/http.dart' as http;
import '../models/models.dart';
import 'auth_service.dart';

class ApiException implements Exception {
  final int statusCode;
  final String message;
  ApiException(this.statusCode, this.message);
  @override
  String toString() => 'ApiException($statusCode): $message';
}

class ApiService {
  final String baseUrl;
  final AuthService auth;

  ApiService(this.baseUrl, this.auth);

  Future<Map<String, String>> _headers() async {
    final token = await auth.getValidAccessToken();
    if (token == null) throw ApiException(401, 'Not authenticated');
    return {'Authorization': 'Bearer $token', 'Content-Type': 'application/json'};
  }

  Future<dynamic> _get(String path) async {
    final resp = await http.get(Uri.parse('$baseUrl$path'), headers: await _headers());
    _check(resp);
    return jsonDecode(resp.body);
  }

  Future<dynamic> _post(String path, Map<String, dynamic> body) async {
    final resp = await http.post(Uri.parse('$baseUrl$path'),
        headers: await _headers(), body: jsonEncode(body));
    _check(resp);
    if (resp.body.isEmpty) return null;
    return jsonDecode(resp.body);
  }

  Future<dynamic> _put(String path, Map<String, dynamic> body) async {
    final resp = await http.put(Uri.parse('$baseUrl$path'),
        headers: await _headers(), body: jsonEncode(body));
    _check(resp);
    if (resp.body.isEmpty) return null;
    return jsonDecode(resp.body);
  }

  Future<void> _delete(String path) async {
    final resp = await http.delete(Uri.parse('$baseUrl$path'), headers: await _headers());
    _check(resp);
  }

  void _check(http.Response resp) {
    if (resp.statusCode >= 400) throw ApiException(resp.statusCode, resp.body);
  }

  // Dashboard
  Future<List<String>> getMonths() async {
    final data = await _get('/api/months') as List;
    return data.map((e) => e as String).toList();
  }

  Future<MonthSummary> getSummary(String? ym) async {
    final q = ym != null ? '?ym=$ym' : '';
    final data = await _get('/api/summary$q') as Map<String, dynamic>;
    return MonthSummary.fromJson(data);
  }

  Future<ApiTransaction> updateTransaction(int id, {
    String? alias,
    required double amount,
    required int category,
  }) async {
    final data = await _put('/api/transactions/$id', {
      'alias': alias,
      'amount': amount,
      'category': category,
    }) as Map<String, dynamic>;
    return ApiTransaction.fromJson(data);
  }

  Future<String?> suggestAlias(int id) async {
    try {
      final data = await _post('/api/transactions/$id/suggest-alias', {}) as Map<String, dynamic>;
      return data['suggestion'] as String?;
    } catch (_) {
      return null;
    }
  }

  Future<void> deleteTransaction(int id) => _delete('/api/transactions/$id');

  // Debts
  Future<List<ApiDebt>> getDebts() async {
    final data = await _get('/api/debts') as List;
    return data.map((e) => ApiDebt.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<ApiDebt> addDebt({
    required String creditorName,
    required int type,
    required double balance,
    required double minPayment,
    required double interestRate,
    String? dueDate,
    String? notes,
  }) async {
    final data = await _post('/api/debts', {
      'creditorName': creditorName,
      'type': type,
      'balance': balance,
      'minPayment': minPayment,
      'interestRate': interestRate,
      'dueDate': dueDate,
      'notes': notes ?? '',
    }) as Map<String, dynamic>;
    return ApiDebt.fromJson(data);
  }

  Future<ApiDebt> updateDebt(int id, {
    String? creditorName,
    required double balance,
    required double minPayment,
    required double interestRate,
    String? dueDate,
    String? notes,
  }) async {
    final data = await _put('/api/debts/$id', {
      'creditorName': creditorName,
      'balance': balance,
      'minPayment': minPayment,
      'interestRate': interestRate,
      'dueDate': dueDate,
      'notes': notes,
    }) as Map<String, dynamic>;
    return ApiDebt.fromJson(data);
  }

  Future<void> deleteDebt(int id) => _delete('/api/debts/$id');

  // Assets / Net Worth
  Future<NetWorth> getNetWorth() async {
    final data = await _get('/api/assets') as Map<String, dynamic>;
    return NetWorth.fromJson(data);
  }

  Future<ApiAsset> addAsset({
    required String name,
    required int type,
    required double value,
    String? notes,
  }) async {
    final data = await _post('/api/assets', {
      'name': name,
      'type': type,
      'value': value,
      'notes': notes ?? '',
    }) as Map<String, dynamic>;
    return ApiAsset.fromJson(data);
  }

  Future<ApiAsset> updateAsset(int id, {String? name, required double value, String? notes}) async {
    final data = await _put('/api/assets/$id', {'name': name, 'value': value, 'notes': notes}) as Map<String, dynamic>;
    return ApiAsset.fromJson(data);
  }

  Future<void> deleteAsset(int id) => _delete('/api/assets/$id');

  // Bills
  Future<List<ApiBill>> getBills() async {
    final data = await _get('/api/bills') as List;
    return data.map((e) => ApiBill.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<ApiBill> addBill({
    required String name,
    double? amount,
    required int dayOfMonth,
    int? linkedDebtId,
    String? notes,
  }) async {
    final data = await _post('/api/bills', {
      'name':        name,
      'amount':      amount,
      'dayOfMonth':  dayOfMonth,
      'linkedDebtId': linkedDebtId,
      'notes':       notes ?? '',
    }) as Map<String, dynamic>;
    return ApiBill.fromJson(data);
  }

  Future<ApiBill> updateBill(int id, {
    required String name,
    double? amount,
    required int dayOfMonth,
    int? linkedDebtId,
    String? notes,
  }) async {
    final data = await _put('/api/bills/$id', {
      'name':        name,
      'amount':      amount,
      'dayOfMonth':  dayOfMonth,
      'linkedDebtId': linkedDebtId,
      'notes':       notes ?? '',
    }) as Map<String, dynamic>;
    return ApiBill.fromJson(data);
  }

  Future<ApiBill> acknowledgeBill(int id, {required double amount}) async {
    final data = await _post('/api/bills/$id/acknowledge', {'amount': amount}) as Map<String, dynamic>;
    return ApiBill.fromJson(data);
  }

  Future<void> deleteBill(int id) => _delete('/api/bills/$id');

  // LLM
  Future<LlmConfig> getLlmConfig() async {
    final data = await _get('/api/llm/config') as Map<String, dynamic>;
    return LlmConfig.fromJson(data);
  }

  Future<LlmConfig> saveLlmConfig(LlmConfig config) async {
    final data = await _put('/api/llm/config', config.toJson()) as Map<String, dynamic>;
    return LlmConfig.fromJson(data);
  }

  Future<String> analyzeLlm() async {
    final data = await _post('/api/llm/analyze', {}) as Map<String, dynamic>;
    return data['insight'] as String;
  }

  // Manual expense
  Future<void> addManual({
    required String description,
    required double amount,
    required int category,
    String? ym,
  }) => _post('/api/manual', {
        'description': description,
        'amount': amount,
        'category': category,
        'ym': ym,
      });

  // Import
  Future<Map<String, dynamic>> uploadPdf(File file) async {
    final token = await auth.getValidAccessToken();
    if (token == null) throw ApiException(401, 'Not authenticated');
    final request = http.MultipartRequest('POST', Uri.parse('$baseUrl/api/import/upload'))
      ..headers['Authorization'] = 'Bearer $token'
      ..files.add(await http.MultipartFile.fromPath('file', file.path,
          filename: file.path.split('/').last));
    final streamed = await request.send();
    final body = await streamed.stream.bytesToString();
    if (streamed.statusCode >= 400) throw ApiException(streamed.statusCode, body);
    return jsonDecode(body) as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> runImport(String? ym) async {
    final q = ym != null ? '?ym=$ym' : '';
    final data = await _post('/api/import/run$q', {}) as Map<String, dynamic>;
    return data;
  }
}
