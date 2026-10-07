using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LuminaMoney.Api.Data;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : IdentityDbContext<AppUser, Microsoft.AspNetCore.Identity.IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>(); public DbSet<CategoryEntity> Categories => Set<CategoryEntity>(); public DbSet<BudgetAllocationEntity> BudgetAllocations => Set<BudgetAllocationEntity>();
    public DbSet<TransactionEntity> Transactions => Set<TransactionEntity>(); public DbSet<TransactionSplitEntity> TransactionSplits => Set<TransactionSplitEntity>(); public DbSet<GoalEntity> Goals => Set<GoalEntity>(); public DbSet<BillEntity> Bills => Set<BillEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<AccountActionChallengeEntity> AccountActionChallenges => Set<AccountActionChallengeEntity>();
    public DbSet<SubscriptionEntitlementEntity> SubscriptionEntitlements => Set<SubscriptionEntitlementEntity>();
    public DbSet<BankConnectionEntity> BankConnections => Set<BankConnectionEntity>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        foreach (var type in new[] { typeof(AccountEntity), typeof(CategoryEntity), typeof(TransactionEntity), typeof(GoalEntity), typeof(BillEntity), typeof(BudgetAllocationEntity) })
        {
            builder.Entity(type).HasKey(nameof(UserOwnedEntity.Id));
            builder.Entity(type).HasIndex(nameof(UserOwnedEntity.UserId), nameof(UserOwnedEntity.UpdatedUtc));
            builder.Entity(type).HasOne(nameof(UserOwnedEntity.User)).WithMany().HasForeignKey(nameof(UserOwnedEntity.UserId)).OnDelete(DeleteBehavior.Cascade);
        }
        builder.Entity<AccountEntity>().Property(x => x.OpeningBalance).HasPrecision(19, 4);
        builder.Entity<AccountEntity>().Property(x => x.Source).HasMaxLength(40);
        builder.Entity<AccountEntity>().Property(x => x.ExternalSourceId).HasMaxLength(512);
        builder.Entity<AccountEntity>().HasIndex(x => new { x.UserId, x.Source, x.ExternalSourceId }).IsUnique().HasFilter("[Source] IS NOT NULL AND [ExternalSourceId] IS NOT NULL");
        builder.Entity<CategoryEntity>().Property(x => x.MonthlyTarget).HasPrecision(19, 4);
        builder.Entity<TransactionEntity>().Property(x => x.Amount).HasPrecision(19, 4);
        builder.Entity<TransactionEntity>().Property(x => x.Source).HasMaxLength(40);
        builder.Entity<TransactionEntity>().Property(x => x.ExternalSourceId).HasMaxLength(512);
        builder.Entity<TransactionEntity>().HasIndex(x => new { x.UserId, x.Source, x.ExternalSourceId }).IsUnique().HasFilter("[Source] IS NOT NULL AND [ExternalSourceId] IS NOT NULL");
        builder.Entity<GoalEntity>().Property(x => x.TargetAmount).HasPrecision(19, 4); builder.Entity<GoalEntity>().Property(x => x.SavedAmount).HasPrecision(19, 4);
        builder.Entity<BillEntity>().Property(x => x.Amount).HasPrecision(19, 4);
        builder.Entity<BudgetAllocationEntity>().Property(x => x.Assigned).HasPrecision(19, 4); builder.Entity<BudgetAllocationEntity>().Property(x => x.RolledOver).HasPrecision(19, 4);
        builder.Entity<BudgetAllocationEntity>().HasIndex(x => new { x.UserId, x.CategoryId, x.Year, x.Month }).IsUnique();
        builder.Entity<TransactionEntity>().HasIndex(x => new { x.UserId, x.Date });
        builder.Entity<TransactionSplitEntity>().HasKey(x => x.Id); builder.Entity<TransactionSplitEntity>().HasIndex(x => new { x.UserId, x.TransactionId }); builder.Entity<TransactionSplitEntity>().Property(x => x.Amount).HasPrecision(19, 4); builder.Entity<TransactionSplitEntity>().Property(x => x.Note).HasMaxLength(240); builder.Entity<TransactionSplitEntity>().HasOne<TransactionEntity>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<RefreshTokenEntity>().HasIndex(x => x.TokenHash).IsUnique();
        builder.Entity<RefreshTokenEntity>().HasIndex(x => new { x.UserId, x.FamilyId, x.RevokedUtc });
        builder.Entity<RefreshTokenEntity>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SubscriptionEntitlementEntity>().HasKey(x => x.UserId);
        builder.Entity<SubscriptionEntitlementEntity>().HasOne(x => x.User).WithOne().HasForeignKey<SubscriptionEntitlementEntity>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SubscriptionEntitlementEntity>().Property(x => x.Store).HasMaxLength(20);
        builder.Entity<SubscriptionEntitlementEntity>().Property(x => x.ProductId).HasMaxLength(120);
        builder.Entity<SubscriptionEntitlementEntity>().Property(x => x.OriginalTransactionId).HasMaxLength(240);
        builder.Entity<SubscriptionEntitlementEntity>().Property(x => x.Status).HasMaxLength(32);
        builder.Entity<BankConnectionEntity>().HasKey(x => x.Id);
        builder.Entity<BankConnectionEntity>().HasIndex(x => new { x.UserId, x.UpdatedUtc });
        builder.Entity<BankConnectionEntity>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<BankConnectionEntity>().Property(x => x.Provider).HasMaxLength(40);
        builder.Entity<BankConnectionEntity>().Property(x => x.ExternalConnectionId).HasMaxLength(160);
        builder.Entity<BankConnectionEntity>().Property(x => x.Status).HasMaxLength(40);
        builder.Entity<BankConnectionEntity>().Property(x => x.Environment).HasMaxLength(20);
        builder.Entity<AppUser>().Property(x => x.DisplayName).HasMaxLength(80);
        builder.Entity<AccountEntity>().Property(x => x.Name).HasMaxLength(120); builder.Entity<AccountEntity>().Property(x => x.Currency).HasMaxLength(3); builder.Entity<AccountEntity>().Property(x => x.Color).HasMaxLength(9);
        builder.Entity<CategoryEntity>().Property(x => x.Name).HasMaxLength(80); builder.Entity<CategoryEntity>().Property(x => x.Icon).HasMaxLength(8); builder.Entity<CategoryEntity>().Property(x => x.Color).HasMaxLength(9);
        builder.Entity<TransactionEntity>().Property(x => x.Payee).HasMaxLength(160); builder.Entity<TransactionEntity>().Property(x => x.Note).HasMaxLength(500);
        builder.Entity<GoalEntity>().Property(x => x.Name).HasMaxLength(120); builder.Entity<GoalEntity>().Property(x => x.Icon).HasMaxLength(8); builder.Entity<GoalEntity>().Property(x => x.Color).HasMaxLength(9);
        builder.Entity<BillEntity>().Property(x => x.Name).HasMaxLength(120);
        builder.Entity<RefreshTokenEntity>().Property(x => x.TokenHash).HasMaxLength(128);
        builder.Entity<AccountActionChallengeEntity>().HasKey(x => x.Id);
        builder.Entity<AccountActionChallengeEntity>().HasIndex(x => new { x.UserId, x.Purpose, x.CreatedUtc });
        builder.Entity<AccountActionChallengeEntity>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AccountActionChallengeEntity>().Property(x => x.Purpose).HasMaxLength(32);
        builder.Entity<AccountActionChallengeEntity>().Property(x => x.CodeHash).HasMaxLength(128);
    }
}
