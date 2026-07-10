using System.Net;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8SettlementAtomicityTests
{
    public static IEnumerable<object[]> ForcedFailureTriggers()
    {
        yield return
        [
            "TR_Phase8_FailAfterDeliveryUpdate",
            """
            CREATE TRIGGER [TR_Phase8_FailAfterDeliveryUpdate] ON [DoctorAdDeliveries] AFTER UPDATE AS
            BEGIN THROW 52010, 'forced phase8 delivery update failure', 1; END
            """
        ];
        yield return
        [
            "TR_Phase8_FailAfterCompanyWalletDebit",
            """
            CREATE TRIGGER [TR_Phase8_FailAfterCompanyWalletDebit] ON [Wallets] AFTER UPDATE AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE [OwnerType] = 2)
                    THROW 52011, 'forced phase8 company wallet failure', 1;
            END
            """
        ];
        yield return
        [
            "TR_Phase8_FailAfterChargeTransaction",
            """
            CREATE TRIGGER [TR_Phase8_FailAfterChargeTransaction] ON [WalletTransactions] AFTER INSERT AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE [OperationType] = 20)
                    THROW 52012, 'forced phase8 charge transaction failure', 1;
            END
            """
        ];
    }

    [Theory]
    [MemberData(nameof(ForcedFailureTriggers))]
    public async Task Interact_ForcedFailureRollsBackEverySettlementMutation(string triggerName, string triggerSql)
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, $"rollback-{triggerName}");
        var before = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);

        await ExecuteSqlAsync(factory, triggerSql);
        try
        {
            using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, seed.DoctorUserId);
            using var request = Phase8InteractionTestHelpers.CreateInteractRequest(
                seed.DeliveryId,
                $"rollback-key-{Guid.NewGuid():N}",
                "Accept");
            using var response = await client.SendAsync(request);
            await Phase8InteractionTestHelpers.AssertSafeEmptyEnvelopeAsync(response, HttpStatusCode.InternalServerError);
        }
        finally
        {
            await ExecuteSqlAsync(factory, $"DROP TRIGGER [{triggerName}]");
        }

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Equal(before, after);
    }

    private static async Task ExecuteSqlAsync(Phase8InteractionTestHelpers.FixedClockFactory factory, string sql)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.ExecuteSqlRawAsync(sql);
    }
}
