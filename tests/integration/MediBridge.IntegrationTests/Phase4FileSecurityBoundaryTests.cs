using FluentAssertions;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Services.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase4FileSecurityBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Core_project_has_no_cloudinary_entity_framework_or_http_references()
    {
        var coreFiles = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "MediBridge.Core"), "*.cs", SearchOption.AllDirectories);

        var forbiddenHits = coreFiles
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => new { file, line, index }))
            .Where(hit =>
                hit.line.Contains("CloudinaryDotNet", StringComparison.Ordinal) ||
                hit.line.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                hit.line.Contains("Microsoft.AspNetCore.Http", StringComparison.Ordinal) ||
                hit.line.Contains("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal))
            .Select(hit => $"{Path.GetRelativePath(RepositoryRoot, hit.file)}:{hit.index + 1}:{hit.line.Trim()}")
            .ToArray();

        forbiddenHits.Should().BeEmpty();
    }

    [Fact]
    public void Controllers_have_no_storage_provider_or_db_context_references()
    {
        var controllerFiles = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "MediBridge.APIs", "Controllers"), "*.cs", SearchOption.AllDirectories);

        var forbiddenHits = controllerFiles
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => new { file, line, index }))
            .Where(hit =>
                hit.line.Contains("Cloudinary", StringComparison.OrdinalIgnoreCase) ||
                hit.line.Contains("IFileStorageProvider", StringComparison.Ordinal) ||
                hit.line.Contains("MediBridgeDbContext", StringComparison.Ordinal))
            .Select(hit => $"{Path.GetRelativePath(RepositoryRoot, hit.file)}:{hit.index + 1}:{hit.line.Trim()}")
            .ToArray();

        forbiddenHits.Should().BeEmpty();
    }

    [Fact]
    public void NonDevelopment_tracked_config_does_not_contain_cloudinary_credential_material()
    {
        var configFiles = Directory.EnumerateFiles(RepositoryRoot, "appsettings*.json", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !file.EndsWith("appsettings.Development.json", StringComparison.OrdinalIgnoreCase));

        var forbiddenHits = configFiles
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => new { file, line, index }))
            .Where(hit =>
                hit.line.Contains("cloudinary://", StringComparison.OrdinalIgnoreCase) ||
                hit.line.Contains("api_secret", StringComparison.OrdinalIgnoreCase) ||
                hit.line.Contains("CLOUDINARY_URL", StringComparison.Ordinal))
            .Select(hit => $"{Path.GetRelativePath(RepositoryRoot, hit.file)}:{hit.index + 1}:{hit.line.Trim()}")
            .ToArray();

        forbiddenHits.Should().BeEmpty();
    }

    [Fact]
    public void Cloudinary_secret_validation_fails_clearly_when_uploads_enabled_and_secret_missing()
    {
        var options = new CloudinaryStorageOptions();

        var errors = options.Validate(uploadEnabled: true, cloudinaryUrl: string.Empty);

        errors.Should().ContainSingle(error =>
            error.Contains(CloudinaryStorageOptions.SecretEnvironmentVariableName, StringComparison.Ordinal) &&
            error.Contains("uploads are enabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task File_access_grant_audits_never_persist_signed_url_token_signature_or_credentials()
    {
        await using var factory = new AuditProbeFactory();
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var user = new MediBridgeIdentityUser
        {
            Id = $"user-{Guid.NewGuid():N}",
            Email = $"audit-{Guid.NewGuid():N}@example.com",
            UserName = $"audit-{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"AUDIT-{Guid.NewGuid():N}@EXAMPLE.COM",
            NormalizedUserName = $"AUDIT-{Guid.NewGuid():N}@EXAMPLE.COM",
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            LastStatusChangedAtUtc = now
        };
        var issuedFileId = Guid.NewGuid().ToString("N");
        var deniedFileId = Guid.NewGuid().ToString("N");
        db.Users.Add(user);
        db.StoredFiles.Add(CreateAuditProbeFile(issuedFileId));
        db.StoredFiles.Add(CreateAuditProbeFile(deniedFileId));
        db.FileAccessGrantAudits.Add(new FileAccessGrantAudit
        {
            StoredFileId = issuedFileId,
            RequestedByUserId = user.Id,
            RequesterRole = UserRole.Doctor.ToString(),
            Outcome = FileAccessGrantOutcome.Issued,
            Reason = "Access grant issued.",
            ExpiresAtUtc = now.AddMinutes(10)
        });
        db.FileAccessGrantAudits.Add(new FileAccessGrantAudit
        {
            StoredFileId = deniedFileId,
            RequestedByUserId = user.Id,
            RequesterRole = UserRole.Doctor.ToString(),
            Outcome = FileAccessGrantOutcome.Denied,
            Reason = "Access denied.",
            ExpiresAtUtc = null
        });
        await db.SaveChangesAsync();

        var persistedText = string.Join(" ", await db.FileAccessGrantAudits
            .Select(audit => new[] { audit.RequestedByUserId, audit.RequesterRole, audit.Reason ?? string.Empty })
            .ToListAsync());

        persistedText = persistedText.ToLowerInvariant();
        persistedText.Should().NotContain("http");
        persistedText.Should().NotContain("signature");
        persistedText.Should().NotContain("token");
        persistedText.Should().NotContain("cloudinary://");
        persistedText.Should().NotContain("api_secret");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediBridge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static StoredFile CreateAuditProbeFile(string fileId)
    {
        return new StoredFile
        {
            Id = fileId,
            OwnerType = StoredFileOwnerType.Doctor,
            OwnerId = $"doctor-{Guid.NewGuid():N}",
            Purpose = StoredFilePurpose.VerificationDocument,
            OriginalFileName = "license.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            StorageKey = $"private/{fileId}.pdf",
            StorageProvider = "FakeStorage",
            StorageResourceType = StoredFileStorageResourceType.Raw,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Pending,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private sealed class AuditProbeFactory : TestHost.ConfiguredWebAppFactory
    {
    }
}
