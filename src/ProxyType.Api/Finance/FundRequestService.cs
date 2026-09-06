using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Security;
using ProxyType.Api.Services;

namespace ProxyType.Api.Finance;

public sealed class FundRequestService(
    ProxyTypeDbContext db,
    ICurrentScopeService scope,
    ServicePermissionService permissions,
    IConfiguration configuration)
{
    private const decimal MaximumAmount = 1_000_000m;
    private const long MaximumProofBytes = 5_000_000;
    private static readonly string[] AllowedProofTypes = ["image/jpeg", "image/png", "image/gif"];

    public async Task<FundRequestInstructionsResponse> InstructionsAsync(CancellationToken ct = default)
    {
        var membership = await PrimaryMembershipAsync(ct);
        _ = await AuthorizedServiceAsync(membership.OrganizationUnitId, ct);

        var section = configuration.GetSection("FundRequest:DepositAccount");
        var bankName = Clean(section["BankName"]);
        var accountHolderName = Clean(section["AccountHolderName"]);
        var accountNumber = Clean(section["AccountNumber"]);
        var ifscCode = Clean(section["IfscCode"])?.ToUpperInvariant();
        var qrCodeUrl = Clean(section["QrCodeUrl"]);
        var isConfigured = bankName is not null && accountHolderName is not null && accountNumber is not null && ifscCode is not null;

        return new("NEFT", bankName, accountHolderName, accountNumber, ifscCode, qrCodeUrl, isConfigured);
    }

    public async Task<FundRequestResponse> CreateAsync(FundRequestCreateRequest request, CancellationToken ct = default)
    {
        if (request.Amount < 1 || request.Amount > MaximumAmount)
            throw new InvalidOperationException("Fund Request amount must be between ₹1 and ₹1,000,000.");
        if (request.TransactionDate is null)
            throw new InvalidOperationException("Transaction date is required.");
        if (request.TransactionDate > DateOnly.FromDateTime(DateTime.Today))
            throw new InvalidOperationException("Transaction date cannot be in the future.");

        var membership = await PrimaryMembershipAsync(ct);
        var service = await AuthorizedServiceAsync(membership.OrganizationUnitId, ct);
        var existingTransaction = await db.ServiceTransactions.SingleOrDefaultAsync(
            item => item.OrganizationUnitId == membership.OrganizationUnitId && item.ClientIdempotencyKey == request.IdempotencyKey, ct);
        if (existingTransaction is not null)
        {
            var existing = await db.FundRequests.SingleAsync(item => item.ServiceTransactionId == existingTransaction.ServiceTransactionId, ct);
            return await ToResponseAsync(existing, ct);
        }

        if (await db.FundRequests.AnyAsync(item => item.ExternalReference == request.ExternalReference.Trim(), ct))
            throw new InvalidOperationException("This payment reference has already been submitted.");

        var proof = await ReadProofAsync(request.Proof, ct);
        var now = DateTime.UtcNow;
        var transactionReference = $"PTF{now:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 999)}";
        var transaction = new ServiceTransaction
        {
            ServiceTransactionId = Guid.NewGuid(),
            OrganizationUnitId = membership.OrganizationUnitId,
            UserId = scope.UserId,
            ServiceId = service.ServiceId,
            TransactionReference = transactionReference,
            ClientIdempotencyKey = request.IdempotencyKey,
            Status = "PENDING",
            Amount = request.Amount,
            ChargeAmount = 0,
            CommissionAmount = 0,
            DebitAmount = 0,
            CreditAmount = 0,
            RequestSummaryJson = JsonSerializer.Serialize(new
            {
                PaymentMode = "NEFT",
                TransactionDate = request.TransactionDate.Value,
                ExternalReference = request.ExternalReference.Trim(),
                ProofContentType = proof.ContentType,
                ProofSize = proof.Content.Length
            }),
            RequestAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var fundRequest = new FundRequest
        {
            FundRequestId = Guid.NewGuid(),
            OrganizationUnitId = membership.OrganizationUnitId,
            RequestedByUserId = scope.UserId,
            ServiceTransactionId = transaction.ServiceTransactionId,
            Amount = request.Amount,
            TransactionDate = request.TransactionDate.Value,
            PaymentMode = "NEFT",
            ExternalReference = request.ExternalReference.Trim(),
            ProofContent = proof.Content,
            ProofContentType = proof.ContentType,
            ProofFileName = proof.FileName,
            Status = "PENDING",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.ServiceTransactions.Add(transaction);
        db.FundRequests.Add(fundRequest);
        db.Receipts.Add(new Receipt
        {
            ReceiptId = Guid.NewGuid(),
            ReceiptNumber = $"RCP-{transactionReference}",
            ServiceTransactionId = transaction.ServiceTransactionId,
            IssuedToUserId = scope.UserId,
            IssuedAtUtc = now,
            SnapshotJson = JsonSerializer.Serialize(new
            {
                fundRequest.FundRequestId,
                transaction.TransactionReference,
                fundRequest.Status,
                fundRequest.Amount,
                fundRequest.TransactionDate,
                fundRequest.PaymentMode,
                fundRequest.ExternalReference
            })
        });
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = scope.UserId,
            OrganizationUnitId = membership.OrganizationUnitId,
            Action = "FUND_REQUEST_CREATED",
            EntityType = "FundRequest",
            EntityId = fundRequest.FundRequestId.ToString(),
            CorrelationId = Guid.NewGuid(),
            DetailsJson = JsonSerializer.Serialize(new { fundRequest.Amount, fundRequest.TransactionDate, fundRequest.PaymentMode, ProofType = proof.ContentType }),
            OccurredAtUtc = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var concurrent = await db.ServiceTransactions.AsNoTracking().SingleOrDefaultAsync(
                item => item.OrganizationUnitId == membership.OrganizationUnitId && item.ClientIdempotencyKey == request.IdempotencyKey, ct);
            if (concurrent is null) throw;
            var concurrentRequest = await db.FundRequests.AsNoTracking().SingleAsync(item => item.ServiceTransactionId == concurrent.ServiceTransactionId, ct);
            return await ToResponseAsync(concurrentRequest, ct);
        }

        return await ToResponseAsync(fundRequest, ct);
    }

    public async Task<IReadOnlyList<FundRequestResponse>> ListAsync(CancellationToken ct = default)
    {
        var membership = await PrimaryMembershipAsync(ct);
        var service = await AuthorizedServiceAsync(membership.OrganizationUnitId, ct);
        var canReview = await CanReviewAsync(membership.OrganizationUnitId, service.ServiceId, ct);
        var accessible = await scope.GetAccessibleOrganizationIdsAsync(ct);
        var query = db.FundRequests.AsNoTracking()
            .Join(db.Users.AsNoTracking(), request => request.RequestedByUserId, user => user.UserId, (request, user) => new { request, user })
            .Join(db.OrganizationUnits.AsNoTracking(), item => item.request.OrganizationUnitId, unit => unit.OrganizationUnitId, (item, unit) => new { item.request, item.user, unit })
            .Join(db.ServiceTransactions.AsNoTracking(), item => item.request.ServiceTransactionId, transaction => (Guid?)transaction.ServiceTransactionId, (item, transaction) => new { item.request, item.user, item.unit, transaction });

        query = canReview
            ? query.Where(item => accessible.Contains(item.request.OrganizationUnitId))
            : query.Where(item => item.request.RequestedByUserId == scope.UserId);

        var rows = await query.OrderByDescending(item => item.request.CreatedAtUtc).Take(100)
            .Select(item => new
            {
                item.request.FundRequestId,
                TransactionId = item.transaction.ServiceTransactionId,
                item.transaction.TransactionReference,
                item.request.Amount,
                item.request.PaymentMode,
                ExternalReference = item.request.ExternalReference!,
                item.request.RequestedByUserId,
                RequesterName = item.user.DisplayName,
                OrganizationCode = item.unit.Code,
                item.request.Status,
                item.request.TransactionDate,
                item.request.CreatedAtUtc,
                item.request.ReviewedAtUtc,
                item.request.ReviewReason,
                HasProof = item.request.ProofContent != null,
                ReceiptNumber = db.Receipts.Where(receipt => receipt.ServiceTransactionId == item.request.ServiceTransactionId).Select(receipt => receipt.ReceiptNumber).FirstOrDefault(),
                WalletCredited = item.request.Status == "APPROVED" && item.transaction.CreditAmount > 0
            })
            .ToListAsync(ct);

        return rows.Select(item => new FundRequestResponse(
            item.FundRequestId, item.TransactionId, item.TransactionReference, item.Status, item.Amount,
            item.PaymentMode, item.ExternalReference, item.HasProof ? $"/api/services/fund-request/requests/{item.FundRequestId}/proof" : null,
            item.RequestedByUserId, item.RequesterName, item.OrganizationCode, item.TransactionDate, item.CreatedAtUtc, item.ReviewedAtUtc,
            item.ReviewReason, item.WalletCredited, item.ReceiptNumber, canReview && item.Status == "PENDING")).ToArray();
    }

    public async Task<FundRequestResponse> GetAsync(Guid fundRequestId, CancellationToken ct = default)
    {
        var request = await db.FundRequests.SingleOrDefaultAsync(item => item.FundRequestId == fundRequestId, ct)
            ?? throw new KeyNotFoundException("Fund Request was not found.");
        var service = await AuthorizedServiceAsync(request.OrganizationUnitId, ct);
        var canReview = await CanReviewAsync(request.OrganizationUnitId, service.ServiceId, ct);
        var accessible = await scope.GetAccessibleOrganizationIdsAsync(ct);
        var isOwner = request.RequestedByUserId == scope.UserId;
        if (!accessible.Contains(request.OrganizationUnitId) || (!isOwner && !canReview))
            throw new UnauthorizedAccessException("FUND_REQUEST_OUT_OF_SCOPE");
        return await ToResponseAsync(request, ct, canReview);
    }

    public async Task<FundRequestResponse> ReviewAsync(Guid fundRequestId, FundRequestReviewRequest review, CancellationToken ct = default)
    {
        var request = await db.FundRequests.AsNoTracking().SingleOrDefaultAsync(item => item.FundRequestId == fundRequestId, ct)
            ?? throw new KeyNotFoundException("Fund Request was not found.");
        var service = await AuthorizedServiceAsync(request.OrganizationUnitId, ct);
        if (!await CanReviewAsync(request.OrganizationUnitId, service.ServiceId, ct))
            throw new UnauthorizedAccessException("FUND_REQUEST_REVIEW_NOT_ALLOWED");

        if (review.Decision.Equals("APPROVED", StringComparison.OrdinalIgnoreCase))
            await EnsureWalletAsync(request.RequestedByUserId, ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC finance.ReviewFundRequest {fundRequestId}, {scope.UserId}, {review.Decision.ToUpperInvariant()}, {review.Reason}", ct);
        return await GetAsync(fundRequestId, ct);
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> GetProofAsync(Guid fundRequestId, CancellationToken ct = default)
    {
        var request = await db.FundRequests.AsNoTracking().SingleOrDefaultAsync(item => item.FundRequestId == fundRequestId, ct)
            ?? throw new KeyNotFoundException("Fund Request was not found.");
        var service = await AuthorizedServiceAsync(request.OrganizationUnitId, ct);
        var accessible = await scope.GetAccessibleOrganizationIdsAsync(ct);
        if (!accessible.Contains(request.OrganizationUnitId) ||
            (request.RequestedByUserId != scope.UserId && !await CanReviewAsync(request.OrganizationUnitId, service.ServiceId, ct)))
            throw new UnauthorizedAccessException("FUND_REQUEST_PROOF_OUT_OF_SCOPE");
        if (request.ProofContent is null || string.IsNullOrWhiteSpace(request.ProofContentType))
            throw new KeyNotFoundException("Fund Request proof was not found.");
        return (request.ProofContent, request.ProofContentType, request.ProofFileName ?? "fund-request-proof");
    }

    private async Task<FundRequestResponse> ToResponseAsync(FundRequest request, CancellationToken ct, bool? reviewer = null)
    {
        var transaction = await db.ServiceTransactions.AsNoTracking().SingleAsync(item => item.ServiceTransactionId == request.ServiceTransactionId, ct);
        var user = await db.Users.AsNoTracking().SingleAsync(item => item.UserId == request.RequestedByUserId, ct);
        var unit = await db.OrganizationUnits.AsNoTracking().SingleAsync(item => item.OrganizationUnitId == request.OrganizationUnitId, ct);
        var service = await db.Services.AsNoTracking().SingleAsync(item => item.Code == "fund_request", ct);
        var canReview = reviewer ?? await CanReviewAsync(request.OrganizationUnitId, service.ServiceId, ct);
        var receipt = await db.Receipts.AsNoTracking().Where(item => item.ServiceTransactionId == transaction.ServiceTransactionId).Select(item => item.ReceiptNumber).SingleOrDefaultAsync(ct);
        return new(
            request.FundRequestId,
            transaction.ServiceTransactionId,
            transaction.TransactionReference,
            request.Status,
            request.Amount,
            request.PaymentMode,
            request.ExternalReference ?? string.Empty,
            request.ProofContent is null ? null : $"/api/services/fund-request/requests/{request.FundRequestId}/proof",
            request.RequestedByUserId,
            user.DisplayName,
            unit.Code,
            request.TransactionDate,
            request.CreatedAtUtc,
            request.ReviewedAtUtc,
            request.ReviewReason,
            request.Status == "APPROVED" && transaction.CreditAmount > 0,
            receipt,
            canReview && request.Status == "PENDING");
    }

    private async Task<OrganizationMembership> PrimaryMembershipAsync(CancellationToken ct)
    {
        var membership = await db.OrganizationMemberships
            .Where(item => item.UserId == scope.UserId && item.IsActive && (item.ValidToUtc == null || item.ValidToUtc > DateTime.UtcNow))
            .OrderByDescending(item => item.IsPrimary)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("An active organization assignment is required.");
        if (!(await scope.GetAccessibleOrganizationIdsAsync(ct)).Contains(membership.OrganizationUnitId))
            throw new UnauthorizedAccessException("USER_INACTIVE_OR_OUT_OF_SCOPE");
        return membership;
    }

    private async Task<FinancialService> AuthorizedServiceAsync(Guid organizationUnitId, CancellationToken ct)
    {
        var service = await db.Services.SingleOrDefaultAsync(item => item.Code == "fund_request" && item.IsActive, ct)
            ?? throw new InvalidOperationException("Fund Request is not deployed in the service catalog.");
        var decision = (await permissions.GetEffectiveAsync(organizationUnitId, ct)).SingleOrDefault(item => item.ServiceId == service.ServiceId);
        if (decision is null || !decision.IsAllowed)
            throw new UnauthorizedAccessException(decision?.DecisionReason ?? "SERVICE_NOT_ASSIGNED");
        return service;
    }

    private async Task<bool> CanReviewAsync(Guid organizationUnitId, Guid serviceId, CancellationToken ct)
    {
        if (!scope.IsPlatformAdmin && !scope.HasRole("CMF_ADMIN", "CSF_ADMIN")) return false;
        if (!await scope.CanManageOrganizationAsync(organizationUnitId, ct)) return false;
        var membership = await db.OrganizationMemberships.AsNoTracking()
            .Where(item => item.UserId == scope.UserId && item.IsActive && (item.ValidToUtc == null || item.ValidToUtc > DateTime.UtcNow))
            .OrderByDescending(item => item.IsPrimary).Select(item => item.OrganizationUnitId).FirstOrDefaultAsync(ct);
        if (membership == Guid.Empty) return false;
        var decision = (await permissions.GetEffectiveAsync(membership, ct)).SingleOrDefault(item => item.ServiceId == serviceId);
        return decision?.IsAllowed == true;
    }

    private async Task EnsureWalletAsync(Guid userId, CancellationToken ct)
    {
        if (await db.LedgerAccounts.AnyAsync(item => item.UserId == userId && item.Code == $"USER:{userId}:WALLET" && item.IsActive, ct)) return;
        var account = new LedgerAccount
        {
            LedgerAccountId = Guid.NewGuid(), UserId = userId, Code = $"USER:{userId}:WALLET", Name = "User wallet",
            AccountType = "LIABILITY", NormalBalance = "C", Currency = "INR", IsActive = true, CreatedAtUtc = DateTime.UtcNow
        };
        db.LedgerAccounts.Add(account);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(account).State = EntityState.Detached;
            if (!await db.LedgerAccounts.AnyAsync(item => item.UserId == userId && item.Code == $"USER:{userId}:WALLET" && item.IsActive, ct)) throw;
        }
    }

    private static async Task<(byte[] Content, string ContentType, string FileName)> ReadProofAsync(Microsoft.AspNetCore.Http.IFormFile? proof, CancellationToken ct)
    {
        if (proof is null || proof.Length <= 0) throw new InvalidOperationException("Screenshot proof is required.");
        if (proof.Length > MaximumProofBytes || !AllowedProofTypes.Contains(proof.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Proof must be a JPG, PNG, or GIF image up to 5 MB.");
        await using var stream = new MemoryStream();
        await proof.CopyToAsync(stream, ct);
        var content = stream.ToArray();
        var contentType = proof.ContentType.ToLowerInvariant();
        if (!HasValidImageSignature(content, contentType))
            throw new InvalidOperationException("The uploaded proof does not contain a valid JPG, PNG, or GIF image.");

        var fileName = Path.GetFileName(proof.FileName);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "fund-request-proof";
        return (content, contentType, fileName[..Math.Min(fileName.Length, 255)]);
    }

    private static bool HasValidImageSignature(byte[] content, string contentType) => contentType switch
    {
        "image/jpeg" => content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF,
        "image/png" => content.Length >= 8 && content.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/gif" => content.Length >= 6 &&
            (content.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || content.AsSpan(0, 6).SequenceEqual("GIF89a"u8)),
        _ => false
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
