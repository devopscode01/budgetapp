class TokenResponse {
  final String accessToken;
  final String refreshToken;
  final int expiresIn;

  TokenResponse({required this.accessToken, required this.refreshToken, required this.expiresIn});

  factory TokenResponse.fromJson(Map<String, dynamic> j) => TokenResponse(
        accessToken: j['accessToken'] as String,
        refreshToken: j['refreshToken'] as String,
        expiresIn: j['expiresIn'] as int,
      );
}

class MonthLabel {
  final String ym;
  final String label;

  MonthLabel({required this.ym, required this.label});

  factory MonthLabel.fromJson(Map<String, dynamic> j) =>
      MonthLabel(ym: j['ym'] as String, label: j['label'] as String);

  @override
  String toString() => label;
}

class ApiCategory {
  final String name;
  final String color;
  final double amount;
  final int count;
  final int pct;

  ApiCategory({required this.name, required this.color, required this.amount, required this.count, required this.pct});

  factory ApiCategory.fromJson(Map<String, dynamic> j) => ApiCategory(
        name: j['name'] as String,
        color: j['color'] as String,
        amount: (j['amount'] as num).toDouble(),
        count: j['count'] as int,
        pct: j['pct'] as int,
      );
}

class ApiTransaction {
  final int id;
  final String date;
  final String description;         // display name (alias if set, else original bank text)
  final String? originalDescription; // raw bank text; null for manual entries
  final String category;
  final String color;
  final double amount;

  bool get hasAlias => originalDescription != null;

  ApiTransaction({
    required this.id,
    required this.date,
    required this.description,
    this.originalDescription,
    required this.category,
    required this.color,
    required this.amount,
  });

  factory ApiTransaction.fromJson(Map<String, dynamic> j) => ApiTransaction(
        id: j['id'] as int,
        date: j['date'] as String,
        description: j['description'] as String,
        originalDescription: j['originalDescription'] as String?,
        category: j['category'] as String,
        color: j['color'] as String,
        amount: (j['amount'] as num).toDouble(),
      );
}

class MonthSummary {
  final String monthYm;
  final String monthLabel;
  final double totalIncome;
  final double totalSpend;
  final double prevIncome;
  final double prevSpend;
  final List<ApiCategory> categories;
  final List<ApiTransaction> transactions;

  double get netSavings => totalIncome - totalSpend;
  double get savingsRate => totalIncome > 0 ? (netSavings / totalIncome * 100) : 0;
  double get incomeVsPrev => totalIncome - prevIncome;
  double get spendVsPrev => totalSpend - prevSpend;

  MonthSummary({
    required this.monthYm,
    required this.monthLabel,
    required this.totalIncome,
    required this.totalSpend,
    required this.prevIncome,
    required this.prevSpend,
    required this.categories,
    required this.transactions,
  });

  factory MonthSummary.fromJson(Map<String, dynamic> j) => MonthSummary(
        monthYm: j['monthYm'] as String,
        monthLabel: j['monthLabel'] as String,
        totalIncome: (j['totalIncome'] as num).toDouble(),
        totalSpend: (j['totalSpend'] as num).toDouble(),
        prevIncome: (j['prevIncome'] as num? ?? 0).toDouble(),
        prevSpend: (j['prevSpend'] as num? ?? 0).toDouble(),
        categories: (j['categories'] as List).map((e) => ApiCategory.fromJson(e as Map<String, dynamic>)).toList(),
        transactions:
            (j['transactions'] as List).map((e) => ApiTransaction.fromJson(e as Map<String, dynamic>)).toList(),
      );
}

class ApiDebt {
  final int id;
  final String creditorName;
  final String type;
  final double balance;
  final double minPayment;
  final double interestRate;
  final String? dueDate;
  final String notes;
  final bool isActive;
  final String addedByName;

  ApiDebt({
    required this.id,
    required this.creditorName,
    required this.type,
    required this.balance,
    required this.minPayment,
    required this.interestRate,
    this.dueDate,
    required this.notes,
    required this.isActive,
    required this.addedByName,
  });

  factory ApiDebt.fromJson(Map<String, dynamic> j) => ApiDebt(
        id: j['id'] as int,
        creditorName: j['creditorName'] as String,
        type: j['type'] as String,
        balance: (j['balance'] as num).toDouble(),
        minPayment: (j['minPayment'] as num).toDouble(),
        interestRate: (j['interestRate'] as num).toDouble(),
        dueDate: j['dueDate'] as String?,
        notes: j['notes'] as String? ?? '',
        isActive: j['isActive'] as bool,
        addedByName: j['addedByName'] as String? ?? '',
      );
}

