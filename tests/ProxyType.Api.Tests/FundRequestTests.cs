using System.ComponentModel.DataAnnotations;
using ProxyType.Api.Contracts;

namespace ProxyType.Api.Tests;

public sealed class FundRequestTests
{
    [Fact]
    public void CreateRequestRequiresAmountReferenceProofAndIdempotency()
    {
        var request = new FundRequestCreateRequest { Amount = 0, ExternalReference = "", IdempotencyKey = "short" };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        Assert.Contains(errors, item => item.MemberNames.Contains(nameof(FundRequestCreateRequest.Amount)));
        Assert.Contains(errors, item => item.MemberNames.Contains(nameof(FundRequestCreateRequest.ExternalReference)));
        Assert.Contains(errors, item => item.MemberNames.Contains(nameof(FundRequestCreateRequest.Proof)));
        Assert.Contains(errors, item => item.MemberNames.Contains(nameof(FundRequestCreateRequest.IdempotencyKey)));
    }

    [Fact]
    public void ReviewRequestOnlyAllowsApprovalOrRejection()
    {
        var request = new FundRequestReviewRequest { Decision = "PENDING" };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        Assert.Contains(errors, item => item.MemberNames.Contains(nameof(FundRequestReviewRequest.Decision)));
    }

    [Fact]
    public void ReviewRequestAcceptsApprovedDecision()
    {
        var request = new FundRequestReviewRequest { Decision = "APPROVED" };
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true));
    }
}
