import 'dart:convert';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import '../models/models.dart';

const _keyAccess = 'access_token';
const _keyRefresh = 'refresh_token';
const _keyExpiry = 'token_expiry';

class AuthService {
  static const _storage = FlutterSecureStorage();

  final String baseUrl;
  AuthService(this.baseUrl);

  Future<TokenResponse> login(String username, String password) async {
    final url = '$baseUrl/api/auth/token';
    // ignore: avoid_print
    print('[Auth] POST $url  user=$username');
    final http.Response resp;
    try {
      resp = await http.post(
        Uri.parse(url),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'username': username.toLowerCase(), 'password': password}),
      );
    } catch (e) {
      // ignore: avoid_print
      print('[Auth] Network error: $e');
      throw Exception('Cannot reach server. Check your internet connection.');
    }
    // ignore: avoid_print
    print('[Auth] Response ${resp.statusCode}: ${resp.body}');
    if (resp.statusCode == 401) throw Exception('Incorrect username or password.');
    if (resp.statusCode != 200) throw Exception('Server error (${resp.statusCode}). Try again.');
    final data = TokenResponse.fromJson(jsonDecode(resp.body) as Map<String, dynamic>);
    await _save(data);
    return data;
  }

  Future<String?> getValidAccessToken() async {
    final expiry = await _storage.read(key: _keyExpiry);
    if (expiry != null) {
      final exp = DateTime.parse(expiry);
      if (DateTime.now().isBefore(exp.subtract(const Duration(minutes: 2)))) {
        return _storage.read(key: _keyAccess);
      }
    }
    return _tryRefresh();
  }

  Future<String?> _tryRefresh() async {
    final refresh = await _storage.read(key: _keyRefresh);
    if (refresh == null) return null;
    try {
      final resp = await http.post(
        Uri.parse('$baseUrl/api/auth/refresh'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'refreshToken': refresh}),
      );
      if (resp.statusCode != 200) {
        await logout();
        return null;
      }
      final data = TokenResponse.fromJson(jsonDecode(resp.body) as Map<String, dynamic>);
      await _save(data);
      return data.accessToken;
    } catch (_) {
      await logout();
      return null;
    }
  }

  Future<void> logout() async {
    await _storage.delete(key: _keyAccess);
    await _storage.delete(key: _keyRefresh);
    await _storage.delete(key: _keyExpiry);
  }

  Future<bool> isLoggedIn() async {
    final token = await getValidAccessToken();
    return token != null;
  }

  Future<void> _save(TokenResponse t) async {
    final expiry = DateTime.now().add(Duration(seconds: t.expiresIn));
    await _storage.write(key: _keyAccess, value: t.accessToken);
    await _storage.write(key: _keyRefresh, value: t.refreshToken);
    await _storage.write(key: _keyExpiry, value: expiry.toIso8601String());
  }
}
