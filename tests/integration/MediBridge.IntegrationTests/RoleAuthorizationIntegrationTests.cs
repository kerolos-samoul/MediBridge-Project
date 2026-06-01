using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class RoleAuthorizationIntegrationTests
{
    [Fact]
    public async Task AdminEndpoints_DenyAnonymousRequests()
    {
        await using var factory = new RoleAuthorizationWebAppFactory();
        using var client = factory.CreateClient();

        using var listResponse = await client.GetAsync("/api/admin/pending-accounts");
        using var decisionResponse = await client.PutAsJsonAsync("/api/admin/accounts/target-user/decision", new
        {
            Decision = "Approve"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, decisionResponse.StatusCode);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Company")]
    public async Task AdminEndpoints_DenyDoctorAndCompanyTokens(string role)
    {
        await using var factory = new RoleAuthorizationWebAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtFactory.CreateToken(role));

        using var listResponse = await client.GetAsync("/api/admin/pending-accounts");
        using var decisionResponse = await client.PutAsJsonAsync("/api/admin/accounts/target-user/decision", new
        {
            Decision = "Approve"
        });

        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, decisionResponse.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_AllowAdminTokens()
    {
        await using var factory = new RoleAuthorizationWebAppFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtFactory.CreateToken("Admin"));

        using var listResponse = await client.GetAsync("/api/admin/pending-accounts");
        using var decisionResponse = await client.PutAsJsonAsync("/api/admin/accounts/target-user/decision", new
        {
            Decision = "Approve"
        });

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);
    }

    private sealed class RoleAuthorizationWebAppFactory : ConfiguredWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAdminAccountService>();
                services.AddScoped<IAdminAccountService, StubAdminAccountService>();
            });
        }
    }

    private sealed class StubAdminAccountService : IAdminAccountService
    {
        public Task<PendingAccountPageDto> ListPendingAccountsAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PendingAccountPageDto
            {
                Items = Array.Empty<PendingAccountDto>(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = 0
            });
        }

        public Task<AccountDecisionResultDto> ApplyDecisionAsync(string adminUserId, string targetUserId, AdminAccountDecisionRequestDto request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AccountDecisionResultDto
            {
                UserId = targetUserId,
                ResultingAccountStatus = AccountStatus.Approved
            });
        }
    }
}
