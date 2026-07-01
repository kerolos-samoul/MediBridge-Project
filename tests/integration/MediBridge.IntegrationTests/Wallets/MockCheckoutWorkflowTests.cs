using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Wallets;

public sealed class MockCheckoutWorkflowTests
{
    [Fact]
    public async Task MockCheckout_CreditsWalletAndRecordsPaymentTransactionLedgerAudit()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
        {
            Content = JsonContent.Create(new { Amount = 75m, Currency = "EGP" })
        };
        request.Headers.Add("Idempotency-Key", $"ledger-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paymentId = document.RootElement.GetProperty("Data").GetProperty("paymentId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(paymentId));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var payment = await context.MockPaymentTransactions.AsNoTracking().SingleAsync(candidate => candidate.PaymentId == paymentId);
        Assert.Equal(ids.CompanyProfileId, payment.CompanyId);
        Assert.Equal(75m, payment.Amount);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(0m, payment.WalletBalanceBefore);
        Assert.Equal(75m, payment.WalletBalanceAfter);
        Assert.NotNull(payment.AuditEventId);

        var wallet = await context.Wallets.AsNoTracking().SingleAsync(wallet => wallet.Id == payment.WalletId);
        Assert.Equal(75m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);

        var transaction = await context.WalletTransactions.AsNoTracking().SingleAsync(transaction => transaction.Id == payment.WalletTransactionId);
        Assert.Equal(WalletTransactionType.TopUp, transaction.OperationType);
        Assert.Equal(75m, transaction.Amount);

        var ledger = await context.WalletLedgerEntries.AsNoTracking().SingleAsync(entry => entry.WalletTransactionId == transaction.Id);
        Assert.Equal(WalletLedgerEntryDirection.Credit, ledger.Direction);
        Assert.Equal(WalletBalanceType.Available, ledger.BalanceType);
        Assert.Equal(75m, ledger.Amount);

        var auditEvent = await context.AuditEvents.AsNoTracking().SingleAsync(audit => audit.Id == payment.AuditEventId);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(AuditTargetType.WalletTransaction, auditEvent.TargetType);
        Assert.Equal(transaction.Id, auditEvent.TargetId);
    }
}
