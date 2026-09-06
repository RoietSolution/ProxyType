using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProxyType.Api.Contracts;

public sealed record LoginRequest(
    [Required, StringLength(256, MinimumLength = 3)] string Username,
    [Required, StringLength(200, MinimumLength = 8)] string Password);

public sealed record RefreshRequest([Required] string RefreshToken);
public sealed record LogoutRequest([Required] string RefreshToken);
public sealed record ChangePasswordRequest(
    [Required, StringLength(200, MinimumLength = 8)] string CurrentPassword,
    [Required, StringLength(200, MinimumLength = 14)] string NewPassword,
    [Required] string ConfirmPassword);

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    DateTime RefreshTokenExpiresAtUtc,
    UserSummary User);

public sealed record UserSummary(
    Guid UserId,
    string Username,
    string Email,
    string DisplayName,
    bool MustChangePassword,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<MembershipSummary> Memberships);

public sealed record MembershipSummary(Guid OrganizationUnitId, string Code, string Name, string UnitType, bool IsPrimary);

public sealed record CreateOrganizationRequest(
    [Required] Guid ParentOrganizationUnitId,
    [Required, RegularExpression("CMF|CSF|CSP")] string UnitType,
    [Required, StringLength(30, MinimumLength = 2)] string Code,
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [EmailAddress] string? Email,
    [Phone] string? Mobile);

public sealed record CreateServiceRequest(
    [Required] Guid ServiceCategoryId,
    [Required, StringLength(60, MinimumLength = 2)] string Code,
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [StringLength(500)] string? Description,
    bool IsActive,
    bool IsChargeable,
    bool IsCommissionable,
    bool RequiresKyc,
    int SortOrder);

public sealed record UpdateServiceRequest(
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [StringLength(500)] string? Description,
    bool IsActive,
    bool IsChargeable,
    bool IsCommissionable,
    bool RequiresKyc,
    int SortOrder);

public sealed record SetServicePermissionRequest(
    [Required, RegularExpression("ALLOW|DENY")] string Effect,
    [StringLength(300)] string? Reason);

public sealed record EffectiveService(
    Guid ServiceId,
    string Code,
    string Name,
    string Category,
    bool IsAllowed,
    string DecisionReason,
    string? LocalEffect);

public sealed class FinoDmtRequest
{
    public FinoDmtRequest(string accountNumber, string ifscCode, string beneficiaryName, string mobile,
        decimal amount, string transferMode, string comments, string idempotencyKey)
    {
        AccountNumber = accountNumber; IfscCode = ifscCode; BeneficiaryName = beneficiaryName;
        Mobile = mobile; Amount = amount; TransferMode = transferMode; Comments = comments; IdempotencyKey = idempotencyKey;
    }

    [Required, StringLength(18, MinimumLength = 9)] public string AccountNumber { get; init; }
    [Required, RegularExpression("^[A-Z]{4}0[A-Z0-9]{6}$")] public string IfscCode { get; init; }
    [Required, StringLength(255, MinimumLength = 2)] public string BeneficiaryName { get; init; }
    [Required, RegularExpression("^[0-9]{10}$")] public string Mobile { get; init; }
    [Range(typeof(decimal), "1000", "10000")] public decimal Amount { get; init; }
    [Required, RegularExpression("IMPS|NEFT|RTGS")] public string TransferMode { get; init; }
    [Required, StringLength(1000)] public string Comments { get; init; }
    [Required, StringLength(100, MinimumLength = 8)] public string IdempotencyKey { get; init; }
}

public sealed record FinoDmtResponse(
    Guid TransactionId,
    string TransactionReference,
    string Status,
    string ProviderMode,
    string? ProviderReference,
    decimal Amount,
    decimal Charges,
    decimal Commission,
    string? FailureReason,
    string? ReceiptNumber);

public sealed class UpiFundingRequest
{
    [Range(typeof(decimal), "1", "100000")] public decimal Amount { get; set; }
    [Required, StringLength(100, MinimumLength = 8)] public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record UpiFundingResponse(
    Guid TransactionId, string TransactionReference, string Status, string ProviderMode,
    string? ProviderReference, string? PaymentUrl, string? QrCode, decimal Amount,
    decimal Charges, decimal Commission, decimal CreditAmount, bool WalletCredited,
    string? FailureReason, string? ReceiptNumber);

public sealed class UpiMockCallbackRequest
{
    [Required, StringLength(100)] public string TransactionReference { get; set; } = string.Empty;
    [Required, StringLength(180)] public string EventId { get; set; } = string.Empty;
    [Required, RegularExpression("SUCCESS|PENDING|FAILED|EXPIRED")] public string Status { get; set; } = string.Empty;
    [StringLength(180)] public string? ProviderReference { get; set; }
}

public sealed class RechargeRequest
{
    [Required, StringLength(50)]
    public string Account { get; set; } = string.Empty;

