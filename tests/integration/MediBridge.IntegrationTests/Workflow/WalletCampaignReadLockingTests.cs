using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class WalletCampaignReadLockingTests
{
    [Fact]
    public async Task WalletCampaignReadEndpoints_DoNotAcquireUpdateLocks()
    {
        await using var factory = new SqlCaptureWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var draftResponse = await company.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Read-lock regression", Description = "Read endpoints must not acquire update locks." });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        using var draftDocument = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        var campaignId = draftDocument.RootElement.GetProperty("Data").GetProperty("campaignId").GetString()!;
        var interceptor = factory.Services.GetRequiredService<SqlCaptureInterceptor>();
        interceptor.Clear();

        using var preview = await company.GetAsync($"/api/company/campaigns/{campaignId}/target-preview");
        using var summary = await company.GetAsync($"/api/company/campaigns/{campaignId}/queue-summary");
        using var queue = await admin.GetAsync($"/api/admin/campaigns/{campaignId}/queue");

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
        Assert.NotEmpty(interceptor.Commands);
        Assert.Contains(
            interceptor.Commands,
            command => command.Contains("Users", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            interceptor.Commands,
            command => command.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase)
                || command.Contains("HOLDLOCK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WalletCampaignMutationEndpoints_RetainUpdateLocks()
    {
        await using var factory = new SqlCaptureWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        var interceptor = factory.Services.GetRequiredService<SqlCaptureInterceptor>();
        interceptor.Clear();

        using var draftResponse = await company.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Mutation-lock regression", Description = "Mutation endpoints retain actor update locks." });

        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        Assert.Contains(interceptor.Commands, ContainsUpdateLock);
        using var draftDocument = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        var campaignId = draftDocument.RootElement.GetProperty("Data").GetProperty("campaignId").GetString()!;

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "mutation-lock.png");
        using var uploadResponse = await company.PostAsync($"/api/company/campaigns/{campaignId}/assets", form);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        using var uploadDocument = JsonDocument.Parse(await uploadResponse.Content.ReadAsStringAsync());
        var assetId = uploadDocument.RootElement.GetProperty("Data").GetProperty("assetId").GetString()!;
        interceptor.Clear();

        using var reviewResponse = await admin.PostAsJsonAsync(
            $"/api/admin/campaign-assets/{assetId}/review",
            new { Decision = "Approved", Reason = (string?)null });

        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        Assert.Contains(interceptor.Commands, ContainsUpdateLock);
    }

    [Fact]
    public async Task WalletCampaignQueueSummary_UsesAggregateQueryWithoutDoctorRows()
    {
        await using var factory = new SqlCaptureWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);
        using var draftResponse = await company.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Aggregate-query regression", Description = "Company summaries must not load doctor rows." });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        using var draftDocument = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        var campaignId = draftDocument.RootElement.GetProperty("Data").GetProperty("campaignId").GetString()!;
        var interceptor = factory.Services.GetRequiredService<SqlCaptureInterceptor>();
        interceptor.Clear();

        using var summary = await company.GetAsync($"/api/company/campaigns/{campaignId}/queue-summary");

        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        var queueCommand = Assert.Single(
            interceptor.Commands,
            command => command.Contains("DoctorMessageQueues", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("GROUP BY", queueCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DoctorId", queueCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsUpdateLock(string command)
        => command.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase)
            && command.Contains("HOLDLOCK", StringComparison.OrdinalIgnoreCase);

    private sealed class SqlCaptureWebAppFactory : ConfiguredWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<SqlCaptureInterceptor>();
                services.RemoveAll<DbContextOptions<MediBridgeDbContext>>();
                services.AddDbContext<MediBridgeDbContext>((serviceProvider, options) =>
                {
                    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
                    options.AddInterceptors(serviceProvider.GetRequiredService<SqlCaptureInterceptor>());
                });
            });
        }
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> commands = new();

        public IReadOnlyCollection<string> Commands => commands.ToArray();

        public void Clear()
        {
            while (commands.TryDequeue(out _))
            {
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
