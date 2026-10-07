namespace LuminaMoney.Core;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(string AccessToken, DateTime ExpiresUtc, string RefreshToken, DateTime RefreshExpiresUtc, string UserId, string DisplayName, string Email, bool RequiresEmailVerification = false);
public sealed record RefreshRequest(string RefreshToken);
public sealed record RequestPasswordResetRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);
public sealed record VerifyEmailRequest(string Email, string Code);
public sealed record AccountActionResponse(string Message);
public sealed record DeleteAccountRequest(string Password, string Confirmation);
public sealed record ExportProfile(string UserId, string DisplayName, string Email, DateTime CreatedUtc);
public sealed record AccountExportResponse(DateTime ExportedUtc, ExportProfile Profile, SyncSnapshot FinanceData);
public sealed record SubscriptionStatusResponse(bool IsPremium, string Plan, string Status, string Source, DateTime? ExpiresUtc, DateTime? LastVerifiedUtc);
public sealed record VerifySubscriptionRequest(string Store, string ProductId, string PurchaseToken);
public sealed record BankProviderStatusResponse(string Provider, bool IsConfigured, string Environment, IReadOnlyList<string> Capabilities);
public sealed record StartBankConnectionResponse(Guid ConnectionId, string AuthorizationUri, DateTime ExpiresUtc);
public sealed record BankConnectionResponse(Guid Id, string Provider, string Status, string Environment, DateTime ConsentExpiresUtc,
    DateTime? LastSyncedUtc, int ImportedAccounts = 0, int ImportedTransactions = 0);
public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Errors = null);
public sealed record SyncSnapshot(IReadOnlyList<Account> Accounts, IReadOnlyList<BudgetCategory> Categories,
    IReadOnlyList<MoneyTransaction> Transactions, IReadOnlyList<SavingsGoal> Goals, IReadOnlyList<RecurringBill> Bills, DateTime ServerUtc,
    IReadOnlyList<BudgetAllocation>? Allocations = null);
public sealed record UpsertTransactionRequest(Guid Id, Guid AccountId, Guid? CategoryId, DateTime Date, decimal Amount,
    TransactionType Type, string Payee, string Note, bool IsCleared, Guid? TransferAccountId = null, IReadOnlyList<TransactionSplit>? Splits = null);
