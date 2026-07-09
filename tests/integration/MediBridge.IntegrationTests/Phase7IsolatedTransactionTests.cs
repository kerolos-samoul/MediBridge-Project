using System.Data.Common;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7IsolatedTransactionTests
{
    [Fact]
    public async Task ExpirySuccess_ClearsAllTrackedCandidateState()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 50m);
        var now = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
        await Phase7ExpiryEligibilityTests.SeedDeliveryAsync(factory.Services, seed, "tracking-success", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-1));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var service = new DeliveryExpiryService(unitOfWork, new EgyptBusinessClock(new FixedTimeProvider(now)));

        var result = await service.RunAsync();

        Assert.Equal(1, result.ExpiredCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ExpiryFailure_ClearsAllTrackedCandidateState()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 25m);
        var now = new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
        await Phase7ExpiryEligibilityTests.SeedDeliveryAsync(factory.Services, seed, "tracking-failure", new DateOnly(2026, 7, 1), DeliveryStatus.Active, ReservationStatus.Reserved, now.AddDays(-1));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();
        var service = new DeliveryExpiryService(unitOfWork, new EgyptBusinessClock(new FixedTimeProvider(now)));

        var result = await service.RunAsync();

        Assert.Equal(1, result.FailedCount);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task TransactionFailure_WhenRollbackAlsoFails_ClearsTrackingAndPreservesBothFailures()
    {
        await using var factory = new RollbackFailureWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 50m);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        var exception = await Assert.ThrowsAsync<AggregateException>(() => unitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
        {
            await unitOfWork.Wallets.StageAvailableBalanceChangeAsync(seed.WalletId, 10m, DateTime.UtcNow, cancellationToken);
            throw new CandidateFailureException("candidate failed");
        }));

        Assert.Contains(exception.InnerExceptions, item => item is CandidateFailureException);
        Assert.Contains(exception.InnerExceptions, item => item is RollbackFailureException);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Cancellation_WhenRollbackAlsoFails_RemainsCancellationAndPreservesBothFailures()
    {
        await using var factory = new RollbackFailureWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 50m);
        using var cancellation = new CancellationTokenSource();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => unitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            await unitOfWork.Wallets.StageAvailableBalanceChangeAsync(seed.WalletId, 10m, DateTime.UtcNow, transactionCancellationToken);
            cancellation.Cancel();
            throw new OperationCanceledException("candidate cancelled", cancellation.Token);
        }, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        var combinedFailure = Assert.IsType<AggregateException>(exception.InnerException);
        Assert.Contains(combinedFailure.InnerExceptions, item => item is OperationCanceledException);
        Assert.Contains(combinedFailure.InnerExceptions, item => item is RollbackFailureException);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task OrdinarySuccessfulTransaction_PreservesExistingTrackingSemantics()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await Phase7ExpiryEligibilityTests.SeedOwnerGraphAsync(factory.Services, reservedBalance: 50m);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync(async cancellationToken =>
        {
            var wallet = await unitOfWork.Wallets.FindActiveWalletForUpdateAsync(seed.WalletId, cancellationToken);
            Assert.NotNull(wallet);
        });

        Assert.Contains(context.ChangeTracker.Entries(), entry => entry.Entity is MediBridge.Core.Entities.Wallets.Wallet);
    }

    private sealed class RollbackFailureWebAppFactory : ConfiguredWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) =>
            {
                services.RemoveAll<DbContextOptions<MediBridgeDbContext>>();
                services.AddDbContext<MediBridgeDbContext>(options =>
                    options
                        .UseSqlServer(context.Configuration.GetConnectionString("DefaultConnection"))
                        .AddInterceptors(new RollbackFailureInterceptor()));
            });
        }
    }

    private sealed class RollbackFailureInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            throw new RollbackFailureException("rollback failed");
        }
    }

    private sealed class CandidateFailureException : Exception
    {
        public CandidateFailureException(string message) : base(message)
        {
        }
    }

    private sealed class RollbackFailureException : Exception
    {
        public RollbackFailureException(string message) : base(message)
        {
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedTimeProvider(DateTime now)
        {
            this.now = new DateTimeOffset(now);
        }

        public override DateTimeOffset GetUtcNow() => now;
    }
}
