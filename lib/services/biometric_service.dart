import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:local_auth/local_auth.dart';

const _keyEnabled = 'biometric_enabled';
const _keyUsername = 'biometric_username';
const _keyPassword = 'biometric_password';

class BiometricService {
  static const _storage = FlutterSecureStorage();
  static final _auth = LocalAuthentication();

  Future<bool> isAvailable() async {
    try {
      final canCheck = await _auth.canCheckBiometrics;
      final isSupported = await _auth.isDeviceSupported();
      return canCheck && isSupported;
    } catch (_) {
      return false;
    }
  }

  Future<bool> isEnabled() async {
    final val = await _storage.read(key: _keyEnabled);
    return val == 'true';
  }

  Future<void> enable(String username, String password) async {
    await _storage.write(key: _keyEnabled, value: 'true');
    await _storage.write(key: _keyUsername, value: username);
    await _storage.write(key: _keyPassword, value: password);
  }

  Future<void> disable() async {
    await _storage.delete(key: _keyEnabled);
    await _storage.delete(key: _keyUsername);
    await _storage.delete(key: _keyPassword);
  }

  /// Returns (username, password) if biometric auth succeeds, null otherwise.
  Future<(String, String)?> authenticate() async {
    try {
      final ok = await _auth.authenticate(
        localizedReason: 'Sign in to DollarCount',
        options: const AuthenticationOptions(biometricOnly: true, stickyAuth: true),
      );
      if (!ok) return null;
      final username = await _storage.read(key: _keyUsername);
      final password = await _storage.read(key: _keyPassword);
      if (username == null || password == null) return null;
      return (username, password);
    } catch (_) {
      return null;
    }
  }

  Future<List<BiometricType>> availableTypes() async {
    try {
      return await _auth.getAvailableBiometrics();
    } catch (_) {
      return [];
    }
  }
}
