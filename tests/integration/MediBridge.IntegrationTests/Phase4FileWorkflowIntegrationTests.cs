using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase4FileWorkflowIntegrationTests
{
    [Fact]
    public async Task DoctorVerificationUpload_WithValidDocument_CreatesPrivatePendingMetadata()
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", user.Id));

        using var response = await client.PostAsync("/api/files/verification-documents", CreateMultipart("license.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, storageProvider.UploadCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        var fileId = data.GetProperty("Id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(fileId));
        Assert.Equal("VerificationDocument", data.GetProperty("Purpose").GetString());
        Assert.Equal("Doctor", data.GetProperty("OwnerType").GetString());
        Assert.Equal("Pending", data.GetProperty("ReviewStatus").GetString());
        Assert.Equal("Stored", data.GetProperty("UploadStatus").GetString());
        AssertDtoHasNoPrivateStorageFields(data);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var storedFile = await db.StoredFiles.SingleAsync(file => file.Id == fileId);
        Assert.Equal(StoredFileOwnerType.Doctor, storedFile.OwnerType);
        Assert.Equal(StoredFilePurpose.VerificationDocument, storedFile.Purpose);
        Assert.Equal(StoredFileVisibility.Private, storedFile.Visibility);
        Assert.Equal(StoredFileUploadStatus.Stored, storedFile.UploadStatus);
        Assert.Equal(StoredFileReviewStatus.Pending, storedFile.ReviewStatus);
        Assert.NotEqual(user.Id, storedFile.OwnerId);
        Assert.NotEqual("license.pdf", storedFile.StorageKey);
        Assert.StartsWith("verification/", storedFile.StorageKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompanyVerificationUpload_WithValidDocument_CreatesPrivatePendingMetadata()
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterCompanyAsync(client);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", user.Id));

        using var response = await client.PostAsync("/api/files/verification-documents", CreateMultipart("license.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 2048));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, storageProvider.UploadCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        var fileId = data.GetProperty("Id").GetString();
        Assert.Equal("Company", data.GetProperty("OwnerType").GetString());
        Assert.Equal("VerificationDocument", data.GetProperty("Purpose").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var storedFile = await db.StoredFiles.SingleAsync(file => file.Id == fileId);
        Assert.Equal(StoredFileOwnerType.Company, storedFile.OwnerType);
        Assert.Equal(StoredFileUploadStatus.Stored, storedFile.UploadStatus);
        Assert.Equal(StoredFileReviewStatus.Pending, storedFile.ReviewStatus);
    }

    [Theory]
    [InlineData("empty file", "license.pdf", "application/pdf", 0)]
    [InlineData("unsafe name", "../license.pdf", "application/pdf", 1024)]
    [InlineData("unsupported mime", "license.pdf", "text/plain", 1024)]
    [InlineData("unsupported extension", "license.exe", "application/pdf", 1024)]
    [InlineData("mismatched pair", "license.pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1024)]
    [InlineData("oversize document", "license.pdf", "application/pdf", 10 * 1024 * 1024 + 1)]
    public async Task VerificationUpload_WithInvalidFile_IsRejectedBeforeProviderUpload(string _, string fileName, string contentType, int sizeBytes)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", user.Id));

        using var response = await client.PostAsync("/api/files/verification-documents", CreateMultipart(fileName, contentType, sizeBytes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, storageProvider.UploadCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task VerificationUpload_AttemptTwentyOneWithinOneHour_ReturnsRateLimitEnvelope()
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var user = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", user.Id));

        for (var index = 0; index < 20; index++)
        {
            using var accepted = await client.PostAsync("/api/files/verification-documents", CreateMultipart($"license-{index}.pdf", "application/pdf", 1024));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using var rejected = await client.PostAsync("/api/files/verification-documents", CreateMultipart("license-21.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(429, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal("Too many requests.", document.RootElement.GetProperty("Message").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task VerificationUpload_RateLimit_IsPartitionedByAuthenticatedUser()
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var firstEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var secondEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var firstUser = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, firstEmail);
        var secondUser = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, secondEmail);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", firstUser.Id));
        for (var index = 0; index < 20; index++)
        {
            using var accepted = await client.PostAsync("/api/files/verification-documents", CreateMultipart($"first-{index}.pdf", "application/pdf", 1024));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", secondUser.Id));
        using var secondUserResponse = await client.PostAsync("/api/files/verification-documents", CreateMultipart("second-user.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.Created, secondUserResponse.StatusCode);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Company")]
    [InlineData("Admin")]
    public async Task PrivateAccessGrant_WithAuthorizedActor_ReturnsTenMinuteGrantAndPersistsIssuedAudit(string role)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = role == "Company"
            ? await Phase6IdentityTestHelpers.RegisterCompanyAsync(client)
            : await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var actorUserId = owner.Id;
        var actorRole = role;
        if (role == "Admin")
        {
            var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
            actorUserId = admin.Id;
        }

        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, role == "Company" ? StoredFileOwnerType.Company : StoredFileOwnerType.Doctor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(actorRole, actorUserId));

        using var response = await client.PostAsync($"/api/files/{fileId}/access", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, storageProvider.AccessGrantCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(200, document.RootElement.GetProperty("Code").GetInt32());
        var data = document.RootElement.GetProperty("Data");
        Assert.StartsWith("https://files.example.test/", data.GetProperty("Url").GetString(), StringComparison.Ordinal);
        var expiresAt = data.GetProperty("ExpiresAtUtc").GetDateTime();
        Assert.InRange(expiresAt, DateTime.UtcNow.AddMinutes(9), DateTime.UtcNow.AddMinutes(11));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var audit = await db.FileAccessGrantAudits.SingleAsync(item => item.StoredFileId == fileId);
        Assert.Equal(FileAccessGrantOutcome.Issued, audit.Outcome);
        Assert.Equal(actorUserId, audit.RequestedByUserId);
        Assert.Equal(actorRole, audit.RequesterRole);
        Assert.NotNull(audit.ExpiresAtUtc);
    }

    [Fact]
    public async Task PrivateAccessGrant_WithDeniedOrUnavailableFiles_DoesNotExposePrivateStorage()
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var otherEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, otherEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var other = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, otherEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", other.Id));

        using var unrelatedResponse = await client.PostAsync($"/api/files/{fileId}/access", null);
        using var guessedResponse = await client.PostAsync($"/api/files/{Guid.NewGuid():N}/access", null);
        await MarkFileAsync(factory.Services, fileId, file =>
        {
            file.UploadStatus = StoredFileUploadStatus.Deleted;
            file.DeletedAtUtc = DateTime.UtcNow;
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", owner.Id));
        using var deletedResponse = await client.PostAsync($"/api/files/{fileId}/access", null);

        Assert.Equal(HttpStatusCode.Forbidden, unrelatedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, guessedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);
        Assert.Equal(0, storageProvider.AccessGrantCallCount);
        await AssertNoPrivateTextAsync(unrelatedResponse);
        await AssertNoPrivateTextAsync(guessedResponse);
        await AssertNoPrivateTextAsync(deletedResponse);
    }

    [Theory]
    [InlineData(StoredFileReviewStatus.Rejected)]
    [InlineData(StoredFileReviewStatus.Quarantined)]
    public async Task PrivateAccessGrant_WithRejectedOrQuarantinedFile_ReturnsNotFound(StoredFileReviewStatus status)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);
        await MarkFileAsync(factory.Services, fileId, file => file.ReviewStatus = status);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", owner.Id));

        using var response = await client.PostAsync($"/api/files/{fileId}/access", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, storageProvider.AccessGrantCallCount);
    }

    [Fact]
    public async Task PrivateAccessGrant_WhenProviderReturnsExpiredGrant_ReturnsForbiddenAndAuditsExpired()
    {
        await using var factory = CreateFactory(out var storageProvider);
        storageProvider.AccessGrantExpiresAtOverride = DateTime.UtcNow.AddMinutes(-1);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", owner.Id));

        using var response = await client.PostAsync($"/api/files/{fileId}/access", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var audit = await db.FileAccessGrantAudits.SingleAsync(item => item.StoredFileId == fileId);
        Assert.Equal(FileAccessGrantOutcome.Expired, audit.Outcome);
    }

    [Theory]
    [InlineData(AccountStatus.Rejected)]
    [InlineData(AccountStatus.Suspended)]
    [InlineData(AccountStatus.Inactive)]
    public async Task PrivateAccessGrant_AndDelete_WithInactiveOwner_DenyNonAdminButAllowAdminAccess(AccountStatus inactiveStatus)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, inactiveStatus);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", owner.Id));

        using var ownerAccess = await client.PostAsync($"/api/files/{fileId}/access", null);
        using var ownerDelete = await client.DeleteAsync($"/api/files/{fileId}");
        await AssertReplacementDeniedForInactiveOwnerAsync(factory.Services, owner.Id, fileId);

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));
        using var adminAccess = await client.PostAsync($"/api/files/{fileId}/access", null);

        Assert.Equal(HttpStatusCode.Forbidden, ownerAccess.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ownerDelete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminAccess.StatusCode);
        Assert.Equal(1, storageProvider.AccessGrantCallCount);
    }

    [Fact]
    public async Task PrivateAccessGrant_WithSoftDeletedOwner_DeniesNonAdminAccess()
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);
        await SetDoctorProfileDeletedAsync(factory.Services, owner.Id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", owner.Id));

        using var response = await client.PostAsync($"/api/files/{fileId}/access", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_WithAuthorizedOwner_MarksUnavailablePreservesMetadataDeletesProviderAndAudits()
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", owner.Id));

        using var response = await client.DeleteAsync($"/api/files/{fileId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, storageProvider.DeleteCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(200, document.RootElement.GetProperty("Code").GetInt32());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(fileId, data.GetProperty("Id").GetString());
        Assert.Equal("Deleted", data.GetProperty("UploadStatus").GetString());
        AssertDtoHasNoPrivateStorageFields(data);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var storedFile = await db.StoredFiles.IgnoreQueryFilters().SingleAsync(file => file.Id == fileId);
        Assert.Equal(StoredFileUploadStatus.Deleted, storedFile.UploadStatus);
        Assert.NotNull(storedFile.DeletedAtUtc);
        Assert.Equal("private/storage/key.pdf", storedFile.StorageKey);
        Assert.Contains(await db.AuditEvents.Select(audit => audit.EventType).ToListAsync(), item => item == "FileDeleted");
    }

    [Fact]
    public async Task PendingReviews_WithAdminToken_ReturnsStoredPendingFilesOrderedByCreatedTime()
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var newerFileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor, createdAtUtc: DateTime.UtcNow.AddMinutes(-1));
        var olderFileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor, createdAtUtc: DateTime.UtcNow.AddMinutes(-10));
        await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor, reviewStatus: StoredFileReviewStatus.Approved);
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.GetAsync("/api/admin/files/pending?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(2, data.GetProperty("TotalCount").GetInt32());
        var items = data.GetProperty("Items").EnumerateArray().ToList();
        Assert.Equal(olderFileId, items[0].GetProperty("Id").GetString());
        Assert.Equal(newerFileId, items[1].GetProperty("Id").GetString());
        Assert.All(items, AssertDtoHasNoPrivateStorageFields);
    }

    [Theory]
    [InlineData(FileReviewDecision.Approved, null, StoredFileReviewStatus.Approved)]
    [InlineData(FileReviewDecision.Rejected, "Document is expired.", StoredFileReviewStatus.Rejected)]
    [InlineData(FileReviewDecision.Quarantined, "File is suspicious.", StoredFileReviewStatus.Quarantined)]
    [InlineData(FileReviewDecision.ReplacementRequested, "Please upload a clearer copy.", StoredFileReviewStatus.ReplacementRequested)]
    public async Task ReviewFile_WithAdminDecision_AppendsReviewAndUpdatesCurrentSummary(FileReviewDecision decision, string? reason, StoredFileReviewStatus expectedStatus)
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));
        using var body = CreateJson($$"""{"Decision":"{{decision}}","Reason":{{JsonSerializer.Serialize(reason)}}}""");

        using var response = await client.PutAsync($"/api/admin/files/{fileId}/review", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var storedFile = await db.StoredFiles.SingleAsync(file => file.Id == fileId);
        Assert.Equal(expectedStatus, storedFile.ReviewStatus);
        Assert.Equal(admin.Id, storedFile.ReviewedByAdminId);
        Assert.NotNull(storedFile.ReviewedAtUtc);
        var review = await db.FileReviews.SingleAsync(item => item.StoredFileId == fileId);
        Assert.Equal(decision, review.Decision);
        Assert.Equal(reason, review.Reason);
        Assert.Equal(admin.Id, review.AdminUserId);
        Assert.Contains(await db.AuditEvents.Select(audit => audit.EventType).ToListAsync(), item => item == "FileReviewDecisionRecorded");
    }

    [Theory]
    [InlineData(FileReviewDecision.Rejected)]
    [InlineData(FileReviewDecision.Quarantined)]
    [InlineData(FileReviewDecision.ReplacementRequested)]
    [InlineData(FileReviewDecision.Correction)]
    public async Task ReviewFile_WithReasonRequiredDecisionAndMissingReason_ReturnsValidation(FileReviewDecision decision)
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.PutAsync($"/api/admin/files/{fileId}/review", CreateJson($$"""{"Decision":"{{decision}}"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Empty(await db.FileReviews.Where(item => item.StoredFileId == fileId).ToListAsync());
    }

    [Fact]
    public async Task CorrectionReview_PreservesOriginalReviewAndLinksCorrectedReview()
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);
        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));
        using var firstResponse = await client.PutAsync($"/api/admin/files/{fileId}/review", CreateJson("""{"Decision":"Rejected","Reason":"Expired license."}"""));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var firstDocument = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var originalReviewId = firstDocument.RootElement.GetProperty("Data").GetProperty("Id").GetString();

        using var correctionResponse = await client.PutAsync($"/api/admin/files/{fileId}/review", CreateJson($$"""{"Decision":"Correction","Reason":"Manual correction after appeal.","CorrectsReviewId":"{{originalReviewId}}"}"""));

        Assert.Equal(HttpStatusCode.OK, correctionResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var reviews = await db.FileReviews.Where(review => review.StoredFileId == fileId).OrderBy(review => review.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, reviews.Count);
        Assert.Equal(FileReviewDecision.Rejected, reviews[0].Decision);
        Assert.Null(reviews[0].CorrectsReviewId);
        Assert.Equal(FileReviewDecision.Correction, reviews[1].Decision);
        Assert.Equal(originalReviewId, reviews[1].CorrectsReviewId);
    }

    [Fact]
    public async Task ReviewSummaryUpdate_WithStaleConcurrencyStamp_ReturnsConflictWithoutOverwritingCurrentDecision()
    {
        await using var factory = CreateFactory(out _);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor);

        using var firstScope = factory.Services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var staleStamp = await firstDb.StoredFiles.Where(file => file.Id == fileId).Select(file => file.ConcurrencyStamp).SingleAsync();
        using var secondScope = factory.Services.CreateScope();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var file = await secondDb.StoredFiles.SingleAsync(item => item.Id == fileId);
        file.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        file.ReviewStatus = StoredFileReviewStatus.Approved;
        await secondDb.SaveChangesAsync();
        var updated = await firstScope.ServiceProvider.GetRequiredService<MediBridge.Core.Interfaces.IDomainUnitOfWork>().StoredFiles.UpdateReviewSummaryAsync(
            fileId,
            StoredFileReviewStatus.Rejected,
            "admin",
            "stale decision",
            DateTime.UtcNow,
            staleStamp);

        Assert.False(updated);
        Assert.Equal(StoredFileReviewStatus.Approved, await secondDb.StoredFiles.Where(item => item.Id == fileId).Select(item => item.ReviewStatus).SingleAsync());
    }

    [Fact]
    public async Task ReplaceFile_WithOwnerAndRejectedFile_CreatesPendingReplacementMarksOriginalUnavailableAndPreservesHistory()
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor, reviewStatus: StoredFileReviewStatus.Rejected);
        await AddReviewAsync(factory.Services, fileId, FileReviewDecision.Rejected, "Expired.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", owner.Id));

        using var response = await client.PostAsync($"/api/files/{fileId}/replacement", CreateMultipart("replacement.pdf", "application/pdf", 2048));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, storageProvider.UploadCallCount);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var replacementId = document.RootElement.GetProperty("Data").GetProperty("Id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(replacementId));
        AssertDtoHasNoPrivateStorageFields(document.RootElement.GetProperty("Data"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var original = await db.StoredFiles.SingleAsync(item => item.Id == fileId);
        var replacement = await db.StoredFiles.SingleAsync(item => item.Id == replacementId);
        Assert.Equal(StoredFileUploadStatus.Replaced, original.UploadStatus);
        Assert.Equal(replacementId, original.ReplacedByFileId);
        Assert.Equal(StoredFileUploadStatus.Stored, replacement.UploadStatus);
        Assert.Equal(StoredFileReviewStatus.Pending, replacement.ReviewStatus);
        Assert.Null(replacement.ReplacedByFileId);
        Assert.Equal(original.OwnerType, replacement.OwnerType);
        Assert.Equal(original.OwnerId, replacement.OwnerId);
        Assert.Equal(original.Purpose, replacement.Purpose);
        Assert.Single(await db.FileReviews.Where(review => review.StoredFileId == fileId).ToListAsync());
        Assert.Empty(await db.FileReviews.Where(review => review.StoredFileId == replacementId).ToListAsync());
        Assert.Contains(await db.AuditEvents.Select(audit => audit.EventType).ToListAsync(), item => item == "FileReplacementUploaded");
    }

    [Theory]
    [InlineData("replacement.exe", "application/pdf", 1024)]
    [InlineData("replacement.pdf", "text/plain", 1024)]
    [InlineData("replacement.pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1024)]
    [InlineData("../replacement.pdf", "application/pdf", 1024)]
    [InlineData("replacement.pdf", "application/pdf", 10 * 1024 * 1024 + 1)]
    public async Task ReplaceFile_WithInvalidFile_IsRejectedBeforeProviderUpload(string fileName, string contentType, int sizeBytes)
    {
        await using var factory = CreateFactory(out var storageProvider);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var ownerEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, ownerEmail, AccountStatus.Approved);
        var owner = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, ownerEmail);
        var fileId = await SeedStoredVerificationFileAsync(factory.Services, owner.Id, StoredFileOwnerType.Doctor, reviewStatus: StoredFileReviewStatus.Rejected);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", owner.Id));

        using var response = await client.PostAsync($"/api/files/{fileId}/replacement", CreateMultipart(fileName, contentType, sizeBytes));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, storageProvider.UploadCallCount);
    }

    private static Phase4FileWorkflowWebAppFactory CreateFactory(out Phase4FakeFileStorageProvider storageProvider)
    {
        storageProvider = new Phase4FakeFileStorageProvider();
        return new Phase4FileWorkflowWebAppFactory(storageProvider);
    }

    private static MultipartFormDataContent CreateMultipart(string fileName, string contentType, int sizeBytes)
    {
        var content = new MultipartFormDataContent();
        var bytes = sizeBytes <= 0 ? [] : new byte[sizeBytes];
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private static async Task<string> SeedStoredVerificationFileAsync(
        IServiceProvider services,
        string ownerUserId,
        StoredFileOwnerType ownerType,
        StoredFileReviewStatus reviewStatus = StoredFileReviewStatus.Pending,
        DateTime? createdAtUtc = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var ownerId = ownerType == StoredFileOwnerType.Company
            ? await db.CompanyProfiles.Where(profile => profile.UserId == ownerUserId).Select(profile => profile.Id).SingleAsync()
            : await db.DoctorProfiles.Where(profile => profile.UserId == ownerUserId).Select(profile => profile.Id).SingleAsync();
        var file = new StoredFile
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerType = ownerType,
            OwnerId = ownerId,
            Purpose = StoredFilePurpose.VerificationDocument,
            OriginalFileName = "license.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            StorageKey = "private/storage/key.pdf",
            StorageProvider = "FakeStorage",
            StorageResourceType = StoredFileStorageResourceType.Raw,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = reviewStatus,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private static async Task AddReviewAsync(IServiceProvider services, string storedFileId, FileReviewDecision decision, string reason)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var admin = await db.Users.Where(user => user.Role == UserRole.Admin).Select(user => user.Id).FirstOrDefaultAsync()
            ?? (await Phase6IdentityTestHelpers.CreateAdminAsync(services)).Id;
        db.FileReviews.Add(new MediBridge.Core.Entities.Files.FileReview
        {
            Id = Guid.NewGuid().ToString("N"),
            StoredFileId = storedFileId,
            AdminUserId = admin,
            Decision = decision,
            Reason = reason,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static StringContent CreateJson(string json)
    {
        return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    }

    private static async Task MarkFileAsync(IServiceProvider services, string fileId, Action<StoredFile> mark)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var file = await db.StoredFiles.SingleAsync(item => item.Id == fileId);
        mark(file);
        await db.SaveChangesAsync();
    }

    private static async Task SetDoctorProfileDeletedAsync(IServiceProvider services, string ownerUserId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var profile = await db.DoctorProfiles.SingleAsync(item => item.UserId == ownerUserId);
        profile.IsDeleted = true;
        profile.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task AssertNoPrivateTextAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private/storage", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cloudinary://", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertReplacementDeniedForInactiveOwnerAsync(IServiceProvider services, string ownerUserId, string fileId)
    {
        using var scope = services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IFileWorkflowService>();
        await using var stream = new MemoryStream(new byte[1024]);
        var upload = new FileWorkflowUpload("replacement.pdf", "application/pdf", stream.Length, stream);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            workflow.ReplaceFileAsync(ownerUserId, UserRole.Doctor, fileId, upload));
    }

    private static void AssertDtoHasNoPrivateStorageFields(JsonElement data)
    {
        foreach (var property in data.EnumerateObject())
        {
            Assert.DoesNotContain("StorageKey", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Credential", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Signed", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Token", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Diagnostic", property.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class Phase4FileWorkflowWebAppFactory : TestHost.ConfiguredWebAppFactory
    {
        private readonly Phase4FakeFileStorageProvider storageProvider;

        public Phase4FileWorkflowWebAppFactory(Phase4FakeFileStorageProvider storageProvider)
        {
            this.storageProvider = storageProvider;
        }

        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorageProvider>();
                services.AddSingleton<IFileStorageProvider>(storageProvider);
            });
        }
    }
}
