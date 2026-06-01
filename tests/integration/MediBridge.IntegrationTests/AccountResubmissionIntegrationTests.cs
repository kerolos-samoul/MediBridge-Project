using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AccountResubmissionIntegrationTests
{
    [Fact]
    public async Task ValidResubmissionToken_UpdatesMetadataConsumesTokenReturnsPendingAndStillDeniesLogin()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var target = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var rejectResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new
        {
            Decision = "Reject",
            Reason = "Need updated license."
        });
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        using var rejectDocument = JsonDocument.Parse(await rejectResponse.Content.ReadAsStringAsync());
        var token = rejectDocument.RootElement.GetProperty("Data").GetProperty("ResubmissionToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = null;
        using var resubmitResponse = await client.PostAsJsonAsync("/api/auth/resubmit-registration", new
        {
            ResubmissionToken = token,
            Specialization = "Neurology",
            ExperienceYears = 6,
            Location = "Cairo",
            VerificationMetadata = new
            {
                DocumentType = "UpdatedLicense",
                OriginalFileName = "updated-license.pdf",
                ContentType = "application/pdf",
                SizeBytes = 2048,
                Reference = "updated-reference"
            }
        });

        Assert.Equal(HttpStatusCode.OK, resubmitResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Id == target.Id);
            var profile = await db.DoctorProfiles.SingleAsync(candidate => candidate.UserId == target.Id);
            var resubmissionToken = await db.AccountResubmissionTokens.SingleAsync(candidate => candidate.UserId == target.Id);
            var resubmission = await db.AccountResubmissions.SingleAsync(candidate => candidate.UserId == target.Id);

            Assert.Equal(AccountStatus.Pending, user.AccountStatus);
            Assert.Equal("Neurology", profile.Specialization);
            Assert.Equal("UpdatedLicense", profile.VerificationDocumentType);
            Assert.NotNull(resubmissionToken.ConsumedAtUtc);
            Assert.Contains("Neurology", resubmission.UpdatedProfileFields);
            Assert.Contains("updated-reference", resubmission.UpdatedVerificationMetadata);
        }

        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });
        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);

        using var replayResponse = await client.PostAsJsonAsync("/api/auth/resubmit-registration", new
        {
            ResubmissionToken = token,
            VerificationMetadata = Phase6IdentityTestHelpers.CreateVerificationMetadata("replay")
        });
        Assert.Equal(HttpStatusCode.Forbidden, replayResponse.StatusCode);
    }

    [Fact]
    public async Task ConcurrentResubmissionRequests_ConsumeTokenOnlyOnce()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var target = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var rejectResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new
        {
            Decision = "Reject",
            Reason = "Need updated license."
        });
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        using var rejectDocument = JsonDocument.Parse(await rejectResponse.Content.ReadAsStringAsync());
        var token = rejectDocument.RootElement.GetProperty("Data").GetProperty("ResubmissionToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = null;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8)
            .Select(async index =>
            {
                await ready.Task;
                using var response = await client.PostAsJsonAsync("/api/auth/resubmit-registration", new
                {
                    ResubmissionToken = token,
                    Specialization = $"Neurology {index}",
                    ExperienceYears = 6,
                    Location = "Cairo",
                    VerificationMetadata = new
                    {
                        DocumentType = "UpdatedLicense",
                        OriginalFileName = $"updated-license-{index}.pdf",
                        ContentType = "application/pdf",
                        SizeBytes = 2048,
                        Reference = $"updated-reference-{index}"
                    }
                });

                return response.StatusCode;
            })
            .ToArray();

        ready.SetResult();
        var statuses = await Task.WhenAll(tasks);

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(7, statuses.Count(status => status == HttpStatusCode.Forbidden));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await db.AccountResubmissions.CountAsync(candidate => candidate.UserId == target.Id));
        Assert.NotNull((await db.AccountResubmissionTokens.SingleAsync(candidate => candidate.UserId == target.Id)).ConsumedAtUtc);
    }
}
