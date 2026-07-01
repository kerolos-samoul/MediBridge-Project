using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Identity;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class CompanyRegistrationIntegrationTests
{
    [Fact]
    public async Task RegisterCompany_CreatesPendingUserAndCompanyProfile()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"company-{Guid.NewGuid():N}@example.com";
        var phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}";
        var licenseNumber = $"LIC-{Guid.NewGuid():N}";
        var request = CreateCompanyRequest(email, phoneNumber, licenseNumber);

        using var response = await client.PostAsJsonAsync("/api/auth/register-company", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var profile = await db.CompanyProfiles.SingleAsync(candidate => candidate.UserId == user.Id);

        Assert.Equal("Pending", user.AccountStatus.ToString());
        Assert.Equal("Company", user.Role.ToString());
        Assert.False(user.IsDeleted);
        Assert.NotNull(user.PasswordHash);

        Assert.Equal("Acme Pharma", profile.CompanyName);
        Assert.Equal(licenseNumber, profile.LicenseNumber);
        Assert.Equal("Jane Doe", profile.ContactName);
        Assert.Equal("License", profile.VerificationDocumentType);
        Assert.Equal("company-license.pdf", profile.VerificationOriginalFileName);
        Assert.Equal("application/pdf", profile.VerificationContentType);
        Assert.Equal(2048, profile.VerificationSizeBytes);
        Assert.StartsWith("ref-", profile.VerificationReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterCompany_WithDuplicateLicense_ReturnsConflictAndKeepsSingleProfile()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var licenseNumber = $"LIC-{Guid.NewGuid():N}";
        var request = CreateCompanyRequest($"company-{Guid.NewGuid():N}@example.com", $"555{Random.Shared.Next(1000000, 9999999)}", licenseNumber);

        using var firstResponse = await client.PostAsJsonAsync("/api/auth/register-company", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/auth/register-company", request);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await db.CompanyProfiles.CountAsync(profile => profile.LicenseNumber == licenseNumber));
    }

    [Fact]
    public async Task IdentityRepository_DuplicateEmailConflict_DoesNotExposeInfrastructureException()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IIdentityUnitOfWork>();
        var email = $"company-{Guid.NewGuid():N}@example.com";

        await unitOfWork.ExecuteInTransactionAsync(
            cancellationToken => unitOfWork.Users.AddAsync(CreateCompanyUser(email), "Password1!", cancellationToken));

        var exception = await Record.ExceptionAsync(() => unitOfWork.ExecuteInTransactionAsync(
            cancellationToken => unitOfWork.Users.AddAsync(CreateCompanyUser(email), "Password1!", cancellationToken)));

        Assert.IsType<IdentityRecordConflictException>(exception);
    }

    private static ApplicationUser CreateCompanyUser(string email)
    {
        var now = DateTime.UtcNow;
        return new ApplicationUser
        {
            Email = email,
            Role = UserRole.Company,
            AccountStatus = AccountStatus.Pending,
            CreatedAtUtc = now,
            LastStatusChangedAtUtc = now
        };
    }

    private static object CreateCompanyRequest(string email, string phoneNumber, string licenseNumber)
    {
        return new
        {
            Email = email,
            Password = "Password1!",
            PhoneNumber = phoneNumber,
            CompanyName = "Acme Pharma",
            LicenseNumber = licenseNumber,
            ContactName = "Jane Doe",
            VerificationMetadata = new
            {
                DocumentType = "License",
                OriginalFileName = "company-license.pdf",
                ContentType = "application/pdf",
                SizeBytes = 2048,
                Reference = $"ref-{Guid.NewGuid():N}"
            }
        };
    }
}
