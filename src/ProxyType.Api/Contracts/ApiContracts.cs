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
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewReason,
    bool WalletCredited,
    string? ReceiptNumber,
    bool CanReview);

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
