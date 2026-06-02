using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase3MigrationSchemaTests
{
    [Fact]
    public async Task Phase3Migration_AppliesCleanlyAndPreservesPhase2IdentityAndRefreshSchema()
    {
        await using var factory = new WebAppFactory();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.EnsureDeletedAsync();

        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260531185506_Phase2IdentityApproval");

        var userId = Guid.NewGuid().ToString("N");
        await context.Roles.AddRangeAsync(
            new IdentityRole(nameof(UserRole.Admin)) { NormalizedName = nameof(UserRole.Admin).ToUpperInvariant() },
            new IdentityRole(nameof(UserRole.Doctor)) { NormalizedName = nameof(UserRole.Doctor).ToUpperInvariant() },
            new IdentityRole(nameof(UserRole.Company)) { NormalizedName = nameof(UserRole.Company).ToUpperInvariant() });
        await context.Users.AddAsync(new MediBridgeIdentityUser
        {
            Id = userId,
            UserName = "phase3-approved@medibridge.local",
            NormalizedUserName = "PHASE3-APPROVED@MEDIBRIDGE.LOCAL",
            Email = "phase3-approved@medibridge.local",
            NormalizedEmail = "PHASE3-APPROVED@MEDIBRIDGE.LOCAL",
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.RefreshCredentials.AddAsync(new RefreshCredential
        {
            Id = $"refresh-{Guid.NewGuid():N}",
            UserId = userId,
            TokenHash = $"token-{Guid.NewGuid():N}",
            FamilyId = $"family-{Guid.NewGuid():N}",
            ReplacedByTokenHash = $"replacement-{Guid.NewGuid():N}",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await context.SaveChangesAsync();

        await migrator.MigrateAsync();

        Assert.True(await context.Database.CanConnectAsync());
        Assert.Contains(await context.Roles.Select(role => role.Name).ToListAsync(), role => role == nameof(UserRole.Admin));
        Assert.Contains(await context.Roles.Select(role => role.Name).ToListAsync(), role => role == nameof(UserRole.Doctor));
        Assert.Contains(await context.Roles.Select(role => role.Name).ToListAsync(), role => role == nameof(UserRole.Company));
        Assert.True(await context.Users.AnyAsync(user => user.Id == userId && user.AccountStatus == AccountStatus.Approved));
        Assert.True(await context.RefreshCredentials.AnyAsync(credential => credential.UserId == userId && credential.ReplacedByTokenHash != null));
        Assert.True(context.Model.FindEntityType(typeof(MediBridge.Core.Entities.Campaigns.Campaign)) is not null);
        Assert.True(context.Model.FindEntityType(typeof(MediBridge.Core.Entities.Wallets.WalletLedgerEntry)) is not null);
        Assert.True(context.Model.FindEntityType(typeof(MediBridge.Core.Entities.Files.StoredFile)) is not null);
    }
}
