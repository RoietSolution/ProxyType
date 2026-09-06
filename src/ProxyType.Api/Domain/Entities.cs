namespace ProxyType.Api.Domain;

public sealed class AppUser
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Guid SecurityStamp { get; set; }
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public DateTime PasswordChangedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? LegacySystem { get; set; }
    public string? LegacyId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<UserRole> UserRoles { get; set; } = [];
    public ICollection<OrganizationMembership> Memberships { get; set; } = [];
    public UserProfile? Profile { get; set; }
}

public sealed class UserProfile
{
    public Guid UserId { get; set; }
    public string ProfileJson { get; set; } = "{}";
    public byte[]? AadhaarDocumentContent { get; set; }
    public string? AadhaarDocumentContentType { get; set; }
    public string? AadhaarDocumentFileName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public AppUser User { get; set; } = null!;
}

public sealed class UserDocument
{
    public Guid UserDocumentId { get; set; }
    public Guid UserId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public byte[] Content { get; set; } = [];
    public Guid UploadedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public AppUser User { get; set; } = null!;
}

public sealed class AppRole
{
    public Guid RoleId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
}

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime AssignedAtUtc { get; set; }
    public Guid? AssignedByUserId { get; set; }
    public AppUser User { get; set; } = null!;
    public AppRole Role { get; set; } = null!;
}

public sealed class RefreshToken
{
    public Guid RefreshTokenId { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid FamilyId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
    public AppUser User { get; set; } = null!;
}

public sealed class LoginAudit
{
    public long LoginAuditId { get; set; }
    public Guid? UserId { get; set; }
    public string UsernameAttempted { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public string? FailureCode { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class OrganizationUnit
{
    public Guid OrganizationUnitId { get; set; }
    public Guid? ParentOrganizationUnitId { get; set; }
    public string UnitType { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public OrganizationUnit? Parent { get; set; }
    public ICollection<OrganizationUnit> Children { get; set; } = [];
}

public sealed class OrganizationMembership
{
    public Guid OrganizationMembershipId { get; set; }
    public Guid OrganizationUnitId { get; set; }
    public Guid UserId { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
    public DateTime ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public OrganizationUnit OrganizationUnit { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}

public sealed class ServiceCategory
{
    public Guid ServiceCategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class FinancialService
{
    public Guid ServiceId { get; set; }
    public Guid ServiceCategoryId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public bool IsActive { get; set; }
    public bool IsChargeable { get; set; }
    public bool IsCommissionable { get; set; }
    public bool RequiresKyc { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ServiceCategory Category { get; set; } = null!;
}

public sealed class OrganizationServicePermission
{
    public Guid OrganizationServicePermissionId { get; set; }
    public Guid OrganizationUnitId { get; set; }
    public Guid ServiceId { get; set; }
    public string Effect { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public OrganizationUnit OrganizationUnit { get; set; } = null!;
    public FinancialService Service { get; set; } = null!;
}

public sealed class PricingRule
{
    public Guid PricingRuleId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid? RoleId { get; set; }
    public string RuleKind { get; set; } = string.Empty;
    public string CalculationType { get; set; } = string.Empty;
    public decimal AmountFrom { get; set; }
    public decimal AmountTo { get; set; }
    public decimal Rate { get; set; }
    public decimal TdsRate { get; set; }
    public decimal GstRate { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; }
    public FinancialService Service { get; set; } = null!;
    public AppRole? Role { get; set; }
}

public sealed class AuditLog
{
    public long AuditLogId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? OrganizationUnitId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public Guid CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public string? DetailsJson { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class Provider
{
    public Guid ProviderId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? SecretReference { get; set; }
    public bool IsEnabled { get; set; }
    public int TimeoutSeconds { get; set; }
}

public sealed class ServiceProviderRoute
{
    public Guid ServiceProviderId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid ProviderId { get; set; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public string? ConfigurationJson { get; set; }
    public FinancialService Service { get; set; } = null!;
    public Provider Provider { get; set; } = null!;
}

public sealed class ServiceTransaction
{
    public Guid ServiceTransactionId { get; set; }
    public Guid OrganizationUnitId { get; set; }
    public Guid UserId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid? CounterpartyUserId { get; set; }
    public Guid? CounterpartyOrganizationUnitId { get; set; }
    public Guid? ProviderId { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public string ClientIdempotencyKey { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }
    public string Status { get; set; } = "CREATED";
    public string? ProviderStatus { get; set; }
    public decimal Amount { get; set; }
    public decimal ChargeAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string Currency { get; set; } = "INR";
    public string? RequestSummaryJson { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? Remarks { get; set; }
    public DateTime RequestAtUtc { get; set; }
    public DateTime? ResponseAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class FundRequest
{
    public Guid FundRequestId { get; set; }
    public Guid OrganizationUnitId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? ServiceTransactionId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string PaymentMode { get; set; } = "NEFT";
    public string? ExternalReference { get; set; }
    public byte[]? ProofContent { get; set; }
    public string? ProofContentType { get; set; }
    public string? ProofFileName { get; set; }
    public string Status { get; set; } = "PENDING";
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LedgerAccount
{
    public Guid LedgerAccountId { get; set; }
    public Guid? OrganizationUnitId { get; set; }
    public Guid? UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
    public string Currency { get; set; } = "INR";
    public decimal CurrentBalance { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class JournalTransaction
{
    public Guid JournalTransactionId { get; set; }
    public Guid? ServiceTransactionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "DRAFT";
    public Guid? ReversalOfJournalId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PostedAtUtc { get; set; }
}

public sealed class JournalEntry
{
    public long JournalEntryId { get; set; }
    public Guid JournalTransactionId { get; set; }
    public Guid LedgerAccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? Memo { get; set; }
}

public sealed class Receipt
{
    public Guid ReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public Guid? ServiceTransactionId { get; set; }
    public Guid? JournalTransactionId { get; set; }
    public Guid IssuedToUserId { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public string SnapshotJson { get; set; } = "{}";
}

public sealed class RechargeOperator
{
    public Guid RechargeOperatorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string OperatorKey { get; set; } = string.Empty;
    public string CommissionType { get; set; } = "FIXED";
    public decimal CommissionValue { get; set; }
    public bool IsActive { get; set; }
}

public sealed class RechargeCommissionRule
{
    public Guid RechargeCommissionRuleId { get; set; }
    public Guid RechargeOperatorId { get; set; }
    public Guid? RoleId { get; set; }
    public Guid? UserId { get; set; }
    public string CalculationType { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool IsActive { get; set; }
    public RechargeOperator Operator { get; set; } = null!;
    public AppRole? Role { get; set; }
    public AppUser? User { get; set; }
}

public sealed class AepsBank
{
    public Guid AepsBankId { get; set; }
    public string Iin { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ProviderBankCode { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AepsTransactionDetail
{
    public Guid AepsTransactionDetailId { get; set; }
    public Guid ServiceTransactionId { get; set; }
    public Guid AepsBankId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string MaskedAadhaar { get; set; } = string.Empty;
    public string MaskedMobile { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string? DeviceProvider { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ServiceTransaction ServiceTransaction { get; set; } = null!;
    public AepsBank Bank { get; set; } = null!;
}