    [Range(typeof(decimal), "10", "100000")]
    public decimal Amount { get; set; }

    [Required, StringLength(100)]
    public string Operator { get; set; } = string.Empty;

    [Required, RegularExpression("^(MOBILE|DTH|GOOGLE|LAPU)$")]
    public string RechargeType { get; set; } = string.Empty;

    [Required, RegularExpression("^\\d{6}$")]
    public string Pincode { get; set; } = string.Empty;

    [Range(typeof(decimal), "-90", "90")]
    public decimal Latitude { get; set; }

    [Range(typeof(decimal), "-180", "180")]
    public decimal Longitude { get; set; }

    [Required, StringLength(100)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record RechargeOperatorResponse(string Name, string Label, string Type, string OperatorKey);

public sealed record RechargeResponse(
    Guid TransactionId,
    string TransactionReference,
    string Status,
    string ProviderMode,
    string? ProviderReference,
    string Account,
    decimal Amount,
    decimal Charges,
    decimal Commission,
    decimal DebitAmount,
    string? FailureReason,
    string? ReceiptNumber);

public sealed class AepsRequest
{
    [Required, RegularExpression("BALANCE_INQUIRY|MINI_STATEMENT|CASH_WITHDRAWAL")]
    public string TransactionType { get; set; } = string.Empty;
    [Required, RegularExpression("^\\d{12}$")] public string AadhaarNumber { get; set; } = string.Empty;
    [Required, RegularExpression("^\\d{10}$")] public string MobileNumber { get; set; } = string.Empty;
    [Required, StringLength(20)] public string BankIin { get; set; } = string.Empty;
    [Required, StringLength(100)] public string DeviceName { get; set; } = string.Empty;
    [Required, StringLength(100)] public string BiometricCaptureReference { get; set; } = string.Empty;
    [Range(typeof(decimal), "100", "10000")] public decimal? Amount { get; set; }
    [Required, StringLength(100, MinimumLength = 8)] public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record AepsBankResponse(string Iin, string Name);
public sealed record AepsResponse(Guid TransactionId, string TransactionReference, string TransactionType, string Status,
    string ProviderMode, string? ProviderReference, string MaskedAadhaar, decimal? Amount, decimal Charges,
    decimal Commission, string? FailureReason, string? ReceiptNumber);

public sealed class WalletTransferRequest
{
    [Required, RegularExpression("^\\d{10}$")]
    public string ReceiverMobile { get; set; } = string.Empty;

    [Range(typeof(decimal), "10", "1000000")]
    public decimal Amount { get; set; }

    [Required, StringLength(100, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record WalletTransferReceiverResponse(Guid UserId, Guid OrganizationUnitId, string DisplayName, string Mobile, string OrganizationCode, string OrganizationType);

public sealed record WalletTransferResponse(
    Guid TransactionId, string TransactionReference, string Status, string ReceiverName, string ReceiverMobile,
    decimal Amount, decimal Charges, decimal DebitAmount, decimal CreditAmount, string? FailureReason, string? ReceiptNumber);

public sealed class FundRequestCreateRequest
{
    [Range(typeof(decimal), "1", "1000000")]
    public decimal Amount { get; set; }

    [Required]
    public DateOnly? TransactionDate { get; set; }

    [Required, StringLength(150, MinimumLength = 1)]
    public string ExternalReference { get; set; } = string.Empty;

    [Required]
    public IFormFile? Proof { get; set; }

    [Required, StringLength(100, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class FundRequestReviewRequest
{
    [Required, RegularExpression("APPROVED|REJECTED")]
    public string Decision { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Reason { get; set; }
}

public sealed record FundRequestResponse(
    Guid FundRequestId,
    Guid TransactionId,
    string TransactionReference,
    string Status,
    decimal Amount,
    string PaymentMode,
    string ExternalReference,
    string? ProofUrl,
    Guid RequestedByUserId,
    string RequesterName,
    string OrganizationCode,
    DateOnly TransactionDate,
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewReason,
    bool WalletCredited,
    string? ReceiptNumber,
    bool CanReview);

public sealed record FundRequestInstructionsResponse(
    string PaymentMode,
    string? BankName,
    string? AccountHolderName,
    string? AccountNumber,
    string? IfscCode,
    string? QrCodeUrl,
    bool IsConfigured);

public sealed record PricingServiceOption(Guid ServiceId, string Code, string Name);
public sealed record PricingRoleOption(Guid RoleId, string Code, string Name);
public sealed record PricingMetadataResponse(PricingServiceOption[] Services, PricingRoleOption[] Roles);

public sealed record ChargeSlabResponse(
    Guid PricingRuleId, Guid ServiceId, string ServiceName, Guid RoleId, string RoleName,
    decimal AmountFrom, decimal AmountTo, string CalculationType, decimal Rate, decimal TdsRate, decimal GstRate);

public sealed class SaveChargeSlabRequest
{
    public Guid? PricingRuleId { get; set; }
    [Required] public Guid ServiceId { get; set; }
    [Required] public Guid RoleId { get; set; }
    [Range(typeof(decimal), "0", "999999999")] public decimal AmountFrom { get; set; }
    [Range(typeof(decimal), "0", "999999999")] public decimal AmountTo { get; set; }
    [Required, RegularExpression("FIXED|PERCENTAGE")] public string CalculationType { get; set; } = "FIXED";
    [Range(typeof(decimal), "0", "999999999")] public decimal Rate { get; set; }
    [Range(typeof(decimal), "0", "100")] public decimal TdsRate { get; set; }
    [Range(typeof(decimal), "0", "100")] public decimal GstRate { get; set; }
}

public sealed record RechargeCommissionResponse(
    Guid OperatorId, string OperatorName, string OperatorLabel, string RechargeType,
    Guid? RoleId, string? RoleName, Guid? UserId, string? UserName, bool IsUserOverride,
    string CalculationType, decimal Rate);

public sealed class SaveRechargeCommissionRequest
{
    public Guid? RoleId { get; set; }
    public Guid? UserId { get; set; }
    [Required, MinLength(1)] public List<RechargeCommissionValueRequest> Operators { get; set; } = [];
}

public sealed class RechargeCommissionValueRequest
{
    [Required] public Guid OperatorId { get; set; }
    [Required, RegularExpression("FIXED|PERCENTAGE")] public string CalculationType { get; set; } = "PERCENTAGE";
    [Range(typeof(decimal), "0", "999999999")] public decimal Rate { get; set; }
    public bool UseUserOverride { get; set; } = true;
}

public sealed record ServiceTransactionHistoryItem(
    Guid TransactionId,
    string TransactionReference,
    string Service,
    string? Provider,
    decimal Amount,
    decimal Charges,
    decimal Commission,
    string Status,
    string? ProviderReference,
    DateTime CreatedAtUtc);

public sealed record WalletBalanceAccount(
    Guid? LedgerAccountId,
    string Type,
    string Name,
    string Currency,
    decimal Balance,
    bool IsActive);

public sealed record WalletBalanceActivity(
    long JournalEntryId,
    string AccountType,
    string Reference,
    string Description,
    string? Memo,
    decimal Amount,
    decimal BalanceChange,
    DateTime PostedAtUtc);

public sealed record WalletBalanceResponse(
    decimal TotalBalance,
    string Currency,
    WalletBalanceAccount[] Accounts,
    WalletBalanceActivity[] RecentActivity,
    DateTime GeneratedAtUtc);

public sealed record UserListItem(Guid UserId, string Username, string Email, string? Mobile, string DisplayName, bool IsActive, bool MustChangePassword, string[] Roles, MembershipSummary[] Memberships, string? LegacySystem, string? LegacyId);
public sealed record UserPageResponse(UserListItem[] Items, int Page, int PageSize, int Total, int TotalPages);

public sealed record CreateUserRequest(
    [Required, StringLength(100, MinimumLength = 3)] string Username,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Phone, StringLength(20)] string? Mobile,
    [Required, StringLength(150, MinimumLength = 2)] string DisplayName,
    [Required, StringLength(200, MinimumLength = 14)] string Password,
    [Required, RegularExpression("CSP_USER|CSF_ADMIN|CMF_ADMIN")] string Role,
    [Required] Guid OrganizationUnitId);

public sealed record UpdateUserRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Phone, StringLength(20)] string? Mobile,
    [Required, StringLength(150, MinimumLength = 2)] string DisplayName,
    bool IsActive,
    [Required, RegularExpression("CSP_USER|CSF_ADMIN|CMF_ADMIN")] string Role,
    [Required] Guid OrganizationUnitId);

public sealed record ResetUserPasswordRequest([Required, StringLength(200, MinimumLength = 14)] string NewPassword);

public sealed class ChannelUserMutationRequest
{
    [Required, RegularExpression("CMF|CSF|CSP")] public string UnitType { get; init; } = string.Empty;
    [Required] public Guid ParentOrganizationUnitId { get; init; }
    [Required, StringLength(30, MinimumLength = 2), RegularExpression("^[A-Za-z0-9._-]+$")] public string UserCode { get; init; } = string.Empty;
    [StringLength(200, MinimumLength = 14)] public string? Password { get; init; }
    [Required, StringLength(10)] public string Prefix { get; init; } = string.Empty;
    [Required, StringLength(30, MinimumLength = 2)] public string FirstName { get; init; } = string.Empty;
    [StringLength(30)] public string? MiddleName { get; init; }
    [Required, StringLength(30, MinimumLength = 1)] public string LastName { get; init; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string Email { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{10}$")] public string Mobile { get; init; } = string.Empty;
    [StringLength(100)] public string? FatherName { get; init; }
    [StringLength(100)] public string? MotherName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    [RegularExpression("M|F|O")] public string? Gender { get; init; }
    [RegularExpression("S|M")] public string? MaritalStatus { get; init; }
    [RegularExpression("^$|^[A-Za-z]{5}[0-9]{4}[A-Za-z]$")] public string? PanNumber { get; init; }
    [StringLength(20)] public string? TinNumber { get; init; }
    [StringLength(50)] public string? MarginType { get; init; }
    [StringLength(50)] public string? ChannelType { get; init; }
    [StringLength(100)] public string? SkuType { get; init; }
    [StringLength(100)] public string? SkuRemarks { get; init; }
    [RegularExpression("^$|^[0-9]{12}$")] public string? AadhaarNumber { get; init; }
    [Range(-90d, 90d)] public decimal? Latitude { get; init; }
    [Range(-180d, 180d)] public decimal? Longitude { get; init; }
    [StringLength(150)] public string? AsmName { get; init; }
    [Required, StringLength(100, MinimumLength = 2)] public string CompanyName { get; init; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 2)] public string CompanyAddress1 { get; init; } = string.Empty;
    [StringLength(100)] public string? CompanyAddress2 { get; init; }
    [StringLength(100)] public string? CompanyAddress3 { get; init; }
    [Required, StringLength(100, MinimumLength = 2)] public string CompanyCity { get; init; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 2)] public string CompanyRegion { get; init; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 2)] public string CompanyState { get; init; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 2)] public string CompanyDistrict { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{6}$")] public string CompanyPincode { get; init; } = string.Empty;
    [RegularExpression("^$|^[0-9]{6,11}$")] public string? CompanyPhone { get; init; }
    [Required, StringLength(100, MinimumLength = 2)] public string ResidentialAddress1 { get; init; } = string.Empty;
    [StringLength(100)] public string? ResidentialAddress2 { get; init; }
    [StringLength(100)] public string? ResidentialAddress3 { get; init; }
    [Required, StringLength(100, MinimumLength = 2)] public string ResidentialCity { get; init; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 2)] public string ResidentialRegion { get; init; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 2)] public string ResidentialState { get; init; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 2)] public string ResidentialDistrict { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{6}$")] public string ResidentialPincode { get; init; } = string.Empty;
    [RegularExpression("^$|^[0-9]{6,11}$")] public string? ResidentialPhone { get; init; }
    public bool IsActive { get; init; } = true;
    public IFormFile? AadhaarDocument { get; init; }
}

public sealed record ChannelUserDetailsResponse(
    UserListItem User,
    Guid OrganizationUnitId,
    Guid ParentOrganizationUnitId,
    string OrganizationCode,
    string OrganizationName,
    System.Text.Json.JsonElement Profile,
    bool HasAadhaarDocument,
    string? AadhaarDocumentFileName);

public sealed record UserDocumentOwner(
    Guid UserId,
    string UserCode,
    string DisplayName,
    string Email,
    string? Mobile,
    string UnitType,
    string OrganizationCode);

public sealed record UserDocumentItem(
    Guid UserDocumentId,
    string DocumentType,
    string FileName,
    long FileSize,
    string UploadedBy,
    DateTime CreatedAtUtc);

public sealed class UploadUserDocumentRequest
{
    [Required, RegularExpression("APPLICATION_FORM|ADDRESS_PROOF|ID_PROOF|PAN_CARD")]
    public string DocumentType { get; init; } = string.Empty;
    [Required] public IFormFile? Document { get; init; }
}
