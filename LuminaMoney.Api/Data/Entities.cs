using Microsoft.AspNetCore.Identity;

namespace LuminaMoney.Api.Data;

public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public abstract class UserOwnedEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AccountEntity : UserOwnedEntity
{
    public string Name { get; set; } = ""; public int Type { get; set; } public string Currency { get; set; } = "GBP";
    public decimal OpeningBalance { get; set; } public string Color { get; set; } = "#2F72F4"; public bool IsArchived { get; set; }
    public string? Source { get; set; } public string? ExternalSourceId { get; set; }
}
public sealed class CategoryEntity : UserOwnedEntity
{
    public string Name { get; set; } = ""; public string Icon { get; set; } = "•"; public string Color { get; set; } = "#10B981";
    public decimal MonthlyTarget { get; set; } public int SortOrder { get; set; }
}
public sealed class TransactionEntity : UserOwnedEntity
{
    public Guid AccountId { get; set; } public Guid? TransferAccountId { get; set; } public Guid? CategoryId { get; set; } public DateTime Date { get; set; }
    public decimal Amount { get; set; } public int Type { get; set; } public string Payee { get; set; } = "";
    public string Note { get; set; } = ""; public bool IsCleared { get; set; }
    public string? Source { get; set; } public string? ExternalSourceId { get; set; }
}
public sealed class TransactionSplitEntity
{
    public Guid Id { get; set; } public Guid UserId { get; set; } public Guid TransactionId { get; set; }
    public Guid CategoryId { get; set; } public decimal Amount { get; set; } public string Note { get; set; } = "";
}
public sealed class GoalEntity : UserOwnedEntity
{
    public string Name { get; set; } = ""; public decimal TargetAmount { get; set; } public decimal SavedAmount { get; set; }
    public DateTime TargetDate { get; set; } public string Icon { get; set; } = "★"; public string Color { get; set; } = "#10B981";
}
public sealed class BillEntity : UserOwnedEntity
{
    public string Name { get; set; } = ""; public decimal Amount { get; set; } public DateTime NextDue { get; set; }
    public int Frequency { get; set; } public Guid? CategoryId { get; set; } public bool IsSubscription { get; set; }
}
public sealed class BudgetAllocationEntity : UserOwnedEntity
{
    public Guid CategoryId { get; set; } public int Year { get; set; } public int Month { get; set; }
    public decimal Assigned { get; set; } public decimal RolledOver { get; set; }
}
public sealed class RefreshTokenEntity
{
    public Guid Id { get; set; } public Guid UserId { get; set; } public AppUser User { get; set; } = null!;
    public string TokenHash { get; set; } = ""; public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; } public DateTime? RevokedUtc { get; set; } public Guid? FamilyId { get; set; }
}

public sealed class AccountActionChallengeEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Purpose { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? UsedUtc { get; set; }
    public int FailedAttempts { get; set; }
}

public sealed class SubscriptionEntitlementEntity
{
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string Store { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string OriginalTransactionId { get; set; } = "";
    public string Status { get; set; } = "inactive";
    public DateTime? ExpiresUtc { get; set; }
    public DateTime LastVerifiedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BankConnectionEntity : UserOwnedEntity
{
    public string Provider { get; set; } = "truelayer";
    public string ExternalConnectionId { get; set; } = "";
    public string Status { get; set; } = "pending_authorisation";
    public string Environment { get; set; } = "sandbox";
    public DateTime ConsentExpiresUtc { get; set; }
    public DateTime? LastSyncedUtc { get; set; }
    public int ImportedAccountCount { get; set; }
    public int ImportedTransactionCount { get; set; }
}
