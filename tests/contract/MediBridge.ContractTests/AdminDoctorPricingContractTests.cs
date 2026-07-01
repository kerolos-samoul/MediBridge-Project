using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class AdminDoctorPricingContractTests
{
    [Fact]
    public async Task PutAdminDoctorPrice_ReturnsPriceEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAdminAndDoctorAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.PutAsync(
            $"/api/admin/doctors/{seed.DoctorId}/price",
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                PricePerMessage = 75.25m,
                Reason = "Contract rate"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal(seed.DoctorId, data.GetProperty("doctorId").GetString());
        Assert.Equal(75.25m, data.GetProperty("pricePerMessage").GetDecimal());
        Assert.Equal("EGP", data.GetProperty("currency").GetString());
    }

    private static async Task<PricingSeed> SeedAdminAndDoctorAsync(ContractWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var admin = CreateUser($"pricing-admin-{suffix}@example.test", UserRole.Admin);
        var doctorUser = CreateUser($"pricing-doctor-{suffix}@example.test", UserRole.Doctor);
        var doctor = new DoctorProfile
        {
            Id = $"doctor-{suffix}",
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 8,
            Location = "Cairo",
            PricePerMessage = 50m,
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}"
        };

        await context.Users.AddRangeAsync(admin, doctorUser);
        await context.DoctorProfiles.AddAsync(doctor);
        await context.SaveChangesAsync();
        return new PricingSeed(admin.Id, doctor.Id);
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role)
    {
        return new MediBridgeIdentityUser
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
    }

    private sealed record PricingSeed(string AdminUserId, string DoctorId);
}
