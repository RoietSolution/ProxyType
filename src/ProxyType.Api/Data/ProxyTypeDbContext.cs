using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Domain;

namespace ProxyType.Api.Data;

public sealed class ProxyTypeDbContext(DbContextOptions<ProxyTypeDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<FinancialService> Services => Set<FinancialService>();
    public DbSet<OrganizationServicePermission> OrganizationServicePermissions => Set<OrganizationServicePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<ServiceProviderRoute> ServiceProviderRoutes => Set<ServiceProviderRoute>();
    public DbSet<ServiceTransaction> ServiceTransactions => Set<ServiceTransaction>();
    public DbSet<FundRequest> FundRequests => Set<FundRequest>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<JournalTransaction> JournalTransactions => Set<JournalTransaction>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<RechargeOperator> RechargeOperators => Set<RechargeOperator>();
    public DbSet<AepsBank> AepsBanks => Set<AepsBank>();
    public DbSet<AepsTransactionDetail> AepsTransactionDetails => Set<AepsTransactionDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users", "auth");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.UserId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.SecurityStamp).HasDefaultValueSql("NEWID()");
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.Ignore("NormalizedUsername");
            entity.Ignore("NormalizedEmail");
        });

        modelBuilder.Entity<AppRole>(entity =>
        {
            entity.ToTable("Roles", "auth");
            entity.HasKey(x => x.RoleId);
            entity.Property(x => x.RoleId).HasDefaultValueSql("NEWSEQUENTIALID()");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles", "auth");
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId);
            entity.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens", "auth");
            entity.HasKey(x => x.RefreshTokenId);
            entity.Property(x => x.RefreshTokenId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<LoginAudit>(entity =>
        {
            entity.ToTable("LoginAudits", "auth");
            entity.HasKey(x => x.LoginAuditId);
        });

        modelBuilder.Entity<OrganizationUnit>(entity =>
        {
            entity.ToTable("OrganizationUnits", "org");
            entity.HasKey(x => x.OrganizationUnitId);
            entity.Property(x => x.OrganizationUnitId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasOne(x => x.Parent).WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentOrganizationUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrganizationMembership>(entity =>
        {
            entity.ToTable("OrganizationMemberships", "org");
            entity.HasKey(x => x.OrganizationMembershipId);
            entity.Property(x => x.OrganizationMembershipId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasOne(x => x.User).WithMany(x => x.Memberships).HasForeignKey(x => x.UserId);
            entity.HasOne(x => x.OrganizationUnit).WithMany().HasForeignKey(x => x.OrganizationUnitId);
        });

        modelBuilder.Entity<ServiceCategory>(entity =>
        {
            entity.ToTable("ServiceCategories", "catalog");
            entity.HasKey(x => x.ServiceCategoryId);
        });

        modelBuilder.Entity<FinancialService>(entity =>
        {
            entity.ToTable("Services", "catalog");
            entity.HasKey(x => x.ServiceId);
            entity.Property(x => x.ServiceId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.ServiceCategoryId);
        });

        modelBuilder.Entity<OrganizationServicePermission>(entity =>
        {
            entity.ToTable("OrganizationServicePermissions", "catalog");
            entity.HasKey(x => x.OrganizationServicePermissionId);
            entity.Property(x => x.OrganizationServicePermissionId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasOne(x => x.OrganizationUnit).WithMany().HasForeignKey(x => x.OrganizationUnitId);
            entity.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs", "audit");
            entity.HasKey(x => x.AuditLogId);
        });

        modelBuilder.Entity<Provider>(entity =>
        {
            entity.ToTable("Providers", "catalog");
            entity.HasKey(x => x.ProviderId);
            entity.Property(x => x.ProviderId).HasDefaultValueSql("NEWSEQUENTIALID()");
        });

        modelBuilder.Entity<ServiceProviderRoute>(entity =>
        {
            entity.ToTable("ServiceProviders", "catalog");
            entity.HasKey(x => x.ServiceProviderId);
            entity.Property(x => x.ServiceProviderId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId);
            entity.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId);
        });

        modelBuilder.Entity<ServiceTransaction>(entity =>
        {
            entity.ToTable("ServiceTransactions", "finance");
            entity.HasKey(x => x.ServiceTransactionId);
            entity.Property(x => x.ServiceTransactionId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.Property(x => x.Amount).HasPrecision(19, 4);
            entity.Property(x => x.ChargeAmount).HasPrecision(19, 4);
            entity.Property(x => x.CommissionAmount).HasPrecision(19, 4);
            entity.Property(x => x.TaxAmount).HasPrecision(19, 4);
            entity.Property(x => x.DebitAmount).HasPrecision(19, 4);
            entity.Property(x => x.CreditAmount).HasPrecision(19, 4);
            entity.HasIndex(x => new { x.CounterpartyUserId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<FundRequest>(entity =>
        {
            entity.ToTable("FundRequests", "finance");
            entity.HasKey(x => x.FundRequestId);
            entity.Property(x => x.FundRequestId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Amount).HasPrecision(19, 4);
            entity.Property(x => x.PaymentMode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ExternalReference).HasMaxLength(150);
            entity.Property(x => x.ProofContentType).HasMaxLength(100);
            entity.Property(x => x.ProofFileName).HasMaxLength(255);
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ReviewReason).HasMaxLength(500);
            entity.Property(x => x.ProofContent).HasColumnType("varbinary(max)");
        });

        modelBuilder.Entity<LedgerAccount>(entity =>
        {
            entity.ToTable("LedgerAccounts", "finance");
            entity.HasKey(x => x.LedgerAccountId);
            entity.Property(x => x.LedgerAccountId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.Property(x => x.CurrentBalance).HasPrecision(19, 4);
        });

        modelBuilder.Entity<JournalTransaction>(entity =>
        {
            entity.ToTable("JournalTransactions", "finance");
            entity.HasKey(x => x.JournalTransactionId);
        });

        modelBuilder.Entity<JournalEntry>(entity =>
        {
            entity.ToTable("JournalEntries", "finance");
            entity.HasKey(x => x.JournalEntryId);
            entity.Property(x => x.DebitAmount).HasPrecision(19, 4);
            entity.Property(x => x.CreditAmount).HasPrecision(19, 4);
        });

        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.ToTable("Receipts", "finance");
            entity.HasKey(x => x.ReceiptId);
            entity.Property(x => x.ReceiptId).HasDefaultValueSql("NEWSEQUENTIALID()");
        });

        modelBuilder.Entity<RechargeOperator>(entity =>
        {
            entity.ToTable("RechargeOperators", "finance");
            entity.HasKey(x => x.RechargeOperatorId);
            entity.Property(x => x.RechargeOperatorId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(30).IsRequired();
            entity.Property(x => x.OperatorKey).HasMaxLength(50).IsRequired();
            entity.Property(x => x.CommissionType).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CommissionValue).HasPrecision(18, 4);
            entity.HasIndex(x => new { x.Label, x.Type }).IsUnique();
        });

        modelBuilder.Entity<AepsBank>(entity =>
        {
            entity.ToTable("AepsBanks", "finance");
            entity.HasKey(x => x.AepsBankId);
            entity.Property(x => x.AepsBankId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Iin).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.ProviderBankCode).HasMaxLength(50);
            entity.HasIndex(x => x.Iin).IsUnique();
        });

        modelBuilder.Entity<AepsTransactionDetail>(entity =>
        {
            entity.ToTable("AepsTransactionDetails", "finance");
            entity.HasKey(x => x.AepsTransactionDetailId);
            entity.Property(x => x.AepsTransactionDetailId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.TransactionType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.MaskedAadhaar).HasMaxLength(20).IsRequired();
            entity.Property(x => x.MaskedMobile).HasMaxLength(20).IsRequired();
            entity.Property(x => x.DeviceName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DeviceProvider).HasMaxLength(80);
            entity.HasIndex(x => x.ServiceTransactionId).IsUnique();
            entity.HasOne(x => x.ServiceTransaction).WithMany().HasForeignKey(x => x.ServiceTransactionId);
            entity.HasOne(x => x.Bank).WithMany().HasForeignKey(x => x.AepsBankId);
        });
    }
}
