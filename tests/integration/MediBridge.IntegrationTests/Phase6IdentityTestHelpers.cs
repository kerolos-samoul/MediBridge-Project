using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

internal static class Phase6IdentityTestHelpers
{
    public static async Task<string> RegisterDoctorAsync(HttpClient client, string? email = null)
    {
        var doctorEmail = email ?? $"doctor-{Guid.NewGuid():N}@example.com";
        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", new
        {
            Email = doctorEmail,
            Password = "Password1!",
            PhoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            Specialization = "Cardiology",
            ExperienceYears = 5,
            Location = "Lagos",
            VerificationMetadata = CreateVerificationMetadata()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return doctorEmail;
    }

    public static async Task<string> RegisterCompanyAsync(HttpClient client, string? email = null)
    {
        var companyEmail = email ?? $"company-{Guid.NewGuid():N}@example.com";
        using var response = await client.PostAsJsonAsync("/api/auth/register-company", new
        {
            Email = companyEmail,
            Password = "Password1!",
            PhoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            CompanyName = "Acme Pharma",
            LicenseNumber = $"LIC-{Guid.NewGuid():N}",
            ContactName = "Casey Admin",
            VerificationMetadata = CreateVerificationMetadata()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return companyEmail;
    }

    public static object CreateVerificationMetadata(string referencePrefix = "ref")
    {
        return new
        {
            DocumentType = "License",
            OriginalFileName = "license.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            Reference = $"{referencePrefix}-{Guid.NewGuid():N}"
        };
    }

    public static async Task<MediBridgeIdentityUser> CreateAdminAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        var admin = new MediBridgeIdentityUser
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            LastStatusChangedAtUtc = now
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }

    public static async Task<MediBridgeIdentityUser> FindUserByEmailAsync(IServiceProvider services, string email)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await db.Users.SingleAsync(candidate => candidate.Email == email);
    }

    public static async Task SetStatusAsync(IServiceProvider services, string email, AccountStatus status, bool markEmailVerified = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var now = DateTime.UtcNow;
        user.AccountStatus = status;
        user.ApprovedAtUtc = status == AccountStatus.Approved ? now : null;
        user.LastStatusChangedAtUtc = now;
        if (status == AccountStatus.Approved && markEmailVerified)
        {
            user.EmailVerified = true;
        }

        await db.SaveChangesAsync();
    }

    public static async Task<string> LoginAndGetRefreshTokenAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("Data").GetProperty("RefreshToken").GetString() ?? string.Empty;
    }
}
