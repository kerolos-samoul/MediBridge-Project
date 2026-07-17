using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class AdminPlatformFeePolicyContractTests
{
    [Fact]
    public async Task PutPlatformFeePolicy_WithAdminToken_ReturnsStandardEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedUserAsync(factory, UserRole.Admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", adminUserId));

        using var response = await client.PutAsync(
            "/api/admin/platform-fee-policy",
            Phase5ContractTestHelpers.CreateJsonContent(new { FeePercent = 12.5m, Reason = "Contract policy update" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal(12.5m, data.GetProperty("FeePercent").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("PolicyId").GetString()));
    }

    [Theory]
    [InlineData(null, "Valid reason")]
    [InlineData("0", "Valid reason")]
    [InlineData("-1", "Valid reason")]
    [InlineData("100.01", "Valid reason")]
    [InlineData("20", "")]
    public async Task PutPlatformFeePolicy_WithInvalidRequest_ReturnsValidationEnvelope(string? feePercent, string reason)
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminUserId = await SeedUserAsync(factory, UserRole.Admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", adminUserId));

        using var response = await client.PutAsync(
            "/api/admin/platform-fee-policy",
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                FeePercent = feePercent is null ? (decimal?)null : decimal.Parse(feePercent),
                Reason = reason
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);
    }

    [Fact]
    public async Task PutPlatformFeePolicy_RequiresAdmin()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var companyUserId = await SeedUserAsync(factory, UserRole.Company);

        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.PutAsync(
            "/api/admin/platform-fee-policy",
            Phase5ContractTestHelpers.CreateJsonContent(new { FeePercent = 20m, Reason = "Unauthorized" }));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var companyClient = factory.CreateClient();
        companyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Company", companyUserId));
        using var forbidden = await companyClient.PutAsync(
            "/api/admin/platform-fee-policy",
            Phase5ContractTestHelpers.CreateJsonContent(new { FeePercent = 20m, Reason = "Forbidden" }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static async Task<string> SeedUserAsync(ContractWebAppFactory factory, UserRole role)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var user = new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = role,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();
        return user.Id;
    }
}