class ApiAsset {
  final int id;
  final String name;
  final String type;
  final double value;
  final String notes;
  final String addedByName;

  ApiAsset({
    required this.id,
    required this.name,
    required this.type,
    required this.value,
    required this.notes,
    required this.addedByName,
  });

  factory ApiAsset.fromJson(Map<String, dynamic> j) => ApiAsset(
        id: j['id'] as int,
        name: j['name'] as String,
        type: j['type'] as String,
        value: (j['value'] as num).toDouble(),
        notes: j['notes'] as String? ?? '',
        addedByName: j['addedByName'] as String? ?? '',
      );
}

class NetWorth {
  final double totalAssets;
  final double totalDebts;
  final double netWorth;
  final List<ApiAsset> assets;

  NetWorth({required this.totalAssets, required this.totalDebts, required this.netWorth, required this.assets});

  factory NetWorth.fromJson(Map<String, dynamic> j) => NetWorth(
        totalAssets: (j['totalAssets'] as num).toDouble(),
        totalDebts: (j['totalDebts'] as num).toDouble(),
        netWorth: (j['netWorth'] as num).toDouble(),
        assets: (j['assets'] as List).map((e) => ApiAsset.fromJson(e as Map<String, dynamic>)).toList(),
      );
}

class ApiBillPaymentInfo {
  final int id;
  final double amount;
  final String acknowledgedAt;
  final String by;

  ApiBillPaymentInfo({required this.id, required this.amount, required this.acknowledgedAt, required this.by});

  factory ApiBillPaymentInfo.fromJson(Map<String, dynamic> j) => ApiBillPaymentInfo(
        id:             j['id'] as int,
        amount:         (j['amount'] as num).toDouble(),
        acknowledgedAt: j['acknowledgedAt'] as String,
        by:             j['by'] as String? ?? '',
      );
}

class ApiBill {
  final int id;
  final String name;
  final double? amount;
  final int dayOfMonth;
  final bool isEndOfMonth;
  final int? linkedDebtId;
  final String? linkedDebtName;
  final bool isActive;
  final String notes;
  final bool isPaidThisMonth;
  final int daysUntilDue;
  final ApiBillPaymentInfo? paymentThisMonth;

  ApiBill({
    required this.id,
    required this.name,
    this.amount,
    required this.dayOfMonth,
    required this.isEndOfMonth,
    this.linkedDebtId,
    this.linkedDebtName,
    required this.isActive,
    required this.notes,
    required this.isPaidThisMonth,
    required this.daysUntilDue,
    this.paymentThisMonth,
  });

  factory ApiBill.fromJson(Map<String, dynamic> j) => ApiBill(
        id:              j['id'] as int,
        name:            j['name'] as String,
        amount:          j['amount'] != null ? (j['amount'] as num).toDouble() : null,
        dayOfMonth:      j['dayOfMonth'] as int,
        isEndOfMonth:    j['isEndOfMonth'] as bool,
        linkedDebtId:    j['linkedDebtId'] as int?,
        linkedDebtName:  j['linkedDebtName'] as String?,
        isActive:        j['isActive'] as bool,
        notes:           j['notes'] as String? ?? '',
        isPaidThisMonth: j['isPaidThisMonth'] as bool,
        daysUntilDue:    j['daysUntilDue'] as int,
        paymentThisMonth: j['paymentThisMonth'] != null
            ? ApiBillPaymentInfo.fromJson(j['paymentThisMonth'] as Map<String, dynamic>)
            : null,
      );
}

class LlmConfig {
  final int provider;  // 0=Ollama, 1=OpenAI, 2=Gemini
  final String endpoint;
  final String apiKey;
  final String model;
  final bool isEnabled;

  LlmConfig({
    required this.provider,
    required this.endpoint,
    required this.apiKey,
    required this.model,
    required this.isEnabled,
  });

  factory LlmConfig.fromJson(Map<String, dynamic> j) => LlmConfig(
        provider:  j['provider'] as int,
        endpoint:  j['endpoint'] as String? ?? 'http://localhost:11434',
        apiKey:    j['apiKey'] as String? ?? '',
        model:     j['model'] as String? ?? 'llama3.2',
        isEnabled: j['isEnabled'] as bool? ?? false,
      );

  Map<String, dynamic> toJson() => {
        'provider':  provider,
        'endpoint':  endpoint,
        'apiKey':    apiKey,
        'model':     model,
        'isEnabled': isEnabled,
      };
}
