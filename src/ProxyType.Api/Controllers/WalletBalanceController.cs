using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Security;

namespace ProxyType.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/wallet/balance")]
public sealed class WalletBalanceController(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WalletBalanceResponse>> Get(CancellationToken cancellationToken)
    {
        var storedAccounts = await dbContext.LedgerAccounts.AsNoTracking()
            .Where(account => account.UserId == scopeService.UserId &&
                (account.Code.EndsWith(":WALLET") || account.Code.EndsWith(":AEPS")))
            .OrderBy(account => account.Code.EndsWith(":WALLET") ? 0 : 1)
            .Select(account => new
            {
                account.LedgerAccountId,
                Type = account.Code.EndsWith(":AEPS") ? "AEPS" : "MAIN",
                account.Currency,
                Balance = account.CurrentBalance,
                account.IsActive
            })
            .ToListAsync(cancellationToken);

        var accounts = new[] { "MAIN", "AEPS" }
            .Select(type =>
            {
                var stored = storedAccounts.FirstOrDefault(account => account.Type == type);
                return stored is null
                    ? new WalletBalanceAccount(null, type, type == "MAIN" ? "Main Wallet" : "AEPS Wallet", "INR", 0, false)
                    : new WalletBalanceAccount(stored.LedgerAccountId, type,
                        type == "MAIN" ? "Main Wallet" : "AEPS Wallet",
                        stored.Currency, stored.Balance, stored.IsActive);
            })
            .ToArray();

        var accountIds = storedAccounts.Select(account => account.LedgerAccountId).ToArray();
        WalletBalanceActivity[] recentActivity = accountIds.Length == 0
            ? []
            : await dbContext.JournalEntries.AsNoTracking()
                .Where(entry => accountIds.Contains(entry.LedgerAccountId))
                .Join(dbContext.LedgerAccounts.AsNoTracking(), entry => entry.LedgerAccountId,
                    account => account.LedgerAccountId, (entry, account) => new { entry, account })
                .Join(dbContext.JournalTransactions.AsNoTracking(), row => row.entry.JournalTransactionId,
                    journal => journal.JournalTransactionId, (row, journal) => new { row.entry, row.account, journal })
                .Where(row => row.journal.Status == "POSTED" && row.journal.PostedAtUtc != null)
                .OrderByDescending(row => row.journal.PostedAtUtc)
                .ThenByDescending(row => row.entry.JournalEntryId)
                .Take(25)
                .Select(row => new WalletBalanceActivity(
                    row.entry.JournalEntryId,
                    row.account.Code.EndsWith(":AEPS") ? "AEPS" : "MAIN",
                    row.journal.Reference,
                    row.journal.Description,
                    row.entry.Memo,
                    row.entry.DebitAmount + row.entry.CreditAmount,
                    row.account.NormalBalance == "D"
                        ? row.entry.DebitAmount - row.entry.CreditAmount
                        : row.entry.CreditAmount - row.entry.DebitAmount,
                    row.journal.PostedAtUtc!.Value))
                .ToArrayAsync(cancellationToken);

        return Ok(new WalletBalanceResponse(
            accounts.Sum(account => account.Balance),
            accounts.FirstOrDefault()?.Currency ?? "INR",
            accounts,
            recentActivity,
            DateTime.UtcNow));
    }
}
