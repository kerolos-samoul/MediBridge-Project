using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public sealed class FileWorkflowContractTests
{
    [Fact]
    public async Task UploadVerificationDocument_WithDoctorToken_ReturnsCreatedEnvelopeWithoutPrivateStorageFields()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.PostAsync("/api/files/verification-documents", CreateMultipart("license.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 201, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal("VerificationDocument", data.GetProperty("Purpose").GetString());
        Assert.Equal("Doctor", data.GetProperty("OwnerType").GetString());
        Assert.Equal("Pending", data.GetProperty("ReviewStatus").GetString());
        Assert.Equal("Stored", data.GetProperty("UploadStatus").GetString());
        AssertNoPrivateStorageFields(data);
    }

    [Fact]
    public async Task UploadVerificationDocument_WithInvalidFile_ReturnsValidationEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.PostAsync("/api/files/verification-documents", CreateMultipart("license.exe", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task UploadVerificationDocument_WithoutMultipartFile_ReturnsValidationEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.PostAsync("/api/files/verification-documents", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task UploadVerificationDocument_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/files/verification-documents", CreateMultipart("license.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Unauthorized.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task UploadVerificationDocument_WithWrongRole_ReturnsForbiddenEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken("admin-user", "Admin"));

        using var response = await client.PostAsync("/api/files/verification-documents", CreateMultipart("license.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task UploadVerificationDocument_AttemptTwentyOneWithinOneHour_ReturnsRateLimitEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        for (var index = 0; index < 20; index++)
        {
            using var accepted = await client.PostAsync("/api/files/verification-documents", CreateMultipart($"license-{index}.pdf", "application/pdf", 1024));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using var response = await client.PostAsync("/api/files/verification-documents", CreateMultipart("license-21.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 429, "Too many requests.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task CreatePrivateAccessGrant_WithAuthorizedOwner_ReturnsOkEnvelopeWithoutStorageReferenceFields()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, userId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.PostAsync($"/api/files/{fileId}/access", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.StartsWith("https://files.example.test/", data.GetProperty("Url").GetString(), StringComparison.Ordinal);
        Assert.True(data.TryGetProperty("ExpiresAtUtc", out _));
        AssertNoPrivateStorageFields(data);
    }

    [Fact]
    public async Task CreatePrivateAccessGrant_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, userId);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/files/{fileId}/access", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Unauthorized.");
    }

    [Fact]
    public async Task CreatePrivateAccessGrant_WithUnrelatedUser_ReturnsForbiddenEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var ownerUserId = await CreateDoctorAsync(factory.Services);
        var otherUserId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, ownerUserId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(otherUserId, "Doctor"));

        using var response = await client.PostAsync($"/api/files/{fileId}/access", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task CreatePrivateAccessGrant_WithMissingFile_ReturnsNotFoundEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.PostAsync($"/api/files/{Guid.NewGuid():N}/access", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 404, "Not found.");
    }

    [Fact]
    public async Task DeleteFile_WithAuthorizedOwner_ReturnsOkEnvelopeWithoutPrivateStorageReferences()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, userId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.DeleteAsync($"/api/files/{fileId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(fileId, data.GetProperty("Id").GetString());
        Assert.Equal("Deleted", data.GetProperty("UploadStatus").GetString());
        AssertNoPrivateStorageFields(data);
    }

    [Fact]
    public async Task DeleteFile_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, userId);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/api/files/{fileId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Unauthorized.");
    }

    [Fact]
    public async Task DeleteFile_WithUnrelatedUser_ReturnsForbiddenEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var ownerUserId = await CreateDoctorAsync(factory.Services);
        var otherUserId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, ownerUserId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(otherUserId, "Doctor"));

        using var response = await client.DeleteAsync($"/api/files/{fileId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
    }

    [Fact]
    public async Task DeleteFile_WithMissingFile_ReturnsNotFoundEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.DeleteAsync($"/api/files/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 404, "Not found.");
    }

    [Fact]
    public async Task ListPendingAdminFiles_WithAdminToken_ReturnsOkEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var ownerUserId = await CreateDoctorAsync(factory.Services);
        var adminUserId = await CreateAdminAsync(factory.Services);
        await CreateStoredFileAsync(factory.Services, ownerUserId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(adminUserId, "Admin"));

        using var response = await client.GetAsync("/api/admin/files/pending");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(1, data.GetProperty("TotalCount").GetInt32());
        AssertNoPrivateStorageFields(data.GetProperty("Items")[0]);
    }

    [Fact]
    public async Task ListPendingAdminFiles_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/admin/files/pending");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Unauthorized.");
    }

    [Fact]
    public async Task ListPendingAdminFiles_WithDoctorToken_ReturnsForbiddenEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.GetAsync("/api/admin/files/pending");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
    }

    [Fact]
    public async Task ReviewAdminFile_WithAdminToken_ReturnsOkEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var ownerUserId = await CreateDoctorAsync(factory.Services);
        var adminUserId = await CreateAdminAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, ownerUserId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(adminUserId, "Admin"));

        using var response = await client.PutAsync($"/api/admin/files/{fileId}/review", CreateJson("""{"Decision":"Approved","Reason":"Looks valid."}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(fileId, data.GetProperty("StoredFileId").GetString());
        Assert.Equal("Approved", data.GetProperty("Decision").GetString());
        AssertNoPrivateStorageFields(data);
    }

    [Fact]
    public async Task ReviewAdminFile_WithMissingReason_ReturnsValidationEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var ownerUserId = await CreateDoctorAsync(factory.Services);
        var adminUserId = await CreateAdminAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, ownerUserId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(adminUserId, "Admin"));

        using var response = await client.PutAsync($"/api/admin/files/{fileId}/review", CreateJson("""{"Decision":"Rejected"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
    }

    [Fact]
    public async Task ReviewAdminFile_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var ownerUserId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, ownerUserId);
        using var client = factory.CreateClient();

        using var response = await client.PutAsync($"/api/admin/files/{fileId}/review", CreateJson("""{"Decision":"Approved"}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Unauthorized.");
    }

    [Fact]
    public async Task ReviewAdminFile_WithDoctorToken_ReturnsForbiddenEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, userId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.PutAsync($"/api/admin/files/{fileId}/review", CreateJson("""{"Decision":"Approved"}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
    }

    [Fact]
    public async Task ReviewAdminFile_WithMissingFile_ReturnsNotFoundEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var adminUserId = await CreateAdminAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(adminUserId, "Admin"));

        using var response = await client.PutAsync($"/api/admin/files/{Guid.NewGuid():N}/review", CreateJson("""{"Decision":"Approved"}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 404, "Not found.");
    }

    [Fact]
    public async Task GetAdminFileReviewHistory_WithAdminToken_ReturnsOkEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var ownerUserId = await CreateDoctorAsync(factory.Services);
        var adminUserId = await CreateAdminAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, ownerUserId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(adminUserId, "Admin"));
        using var reviewResponse = await client.PutAsync($"/api/admin/files/{fileId}/review", CreateJson("""{"Decision":"Approved"}"""));
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);

        using var response = await client.GetAsync($"/api/admin/files/{fileId}/reviews");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 200, "Success");
        Assert.Single(document.RootElement.GetProperty("Data").EnumerateArray());
    }

    [Fact]
    public async Task ReplaceFile_WithAuthorizedOwner_ReturnsCreatedEnvelopeWithoutPrivateStorageFields()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, userId, StoredFileReviewStatus.Rejected);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.PostAsync($"/api/files/{fileId}/replacement", CreateMultipart("replacement.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 201, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(fileId, data.GetProperty("ReplacedFileId").GetString());
        Assert.Equal("Pending", data.GetProperty("ReviewStatus").GetString());
        Assert.Equal("Stored", data.GetProperty("UploadStatus").GetString());
        AssertNoPrivateStorageFields(data);
    }

    [Fact]
    public async Task ReplaceFile_WithInvalidFile_ReturnsValidationEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, userId, StoredFileReviewStatus.Rejected);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.PostAsync($"/api/files/{fileId}/replacement", CreateMultipart("replacement.exe", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
    }

    [Fact]
    public async Task ReplaceFile_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, userId, StoredFileReviewStatus.Rejected);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/files/{fileId}/replacement", CreateMultipart("replacement.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Unauthorized.");
    }

    [Fact]
    public async Task ReplaceFile_WithUnrelatedUser_ReturnsForbiddenEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var ownerUserId = await CreateDoctorAsync(factory.Services);
        var otherUserId = await CreateDoctorAsync(factory.Services);
        var fileId = await CreateStoredFileAsync(factory.Services, ownerUserId, StoredFileReviewStatus.Rejected);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(otherUserId, "Doctor"));

        using var response = await client.PostAsync($"/api/files/{fileId}/replacement", CreateMultipart("replacement.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
    }

    [Fact]
    public async Task ReplaceFile_WithMissingFile_ReturnsNotFoundEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        using var response = await client.PostAsync($"/api/files/{Guid.NewGuid():N}/replacement", CreateMultipart("replacement.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 404, "Not found.");
    }

    [Fact]
    public async Task ReplaceFile_AttemptTwentyOneWithinOneHour_ReturnsRateLimitEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Doctor"));

        for (var index = 0; index < 20; index++)
        {
            var fileId = await CreateStoredFileAsync(factory.Services, userId, StoredFileReviewStatus.Rejected);
            using var accepted = await client.PostAsync($"/api/files/{fileId}/replacement", CreateMultipart($"replacement-{index}.pdf", "application/pdf", 1024));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        var finalFileId = await CreateStoredFileAsync(factory.Services, userId, StoredFileReviewStatus.Rejected);
        using var response = await client.PostAsync($"/api/files/{finalFileId}/replacement", CreateMultipart("replacement-21.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 429, "Too many requests.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task UploadCampaignFile_WithCompanyOwner_ReturnsCreatedEnvelopeWithoutPrivateStorageFields()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateCompanyAsync(factory.Services);
        var campaignId = await CreateCampaignAsync(factory.Services, userId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Company"));

        using var response = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("Purpose", "CampaignMedia", "banner.png", "image/png", 1024));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 201, "Success");
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal("CampaignMedia", data.GetProperty("Purpose").GetString());
        Assert.Equal("Company", data.GetProperty("OwnerType").GetString());
        Assert.Equal(campaignId, data.GetProperty("RelatedCampaignId").GetString());
        Assert.Equal("Pending", data.GetProperty("ReviewStatus").GetString());
        Assert.Equal("Stored", data.GetProperty("UploadStatus").GetString());
        AssertNoPrivateStorageFields(data);
    }

    [Fact]
    public async Task UploadCampaignFile_WithInvalidPurpose_ReturnsValidationEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateCompanyAsync(factory.Services);
        var campaignId = await CreateCampaignAsync(factory.Services, userId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Company"));

        using var response = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("Purpose", "VerificationDocument", "license.pdf", "application/pdf", 1024));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 400, "Validation failed.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task UploadCampaignFile_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateCompanyAsync(factory.Services);
        var campaignId = await CreateCampaignAsync(factory.Services, userId);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("Purpose", "CampaignMedia", "banner.png", "image/png", 1024));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 401, "Unauthorized.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task UploadCampaignFile_WithNonCompanyRole_ReturnsForbiddenEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var companyUserId = await CreateCompanyAsync(factory.Services);
        var campaignId = await CreateCampaignAsync(factory.Services, companyUserId);
        var doctorUserId = await CreateDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(doctorUserId, "Doctor"));

        using var response = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("Purpose", "CampaignMedia", "banner.png", "image/png", 1024));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
    }

    [Fact]
    public async Task UploadCampaignFile_WithOwnedNonDraftCampaign_ReturnsForbiddenEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateCompanyAsync(factory.Services);
        var campaignId = await CreateCampaignAsync(factory.Services, userId, CampaignStatus.Approved);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Company"));

        using var response = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("Purpose", "CampaignMedia", "banner.png", "image/png", 1024));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 403, "Forbidden.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task UploadCampaignFile_WithMissingCampaign_ReturnsNotFoundEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateCompanyAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Company"));

        using var response = await client.PostAsync($"/api/campaigns/{Guid.NewGuid():N}/files", CreateMultipart("Purpose", "CampaignMedia", "banner.png", "image/png", 1024));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 404, "Not found.");
    }

    [Fact]
    public async Task UploadCampaignFile_AttemptTwentyOneWithinOneHour_ReturnsRateLimitEnvelope()
    {
        await using var factory = CreateFactory();
        await InitializeDatabaseAsync(factory.Services);
        var userId = await CreateCompanyAsync(factory.Services);
        var campaignId = await CreateCampaignAsync(factory.Services, userId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ContractJwtFactory.CreateToken(userId, "Company"));

        for (var index = 0; index < 20; index++)
        {
            using var accepted = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("Purpose", "CampaignMedia", $"banner-{index}.png", "image/png", 1024));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using var response = await client.PostAsync($"/api/campaigns/{campaignId}/files", CreateMultipart("Purpose", "CampaignMedia", "banner-21.png", "image/png", 1024));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertEnvelope(document.RootElement, 429, "Too many requests.");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private static ContractWebAppFactory CreateFactory()
    {
        return new Phase4ContractWebAppFactory();
    }

    private static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await db.Database.MigrateAsync();
    }

    private static async Task<string> CreateDoctorAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var user = new MediBridgeIdentityUser
        {
            Email = $"doctor-{Guid.NewGuid():N}@example.com",
            UserName = $"doctor-{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"DOCTOR-{Guid.NewGuid():N}@EXAMPLE.COM",
            NormalizedUserName = $"DOCTOR-{Guid.NewGuid():N}@EXAMPLE.COM",
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            LastStatusChangedAtUtc = now
        };
        db.Users.Add(user);
        db.DoctorProfiles.Add(new DoctorProfile
        {
            UserId = user.Id,
            Specialization = "Cardiology",
            ExperienceYears = 5,
            Location = "Lagos",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "seed.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 512,
            VerificationReference = $"ref-{Guid.NewGuid():N}"
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<string> CreateAdminAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var user = new MediBridgeIdentityUser
        {
            Email = $"admin-{Guid.NewGuid():N}@example.com",
            UserName = $"admin-{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"ADMIN-{Guid.NewGuid():N}@EXAMPLE.COM",
            NormalizedUserName = $"ADMIN-{Guid.NewGuid():N}@EXAMPLE.COM",
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            LastStatusChangedAtUtc = now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<string> CreateCompanyAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var email = $"company-{Guid.NewGuid():N}@example.com";
        var user = new MediBridgeIdentityUser
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            Role = UserRole.Company,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            LastStatusChangedAtUtc = now
        };
        db.Users.Add(user);
        db.CompanyProfiles.Add(new CompanyProfile
        {
            UserId = user.Id,
            CompanyName = "Acme Pharma",
            LicenseNumber = $"LIC-{Guid.NewGuid():N}",
            ContactName = "Casey Admin",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "seed.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 512,
            VerificationReference = $"ref-{Guid.NewGuid():N}"
        });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<string> CreateCampaignAsync(IServiceProvider services, string companyUserId, CampaignStatus status = CampaignStatus.Draft)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var companyId = await db.CompanyProfiles.Where(profile => profile.UserId == companyUserId).Select(profile => profile.Id).SingleAsync();
        var campaign = new Campaign
        {
            Id = Guid.NewGuid().ToString("N"),
            CompanyId = companyId,
            Title = "Campaign draft",
            Description = "Campaign file test",
            Status = status
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    private static async Task<string> CreateStoredFileAsync(IServiceProvider services, string ownerUserId, StoredFileReviewStatus reviewStatus = StoredFileReviewStatus.Pending)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var ownerId = await db.DoctorProfiles.Where(profile => profile.UserId == ownerUserId).Select(profile => profile.Id).SingleAsync();
        var file = new StoredFile
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerType = StoredFileOwnerType.Doctor,
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
            CreatedAtUtc = DateTime.UtcNow
        };
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private static StringContent CreateJson(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static MultipartFormDataContent CreateMultipart(string fileName, string contentType, int sizeBytes)
    {
        return CreateMultipart(null, null, fileName, contentType, sizeBytes);
    }

    private static MultipartFormDataContent CreateMultipart(string? textName, string? textValue, string fileName, string contentType, int sizeBytes)
    {
        var content = new MultipartFormDataContent();
        if (textName is not null && textValue is not null)
        {
            content.Add(new StringContent(textValue), textName);
        }

        var bytes = sizeBytes <= 0 ? [] : new byte[sizeBytes];
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private static void AssertEnvelope(JsonElement root, int expectedCode, string expectedMessage)
    {
        Assert.Equal(expectedCode, root.GetProperty("Code").GetInt32());
        Assert.Equal(expectedMessage, root.GetProperty("Message").GetString());
    }

    private static void AssertNoPrivateStorageFields(JsonElement data)
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

    private sealed class ContractFakeFileStorageProvider : IFileStorageProvider
    {
        public Task<FileStorageUploadResponse> UploadAsync(FileStorageUploadRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FileStorageUploadResponse("FakeStorage", request.StorageKey, request.ResourceType, request.DeliveryType, request.Content.Length, request.ContentType, "etag"));
        }

        public Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(string storageKey, StoredFileStorageResourceType resourceType, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FileStorageAccessGrant($"https://files.example.test/{storageKey}", expiresAtUtc));
        }

        public Task DeleteAsync(string storageKey, StoredFileStorageResourceType resourceType, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class Phase4ContractWebAppFactory : ContractWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorageProvider>();
                services.AddSingleton<IFileStorageProvider, ContractFakeFileStorageProvider>();
            });
        }
    }

    private static class ContractJwtFactory
    {
        private const string Issuer = "MediBridge.ContractTests";
        private const string Audience = "MediBridge.ContractTests.ApiClients";
        private const string SigningKey = "ContractTestSigningKey-ReplaceBeforeProduction-32Chars";

        public static string CreateToken(string userId, string role)
        {
            var issuedAt = DateTimeOffset.UtcNow;
            var payload = new Dictionary<string, object>
            {
                ["sub"] = userId,
                ["email"] = $"{role.ToLowerInvariant()}@example.com",
                ["name"] = $"{role.ToLowerInvariant()}@example.com",
                ["role"] = role,
                ["iss"] = Issuer,
                ["aud"] = Audience,
                ["iat"] = issuedAt.ToUnixTimeSeconds(),
                ["nbf"] = issuedAt.ToUnixTimeSeconds(),
                ["exp"] = issuedAt.AddMinutes(15).ToUnixTimeSeconds(),
                ["jti"] = Guid.NewGuid().ToString("N")
            };

            var header = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["alg"] = "HS256",
                ["typ"] = "JWT"
            })));
            var body = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
            var signature = Base64UrlEncode(CreateSignature($"{header}.{body}"));
            return $"{header}.{body}.{signature}";
        }

        private static byte[] CreateSignature(string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
