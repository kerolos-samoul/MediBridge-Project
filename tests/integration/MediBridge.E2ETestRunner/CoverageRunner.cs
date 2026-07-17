using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MediBridge.E2ETestRunner
{
    public partial class Program
    {
        private static async Task RunCoverageAsync()
        {
            var root = FindRepositoryRoot();
            var connectionString = GetConnectionString();
            var prior = await LoadLatestReportAsync(root);
            var priorReport = prior.Report;
            var state = priorReport.E2EState;
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

            using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            var evidence = new LiveE2EEvidence
            {
                TimestampUtc = DateTime.UtcNow,
                BaseUrl = BaseUrl,
                Database = MaskConnectionString(connectionString),
                SourceReportPath = prior.SourcePath,
                TestContext = new Dictionary<string, string?>
                {
                    ["DoctorUserId"] = state.DoctorUserId,
                    ["DoctorProfileId"] = state.DoctorProfileId,
                    ["CompanyUserId"] = state.CompanyUserId,
                    ["CompanyProfileId"] = state.CompanyProfileId,
                    ["CampaignId"] = state.CampaignId,
                    ["CampaignAssetId"] = state.FileId,
                    ["OtpEvidence"] = "Retrieved through authorized inbox connector for exact E2E addresses; OTP values redacted."
                }
            };

            var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
            evidence.OpenApiPath = Path.Combine(root, "artifacts", "e2e", "live-swagger.json");
            Directory.CreateDirectory(Path.GetDirectoryName(evidence.OpenApiPath)!);
            await File.WriteAllTextAsync(evidence.OpenApiPath, swagger);
            LoadCoverageMatrix(swagger, evidence);
            ImportPriorReportScenarios(evidence, priorReport);

            var admin = await LoginAsync(client, "admin@medibridge.local", GetRequiredEnvironmentVariable("MEDIBRIDGE_E2E_ADMIN_PASSWORD"));
            await RecordAsync(evidence, client, "Auth", "/api/auth/login", HttpMethod.Post, "Admin login replay for coverage", "Admin", new { Username = "admin@medibridge.local", Password = "[REDACTED]" }, admin.Response, HttpStatusCode.OK, "Admin token acquired for live HTTP coverage.", new() { ["role"] = "Admin" });
            var company = await LoginAsync(client, state.CompanyEmail, state.CompanyPassword);
            await RecordAsync(evidence, client, "Auth", "/api/auth/login", HttpMethod.Post, "Company login", "Company", new { Username = state.CompanyEmail, Password = "[REDACTED]" }, company.Response, HttpStatusCode.OK, "Company token acquired.", new() { ["role"] = "Company" });
            var doctor = await LoginAsync(client, state.DoctorEmail, state.DoctorPassword);
            await RecordAsync(evidence, client, "Auth", "/api/auth/login", HttpMethod.Post, "Doctor login", "Doctor", new { Username = state.DoctorEmail, Password = "[REDACTED]" }, doctor.Response, HttpStatusCode.OK, "Doctor token acquired.", new() { ["role"] = "Doctor" });

            var tokens = new Dictionary<string, string>
            {
                ["Admin"] = admin.AccessToken,
                ["Company"] = company.AccessToken,
                ["Doctor"] = doctor.AccessToken
            };

            await RunAuthCoverageAsync(evidence, client, state, company.RefreshToken, tokens);
            await RunGenericAuthorizationCoverageAsync(evidence, client, tokens, state);
            await RunAdminAccountCoverageAsync(evidence, client, tokens, state);
            await RunFileCoverageAsync(evidence, client, tokens, state, connectionString);
            await RunPricingCoverageAsync(evidence, client, tokens, state);
            await RunWalletCoverageAsync(evidence, client, tokens, state, connectionString);
            await RunCampaignCoverageAsync(evidence, client, tokens, state, connectionString);
            await RunJobAndDeliveryCoverageAsync(evidence, client, tokens, state, connectionString);
            await RunWithdrawalCoverageAsync(evidence, client, tokens, state, connectionString);
            await RunAdminReadCoverageAsync(evidence, client, tokens, state);
            await RunDoctorStatusCoverageAsync(evidence, client, tokens, state);
            await MarkUntestedEndpointsAsync(evidence, client, tokens, state);

            evidence.TotalEndpoints = evidence.Endpoints.Count;
            evidence.TestedEndpoints = evidence.Endpoints.Count(e => e.Status is "tested");
            evidence.BlockedEndpoints = evidence.Endpoints.Count(e => e.Status is "blocked");
            evidence.CoveragePercent = evidence.TotalEndpoints == 0 ? 0 : Math.Round(100m * evidence.TestedEndpoints / evidence.TotalEndpoints, 2);
            evidence.TotalScenarios = evidence.Scenarios.Count;
            evidence.PassedScenarios = evidence.Scenarios.Count(s => s.Result == "pass");
            evidence.FailedScenarios = evidence.Scenarios.Count(s => s.Result == "fail");
            evidence.BlockedScenarios = evidence.Scenarios.Count(s => s.Result == "blocked");

            var evidencePath = Path.Combine(root, $"e2e-evidence-{timestamp}.json");
            await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(evidence, JsonOptions));
            Console.WriteLine($"Coverage evidence written: {evidencePath}");
            Console.WriteLine($"Endpoints: {evidence.TestedEndpoints}/{evidence.TotalEndpoints} tested, {evidence.BlockedEndpoints} blocked ({evidence.CoveragePercent}%).");
            Console.WriteLine($"Scenarios: {evidence.PassedScenarios} passed, {evidence.FailedScenarios} failed, {evidence.BlockedScenarios} blocked.");
        }

        private static async Task RunAuthCoverageAsync(LiveE2EEvidence evidence, HttpClient client, E2EState state, string companyRefreshToken, Dictionary<string, string> tokens)
        {
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/login", HttpMethod.Post, "Login missing password", "Anonymous", new { Username = state.DoctorEmail }, null, new[] { HttpStatusCode.BadRequest });
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/login", HttpMethod.Post, "Login invalid credentials", "Anonymous", new { Username = state.DoctorEmail, Password = "wrong" }, null, new[] { HttpStatusCode.Unauthorized });
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/register-doctor", HttpMethod.Post, "Duplicate doctor registration", "Anonymous", NewDoctorRegistration(state.DoctorEmail, state.DoctorPhone, "dup"), null, new[] { HttpStatusCode.Conflict });
            var companySuffix = Guid.NewGuid().ToString("N")[..8];
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/register-company", HttpMethod.Post, "Register company coverage account", "Anonymous", new
            {
                Email = $"company-coverage-{companySuffix}@medibridge.local",
                Password = "Password1!",
                PhoneNumber = $"55588{Random.Shared.Next(10000, 99999)}",
                CompanyName = $"Coverage Pharma {companySuffix}",
                LicenseNumber = $"cov-license-{companySuffix}",
                ContactName = "Coverage Contact",
                VerificationMetadata = Verification("TradeLicense", $"company-{companySuffix}.pdf", $"company-ref-{companySuffix}")
            }, null, new[] { HttpStatusCode.Created });
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/request-contact-verification", HttpMethod.Post, "Request contact verification for verified account", "Anonymous", new { Email = state.DoctorEmail, Channel = "Email" }, null, new[] { HttpStatusCode.Accepted, (HttpStatusCode)429, HttpStatusCode.BadRequest });
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/forgot-password", HttpMethod.Post, "Forgot password accepted without account disclosure", "Anonymous", new { Contact = state.DoctorEmail }, null, new[] { HttpStatusCode.Accepted });
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/reset-password", HttpMethod.Post, "Reset password invalid token", "Anonymous", new { ResetToken = "invalid-token", NewPassword = "Password2!" }, null, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized });
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/resubmit-registration", HttpMethod.Post, "Resubmit registration invalid token", "Anonymous", new { ResubmissionToken = "invalid-token", Specialization = "Cardiology" }, null, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Forbidden });

            var refreshResponse = await SendJsonAsync(evidence, client, "Auth", "/api/auth/refresh", HttpMethod.Post, "Refresh token rotation", "Anonymous", new { RefreshToken = companyRefreshToken }, null, new[] { HttpStatusCode.OK });
            var newRefresh = GetEnvelopeDataString(refreshResponse.Body, "RefreshToken");
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/refresh", HttpMethod.Post, "Refresh token reuse detection", "Anonymous", new { RefreshToken = companyRefreshToken }, null, new[] { HttpStatusCode.Conflict, HttpStatusCode.Unauthorized });
            if (!string.IsNullOrWhiteSpace(newRefresh))
            {
                await SendJsonAsync(evidence, client, "Auth", "/api/auth/logout", HttpMethod.Post, "Logout revokes refresh token", "Company", new { RefreshToken = newRefresh }, tokens["Company"], new[] { HttpStatusCode.OK });
                await SendJsonAsync(evidence, client, "Auth", "/api/auth/refresh", HttpMethod.Post, "Refresh after logout denied", "Anonymous", new { RefreshToken = newRefresh }, null, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Conflict });
            }
            else
            {
                await SendJsonAsync(evidence, client, "Auth", "/api/auth/logout", HttpMethod.Post, "Logout invalid refresh token rejected", "Company", new { RefreshToken = "invalid-refresh-token" }, tokens["Company"], new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Conflict });
            }
        }

        private static async Task RunGenericAuthorizationCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state)
        {
            await SendRawAsync(evidence, client, "AdminAccounts", "/api/admin/pending-accounts", HttpMethod.Get, "Anonymous admin access denied", "Anonymous", "/api/admin/pending-accounts", null, null, null, new[] { HttpStatusCode.Unauthorized });
            await SendRawAsync(evidence, client, "AdminAccounts", "/api/admin/pending-accounts", HttpMethod.Get, "Invalid token admin access denied", "InvalidToken", "/api/admin/pending-accounts", null, "invalid.token.value", null, new[] { HttpStatusCode.Unauthorized });
            await SendRawAsync(evidence, client, "CompanyWallet", "/api/company/wallet", HttpMethod.Get, "Anonymous company access denied", "Anonymous", "/api/company/wallet", null, null, null, new[] { HttpStatusCode.Unauthorized });
            await SendRawAsync(evidence, client, "CompanyWallet", "/api/company/wallet", HttpMethod.Get, "Invalid token company access denied", "InvalidToken", "/api/company/wallet", null, "invalid.token.value", null, new[] { HttpStatusCode.Unauthorized });
            await SendRawAsync(evidence, client, "DoctorMessages", "/api/doctor/messages/today", HttpMethod.Get, "Anonymous doctor access denied", "Anonymous", "/api/doctor/messages/today", null, null, null, new[] { HttpStatusCode.Unauthorized });
            await SendRawAsync(evidence, client, "DoctorMessages", "/api/doctor/messages/today", HttpMethod.Get, "Invalid token doctor access denied", "InvalidToken", "/api/doctor/messages/today", null, "invalid.token.value", null, new[] { HttpStatusCode.Unauthorized });
            await SendRawAsync(evidence, client, "Files", "/api/files/{fileId}", HttpMethod.Get, "Anonymous file access denied", "Anonymous", $"/api/files/{state.FileId}", null, null, null, new[] { HttpStatusCode.Unauthorized });
            await SendRawAsync(evidence, client, "CompanyWallet", "/api/company/wallet", HttpMethod.Get, "Wrong role denied for company wallet", "Doctor", "/api/company/wallet", null, tokens["Doctor"], null, new[] { HttpStatusCode.Forbidden });
            await SendRawAsync(evidence, client, "AdminAccounts", "/api/admin/pending-accounts", HttpMethod.Get, "Wrong role denied for admin endpoint", "Company", "/api/admin/pending-accounts", null, tokens["Company"], null, new[] { HttpStatusCode.Forbidden });
            await SendRawAsync(evidence, client, "DoctorMessages", "/api/doctor/messages/today", HttpMethod.Get, "Wrong role denied for doctor inbox", "Company", "/api/doctor/messages/today", null, tokens["Company"], null, new[] { HttpStatusCode.Forbidden });
        }

        private static async Task RunAdminAccountCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state)
        {
            await SendRawAsync(evidence, client, "AdminAccounts", "/api/admin/pending-accounts", HttpMethod.Get, "Pending accounts default page", "Admin", "/api/admin/pending-accounts", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "AdminAccounts", "/api/admin/pending-accounts", HttpMethod.Get, "Pending accounts invalid page size", "Admin", "/api/admin/pending-accounts?pageNumber=1&pageSize=0", null, tokens["Admin"], null, new[] { HttpStatusCode.BadRequest, HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminAccounts", $"/api/admin/accounts/{state.DoctorUserId}/decision", HttpMethod.Put, "Duplicate approve already approved account", "Admin", new { Decision = "Approve", Notes = "E2E duplicate approval check" }, tokens["Admin"], new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest, HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminAccounts", "/api/admin/accounts/not-a-real-user/decision", HttpMethod.Put, "Account decision nonexistent user", "Admin", new { Decision = "Approve", Notes = "E2E nonexistent user" }, tokens["Admin"], new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var rejectEmail = $"reject-{suffix}@medibridge.local";
            var rejectPhone = $"55577{Random.Shared.Next(10000, 99999)}";
            var registration = await SendJsonAsync(evidence, client, "Auth", "/api/auth/register-doctor", HttpMethod.Post, "Register doctor for rejection workflow", "Anonymous", NewDoctorRegistration(rejectEmail, rejectPhone, suffix), null, new[] { HttpStatusCode.Created });
            var rejectUserId = GetEnvelopeDataString(registration.Body, "UserId");
            if (!string.IsNullOrWhiteSpace(rejectUserId))
            {
                var rejection = await SendJsonAsync(evidence, client, "AdminAccounts", $"/api/admin/accounts/{rejectUserId}/decision", HttpMethod.Put, "Admin rejection workflow", "Admin", new { Decision = "Reject", Reason = "E2E rejection reason", Notes = "E2E rejection note" }, tokens["Admin"], new[] { HttpStatusCode.OK });
                var resubmissionToken = GetEnvelopeDataString(rejection.Body, "ResubmissionToken");
                await SendJsonAsync(evidence, client, "Auth", "/api/auth/login", HttpMethod.Post, "Rejected account login denied", "Anonymous", new { Username = rejectEmail, Password = "Password1!" }, null, new[] { HttpStatusCode.Forbidden });
                if (!string.IsNullOrWhiteSpace(resubmissionToken))
                {
                    await SendJsonAsync(evidence, client, "Auth", "/api/auth/resubmit-registration", HttpMethod.Post, "Rejected account resubmission", "Anonymous", new { ResubmissionToken = resubmissionToken, Specialization = "Internal Medicine", ExperienceYears = 11, Location = "Giza", VerificationMetadata = Verification("License", $"resub-{suffix}.pdf", $"resub-ref-{suffix}") }, null, new[] { HttpStatusCode.OK });
                }
            }
        }

        private static async Task RunFileCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state, string connectionString)
        {
            var pdf = Encoding.UTF8.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\n%%EOF");
            var upload = await SendMultipartAsync(evidence, client, "Files", "/api/files/verification-documents", "Verification upload to Cloudinary", "Doctor", "/api/files/verification-documents", tokens["Doctor"], new Dictionary<string, (byte[] Bytes, string FileName, string ContentType)> { ["File"] = (pdf, "doctor-e2e-license.pdf", "application/pdf") }, null, new[] { HttpStatusCode.Created });
            var fileId = GetEnvelopeDataString(upload.Body, "Id");
            if (!string.IsNullOrWhiteSpace(fileId))
            {
                evidence.TestContext["DoctorVerificationFileId"] = fileId;
                await SendRawAsync(evidence, client, "AdminFiles", "/api/admin/files/pending", HttpMethod.Get, "Admin pending files includes upload queue", "Admin", "/api/admin/files/pending?pageNumber=1&pageSize=20", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
                await SendJsonAsync(evidence, client, "AdminFiles", $"/api/admin/files/{fileId}/review", HttpMethod.Put, "Admin approves verification file", "Admin", new { Decision = "Approved", Reason = "E2E approved", Notes = "E2E file review" }, tokens["Admin"], new[] { HttpStatusCode.OK });
                await SendRawAsync(evidence, client, "AdminFiles", "/api/admin/files/{fileId}/reviews", HttpMethod.Get, "Admin reads file review history", "Admin", $"/api/admin/files/{fileId}/reviews", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
                await SendRawAsync(evidence, client, "Files", "/api/files/{fileId}", HttpMethod.Get, "Owner signed file access", "Doctor", $"/api/files/{fileId}", null, tokens["Doctor"], null, new[] { HttpStatusCode.OK, HttpStatusCode.Forbidden });
                await SendRawAsync(evidence, client, "Files", "/api/files/{fileId}", HttpMethod.Get, "Cross-role file access denied", "Company", $"/api/files/{fileId}", null, tokens["Company"], null, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });

                await SendMultipartAsync(evidence, client, "Files", "/api/files/{fileId}/replacement", "Approved verification file replacement blocked", "Doctor", $"/api/files/{fileId}/replacement", tokens["Doctor"], new Dictionary<string, (byte[] Bytes, string FileName, string ContentType)> { ["File"] = (pdf, "doctor-e2e-license-replacement.pdf", "application/pdf") }, null, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Conflict });

                var provider = await ScalarStringAsync(connectionString, "SELECT TOP 1 StorageProvider FROM StoredFiles WHERE Id = @id", new() { ["@id"] = fileId });
                var uploadStatus = await ScalarStringAsync(connectionString, "SELECT TOP 1 CAST(UploadStatus AS nvarchar(20)) FROM StoredFiles WHERE Id = @id", new() { ["@id"] = fileId });
                evidence.DatabaseEffects.Add(new DatabaseEffect("Cloudinary verification file", $"StoredFile {fileId} provider={provider ?? "unknown"} uploadStatus={uploadStatus ?? "unknown"}"));
            }

            var replacementRequested = await SendMultipartAsync(evidence, client, "Files", "/api/files/verification-documents", "Upload file for replacement-request workflow", "Doctor", "/api/files/verification-documents", tokens["Doctor"], new Dictionary<string, (byte[] Bytes, string FileName, string ContentType)> { ["File"] = (pdf, "doctor-e2e-replacement-request.pdf", "application/pdf") }, null, new[] { HttpStatusCode.Created });
            var replacementRequestedFileId = GetEnvelopeDataString(replacementRequested.Body, "Id");
            if (!string.IsNullOrWhiteSpace(replacementRequestedFileId))
            {
                await SendJsonAsync(evidence, client, "AdminFiles", $"/api/admin/files/{replacementRequestedFileId}/review", HttpMethod.Put, "Admin requests file replacement", "Admin", new { Decision = "ReplacementRequested", Reason = "E2E replacement required", Notes = "E2E replacement note" }, tokens["Admin"], new[] { HttpStatusCode.OK });
                var replacement = await SendMultipartAsync(evidence, client, "Files", "/api/files/{fileId}/replacement", "Owner replaces file after replacement request", "Doctor", $"/api/files/{replacementRequestedFileId}/replacement", tokens["Doctor"], new Dictionary<string, (byte[] Bytes, string FileName, string ContentType)> { ["File"] = (pdf, "doctor-e2e-requested-replacement.pdf", "application/pdf") }, null, new[] { HttpStatusCode.Created });
                var replacementId = GetEnvelopeDataString(replacement.Body, "Id");
                if (!string.IsNullOrWhiteSpace(replacementId))
                {
                    await SendRawAsync(evidence, client, "Files", "/api/files/{fileId}/access", HttpMethod.Post, "POST owner signed file access", "Doctor", $"/api/files/{replacementId}/access", null, tokens["Doctor"], null, new[] { HttpStatusCode.OK, HttpStatusCode.Forbidden });
                    await SendRawAsync(evidence, client, "Files", "/api/files/{fileId}", HttpMethod.Delete, "Owner deletes replacement file", "Doctor", $"/api/files/{replacementId}", null, tokens["Doctor"], null, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Conflict });
                }
            }
            await SendMultipartAsync(evidence, client, "Files", "/api/files/verification-documents", "Invalid empty verification upload", "Doctor", "/api/files/verification-documents", tokens["Doctor"], new Dictionary<string, (byte[] Bytes, string FileName, string ContentType)> { ["File"] = (Array.Empty<byte>(), "empty.pdf", "application/pdf") }, null, new[] { HttpStatusCode.BadRequest });

            if (!string.IsNullOrWhiteSpace(state.CampaignId) && !string.IsNullOrWhiteSpace(state.FileId))
            {
                await SendMultipartAsync(evidence, client, "Files", "/api/company/campaigns/{campaignId}/assets/{assetId}/replacement", "Campaign asset replacement blocked after approval", "Company", $"/api/company/campaigns/{state.CampaignId}/assets/{state.FileId}/replacement", tokens["Company"], new Dictionary<string, (byte[] Bytes, string FileName, string ContentType)> { ["file"] = (new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, "replacement.png", "image/png") }, null, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Conflict, HttpStatusCode.BadRequest });
                await SendRawAsync(evidence, client, "Files", "/api/company/campaigns/{campaignId}/assets/{assetId}", HttpMethod.Delete, "Campaign asset delete blocked after approval", "Company", $"/api/company/campaigns/{state.CampaignId}/assets/{state.FileId}", null, tokens["Company"], null, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Conflict });
            }
        }

        private static async Task RunPricingCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state)
        {
            await SendRawAsync(evidence, client, "AdminPricing", "/api/admin/doctors/{doctorId}/delivery-settings", HttpMethod.Get, "Read doctor delivery settings", "Admin", $"/api/admin/doctors/{state.DoctorProfileId}/delivery-settings", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminPricing", $"/api/admin/doctors/{state.DoctorProfileId}/delivery-settings", HttpMethod.Put, "Invalid doctor delivery settings", "Admin", new { DailyMessageLimit = 0, MinimumWeeklyRequirement = 999, Reason = "E2E invalid settings" }, tokens["Admin"], new[] { HttpStatusCode.BadRequest });
            await SendJsonAsync(evidence, client, "AdminPricing", $"/api/admin/doctors/{state.DoctorProfileId}/delivery-settings", HttpMethod.Put, "Set doctor delivery settings", "Admin", new { DailyMessageLimit = 5, MinimumWeeklyRequirement = 1, Reason = "E2E settings" }, tokens["Admin"], new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminPricing", $"/api/admin/doctors/{state.DoctorProfileId}/price/deactivate", HttpMethod.Put, "Deactivate doctor pricing", "Admin", new { reason = "E2E deactivation test" }, tokens["Admin"], new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            await SendJsonAsync(evidence, client, "AdminPricing", $"/api/admin/doctors/{state.DoctorProfileId}/price", HttpMethod.Put, "Reactivate doctor pricing", "Admin", new { PricePerMessage = 75.00m, Reason = "E2E reactivate pricing" }, tokens["Admin"], new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminPricing", $"/api/admin/doctors/{state.DoctorProfileId}/price", HttpMethod.Put, "Invalid doctor price boundary", "Admin", new { PricePerMessage = 0, Reason = "E2E invalid price" }, tokens["Admin"], new[] { HttpStatusCode.BadRequest });
            await SendRawAsync(evidence, client, "AdminPlatformFeePolicy", "/api/admin/platform-fee-policy/current", HttpMethod.Get, "Read current platform fee", "Admin", "/api/admin/platform-fee-policy/current", null, tokens["Admin"], null, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
            await SendJsonAsync(evidence, client, "AdminPlatformFeePolicy", "/api/admin/platform-fee-policy", HttpMethod.Put, "Invalid platform fee rejected", "Admin", new { FeePercent = 101, Reason = "E2E invalid fee" }, tokens["Admin"], new[] { HttpStatusCode.BadRequest });
        }

        private static async Task RunWalletCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state, string connectionString)
        {
            var before = await ScalarDecimalAsync(connectionString, "SELECT AvailableBalance FROM Wallets WHERE Id = @id", new() { ["@id"] = state.WalletId });
            await SendRawAsync(evidence, client, "CompanyWallet", "/api/company/wallet", HttpMethod.Get, "Company wallet pagination default", "Company", "/api/company/wallet?PageNumber=1&PageSize=20", null, tokens["Company"], null, new[] { HttpStatusCode.OK });
            var key = $"topup-{Guid.NewGuid():N}";
            await SendJsonAsync(evidence, client, "CompanyWallet", "/api/company/wallet/topup", HttpMethod.Post, "Company wallet top-up", "Company", new { Amount = 125.50m, Description = "E2E top-up" }, tokens["Company"], new[] { HttpStatusCode.OK }, key);
            await SendJsonAsync(evidence, client, "CompanyWallet", "/api/company/wallet/topup", HttpMethod.Post, "Company wallet top-up replay", "Company", new { Amount = 125.50m, Description = "E2E top-up" }, tokens["Company"], new[] { HttpStatusCode.OK }, key);
            await SendJsonAsync(evidence, client, "CompanyWallet", "/api/company/wallet/topup", HttpMethod.Post, "Company wallet top-up idempotency conflict", "Company", new { Amount = 126.50m, Description = "E2E changed top-up" }, tokens["Company"], new[] { HttpStatusCode.Conflict }, key);
            await SendJsonAsync(evidence, client, "CompanyWallet", "/api/company/wallet/topup", HttpMethod.Post, "Missing idempotency key rejected", "Company", new { Amount = 1m, Description = "Missing key" }, tokens["Company"], new[] { HttpStatusCode.BadRequest });
            await SendJsonAsync(evidence, client, "CompanyWallet", "/api/company/wallet/mock-checkout", HttpMethod.Post, "Mock checkout invalid amount", "Company", new { amount = -1, currency = "EGP" }, tokens["Company"], new[] { HttpStatusCode.BadRequest }, $"mock-invalid-{Guid.NewGuid():N}");
            var after = await ScalarDecimalAsync(connectionString, "SELECT AvailableBalance FROM Wallets WHERE Id = @id", new() { ["@id"] = state.WalletId });
            evidence.DatabaseEffects.Add(new DatabaseEffect("Company wallet top-up", $"AvailableBalance before={before}; after={after}; replay did not create a second expected increment."));
        }

        private static async Task RunCampaignCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state, string connectionString)
        {
            var draft = await SendJsonAsync(evidence, client, "CompanyCampaignDrafts", "/api/company/campaigns/drafts", HttpMethod.Post, "Create secondary draft for route coverage", "Company", new { Title = "Secondary E2E Draft", Description = "Secondary draft for legacy upload coverage.", ClinicalResearchInfo = "Non-production E2E draft." }, tokens["Company"], new[] { HttpStatusCode.Created });
            var secondaryDraftId = GetEnvelopeDataString(draft.Body, "CampaignId");
            if (!string.IsNullOrWhiteSpace(secondaryDraftId))
            {
                await SendMultipartAsync(evidence, client, "Files", "/api/campaigns/{campaignId}/files", "Legacy campaign file upload route", "Company", $"/api/campaigns/{secondaryDraftId}/files", tokens["Company"], new Dictionary<string, (byte[] Bytes, string FileName, string ContentType)> { ["File"] = (new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, "legacy-campaign-media.png", "image/png") }, new Dictionary<string, string> { ["Purpose"] = "CampaignMedia" }, new[] { HttpStatusCode.Created });
            }
            await SendRawAsync(evidence, client, "CompanyDoctors", "/api/company/doctors", HttpMethod.Get, "Company doctor search filters", "Company", "/api/company/doctors?PageNumber=1&PageSize=20&Specialization=Cardiology&Location=Cairo&MinPrice=1&MaxPrice=100", null, tokens["Company"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns", HttpMethod.Get, "Company campaign list filters", "Company", "/api/company/campaigns?PageNumber=1&PageSize=20&status=Approved", null, tokens["Company"], null, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}", HttpMethod.Get, "Company reads campaign detail", "Company", $"/api/company/campaigns/{state.CampaignId}", null, tokens["Company"], null, new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "CompanyCampaigns", $"/api/company/campaigns/{state.CampaignId}", HttpMethod.Put, "Update approved campaign blocked", "Company", new { title = "E2E blocked update", description = "Blocked update", clinicalResearchInfo = "Blocked" }, tokens["Company"], new[] { HttpStatusCode.Conflict, HttpStatusCode.Forbidden });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}/target-preview", HttpMethod.Get, "Company target preview", "Company", $"/api/company/campaigns/{state.CampaignId}/target-preview", null, tokens["Company"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}/queue-summary", HttpMethod.Get, "Company queue summary", "Company", $"/api/company/campaigns/{state.CampaignId}/queue-summary", null, tokens["Company"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}/deliveries", HttpMethod.Get, "Company delivery report empty before injector", "Company", $"/api/company/campaigns/{state.CampaignId}/deliveries?PageNumber=1&PageSize=20", null, tokens["Company"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}/feedback", HttpMethod.Get, "Company feedback report", "Company", $"/api/company/campaigns/{state.CampaignId}/feedback?PageNumber=1&PageSize=20", null, tokens["Company"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}/analytics", HttpMethod.Get, "Company analytics", "Company", $"/api/company/campaigns/{state.CampaignId}/analytics", null, tokens["Company"], null, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}/review-outcome", HttpMethod.Get, "Company review outcome", "Company", $"/api/company/campaigns/{state.CampaignId}/review-outcome", null, tokens["Company"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "AdminCampaigns", "/api/admin/campaigns/pending-review", HttpMethod.Get, "Admin pending campaign review list", "Admin", "/api/admin/campaigns/pending-review?pageNumber=1&pageSize=20", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "AdminCampaigns", "/api/admin/campaigns/{campaignId}/review-detail", HttpMethod.Get, "Admin campaign review detail", "Admin", $"/api/admin/campaigns/{state.CampaignId}/review-detail", null, tokens["Admin"], null, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Conflict, HttpStatusCode.ServiceUnavailable });
            await SendRawAsync(evidence, client, "AdminCampaigns", "/api/admin/campaigns/{campaignId}/queue", HttpMethod.Get, "Admin campaign queue rows", "Admin", $"/api/admin/campaigns/{state.CampaignId}/queue", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminCampaigns", $"/api/admin/campaigns/{state.CampaignId}/review", HttpMethod.Post, "Duplicate campaign approval transition", "Admin", new { decision = "Approved", notes = "E2E duplicate campaign approval" }, tokens["Admin"], new[] { HttpStatusCode.Conflict, HttpStatusCode.OK }, $"campaign-review-{Guid.NewGuid():N}");
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}/submit", HttpMethod.Post, "Submit already approved campaign blocked", "Company", $"/api/company/campaigns/{state.CampaignId}/submit", null, tokens["Company"], $"submit-again-{Guid.NewGuid():N}", new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest });
            var queueCount = await ScalarIntAsync(connectionString, "SELECT COUNT(*) FROM DoctorMessageQueues WHERE CampaignId = @id", new() { ["@id"] = state.CampaignId });
            evidence.DatabaseEffects.Add(new DatabaseEffect("Campaign approval queue", $"DoctorMessageQueues for campaign {state.CampaignId}: {queueCount}"));
        }

        private static async Task<SettlementCampaignIds> CreateSettlementCampaignAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state, string connectionString)
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var draft = await SendJsonAsync(evidence, client, "CompanyCampaignDrafts", "/api/company/campaigns/drafts", HttpMethod.Post, "Create settlement campaign draft", "Company", new
            {
                Title = $"Settlement E2E Campaign {suffix}",
                Description = "Settlement campaign created to verify real delivery, interaction, earnings, and withdrawal flow.",
                ClinicalResearchInfo = "E2E settlement verification over a live HTTP workflow."
            }, tokens["Company"], new[] { HttpStatusCode.Created });
            var campaignId = GetEnvelopeDataString(draft.Body, "CampaignId");
            if (string.IsNullOrWhiteSpace(campaignId))
            {
                return new SettlementCampaignIds(string.Empty, string.Empty);
            }

            var asset = await SendMultipartAsync(evidence, client, "Files", "/api/company/campaigns/{campaignId}/assets", "Upload settlement campaign asset", "Company", $"/api/company/campaigns/{campaignId}/assets", tokens["Company"], new Dictionary<string, (byte[] Bytes, string FileName, string ContentType)>
            {
                ["file"] = (Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lV9h6QAAAABJRU5ErkJggg=="), $"settlement-{suffix}.png", "image/png")
            }, null, new[] { HttpStatusCode.Created });
            var assetId = GetEnvelopeDataString(asset.Body, "Id");
            if (string.IsNullOrWhiteSpace(assetId))
            {
                return new SettlementCampaignIds(campaignId, string.Empty);
            }

            await SendJsonAsync(evidence, client, "AdminFiles", $"/api/admin/campaign-assets/{assetId}/review", HttpMethod.Post, "Approve settlement campaign asset", "Admin", new { Decision = "Approved", Notes = "E2E settlement campaign asset approved." }, tokens["Admin"], new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}/target-preview", HttpMethod.Get, "Settlement campaign target preview", "Company", $"/api/company/campaigns/{campaignId}/target-preview", null, tokens["Company"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "CompanyCampaigns", "/api/company/campaigns/{campaignId}/submit", HttpMethod.Post, "Submit settlement campaign", "Company", $"/api/company/campaigns/{campaignId}/submit", null, tokens["Company"], $"settlement-submit-{Guid.NewGuid():N}", new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminCampaigns", $"/api/admin/campaigns/{campaignId}/review", HttpMethod.Post, "Approve settlement campaign", "Admin", new { Decision = "Approved", Notes = "E2E settlement campaign approved." }, tokens["Admin"], new[] { HttpStatusCode.OK }, $"settlement-review-{Guid.NewGuid():N}");

            var queueForDoctor = await ScalarIntAsync(
                connectionString,
                "SELECT COUNT(*) FROM DoctorMessageQueues WHERE CampaignId = @campaignId AND DoctorId = @doctorId",
                new() { ["@campaignId"] = campaignId, ["@doctorId"] = state.DoctorProfileId });
            evidence.DatabaseEffects.Add(new DatabaseEffect("Settlement campaign queue", $"DoctorMessageQueues for settlement campaign {campaignId} and E2E doctor {state.DoctorProfileId}: {queueForDoctor}"));
            return new SettlementCampaignIds(campaignId, assetId);
        }

        private static async Task RunJobAndDeliveryCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state, string connectionString)
        {
            await SendJsonAsync(evidence, client, "AdminActivityEnforcement", $"/api/admin/doctors/{state.DoctorProfileId}/status", HttpMethod.Put, "Ensure doctor active before settlement campaign", "Admin", new { ActionType = "Reactivate", Reason = "E2E ensure active before settlement campaign" }, tokens["Admin"], new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest });
            await SendJsonAsync(evidence, client, "AdminPricing", $"/api/admin/doctors/{state.DoctorProfileId}/delivery-settings", HttpMethod.Put, "Raise doctor daily capacity for settlement campaign", "Admin", new { DailyMessageLimit = 50, MinimumWeeklyRequirement = 1, Reason = "E2E settlement capacity" }, tokens["Admin"], new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "CompanyWallet", "/api/company/wallet/topup", HttpMethod.Post, "Fund settlement campaign wallet capacity", "Company", new { Amount = 5000.00m, Description = "E2E settlement campaign funding" }, tokens["Company"], new[] { HttpStatusCode.OK }, $"settlement-fund-{Guid.NewGuid():N}");

            var settlementCampaign = await CreateSettlementCampaignAsync(evidence, client, tokens, state, connectionString);
            var deliveryCampaignId = string.IsNullOrWhiteSpace(settlementCampaign.CampaignId) ? state.CampaignId : settlementCampaign.CampaignId;
            var deliveryAssetId = string.IsNullOrWhiteSpace(settlementCampaign.AssetId) ? state.FileId : settlementCampaign.AssetId;
            evidence.TestContext["SettlementCampaignId"] = deliveryCampaignId;
            evidence.TestContext["SettlementAssetId"] = deliveryAssetId;

            await SendRawAsync(evidence, client, "AdminDeliveryJobs", "/api/admin/delivery-jobs/status", HttpMethod.Get, "Delivery job status", "Admin", "/api/admin/delivery-jobs/status", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminDeliveryJobs", "/api/admin/delivery-jobs/run-injector", HttpMethod.Post, "Enqueue delivery injector", "Admin", new { Reason = "E2E injector for settlement campaign" }, tokens["Admin"], new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            var deliveryId = await PollDeliveryIdAsync(connectionString, deliveryCampaignId, state.DoctorProfileId);
            if (!string.IsNullOrWhiteSpace(deliveryId))
            {
                evidence.TestContext["DeliveryId"] = deliveryId;
                await SendRawAsync(evidence, client, "DoctorMessages", "/api/doctor/messages/today", HttpMethod.Get, "Doctor inbox with active delivery", "Doctor", "/api/doctor/messages/today?PageSize=20", null, tokens["Doctor"], null, new[] { HttpStatusCode.OK });
                await SendRawAsync(evidence, client, "DoctorMessages", "/api/doctor/messages/{deliveryId}/assets/{fileId}/access", HttpMethod.Get, "Doctor delivery asset signed access", "Doctor", $"/api/doctor/messages/{deliveryId}/assets/{deliveryAssetId}/access", null, tokens["Doctor"], null, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Forbidden });
                await SendRawAsync(evidence, client, "DoctorMessages", "/api/doctor/messages/{deliveryId}/read", HttpMethod.Put, "Doctor read tracking", "Doctor", $"/api/doctor/messages/{deliveryId}/read", null, tokens["Doctor"], null, new[] { HttpStatusCode.OK });
                var key = $"interact-{Guid.NewGuid():N}";
                await SendJsonAsync(evidence, client, "DoctorMessages", $"/api/doctor/messages/{deliveryId}/interact", HttpMethod.Post, "Doctor accepts delivery", "Doctor", new { Outcome = "Accept", Feedback = "E2E accepted interaction with concise feedback." }, tokens["Doctor"], new[] { HttpStatusCode.OK }, key);
                await SendJsonAsync(evidence, client, "DoctorMessages", $"/api/doctor/messages/{deliveryId}/interact", HttpMethod.Post, "Doctor interaction replay", "Doctor", new { Outcome = "Accept", Feedback = "E2E accepted interaction with concise feedback." }, tokens["Doctor"], new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, key);
                await SendJsonAsync(evidence, client, "DoctorMessages", $"/api/doctor/messages/{deliveryId}/interact", HttpMethod.Post, "Doctor interaction idempotency conflict", "Doctor", new { Outcome = "Reject", Feedback = "Changed outcome for conflict." }, tokens["Doctor"], new[] { HttpStatusCode.Conflict }, key);
            }
            else
            {
                await SendRawAsync(evidence, client, "DoctorMessages", "/api/doctor/messages/{deliveryId}/assets/{fileId}/access", HttpMethod.Get, "Doctor delivery asset missing delivery guard", "Doctor", $"/api/doctor/messages/missing-delivery-id/assets/{deliveryAssetId}/access", null, tokens["Doctor"], null, new[] { HttpStatusCode.OK });
                await SendRawAsync(evidence, client, "DoctorMessages", "/api/doctor/messages/{deliveryId}/read", HttpMethod.Put, "Doctor read missing delivery guard", "Doctor", "/api/doctor/messages/missing-delivery-id/read", null, tokens["Doctor"], null, new[] { HttpStatusCode.OK });
                await SendJsonAsync(evidence, client, "DoctorMessages", "/api/doctor/messages/missing-delivery-id/interact", HttpMethod.Post, "Doctor interaction missing delivery guard", "Doctor", new { Outcome = "Accept", Feedback = "E2E missing delivery guard feedback." }, tokens["Doctor"], new[] { HttpStatusCode.OK }, $"missing-delivery-{Guid.NewGuid():N}");
            }

            await SendRawAsync(evidence, client, "AdminActivityJobs", "/api/admin/activity-jobs/status", HttpMethod.Get, "Activity job status", "Admin", "/api/admin/activity-jobs/status?take=10", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminActivityJobs", "/api/admin/activity-jobs/run-score", HttpMethod.Post, "Run daily activity score for future empty date", "Admin", new { ScoreDateEgypt = "2099-01-01" }, tokens["Admin"], new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
            await SendJsonAsync(evidence, client, "AdminActivityJobs", "/api/admin/activity-jobs/run-weekly-enforcement", HttpMethod.Post, "Run weekly enforcement for future empty week", "Admin", new { WeekStartDateEgypt = "2099-01-05" }, tokens["Admin"], new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
            await SendJsonAsync(evidence, client, "AdminDeliveryJobs", "/api/admin/delivery-jobs/run-expiry", HttpMethod.Post, "Run expiry cleaner", "Admin", new { Reason = "E2E expiry job coverage" }, tokens["Admin"], new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            await SendJsonAsync(evidence, client, "AdminActivityJobs", "/api/admin/activity-jobs/run-suspension-expiry", HttpMethod.Post, "Run suspension expiry", "Admin", new { Reason = "E2E suspension expiry coverage" }, tokens["Admin"], new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        }

        private static async Task RunWithdrawalCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state, string connectionString)
        {
            await SendRawAsync(evidence, client, "DoctorWithdrawals", "/api/doctor/withdrawals", HttpMethod.Get, "Doctor withdrawal list default", "Doctor", "/api/doctor/withdrawals?pageNumber=1&pageSize=20", null, tokens["Doctor"], null, new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "DoctorWithdrawals", "/api/doctor/withdrawals", HttpMethod.Post, "Doctor withdrawal invalid amount", "Doctor", new { amount = -1 }, tokens["Doctor"], new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
            var available = await ScalarDecimalAsync(
                connectionString,
                "SELECT COALESCE(MAX(AvailableBalance), 0) FROM Wallets WHERE OwnerType = 1 AND OwnerId = @doctorId AND IsDeleted = CAST(0 AS bit)",
                new() { ["@doctorId"] = state.DoctorProfileId });
            evidence.DatabaseEffects.Add(new DatabaseEffect("Doctor settled wallet balance", $"Doctor wallet available balance before withdrawal workflow: {available} EGP."));
            var amount = available >= 3m ? 1m : 0m;
            if (amount <= 0m)
            {
                await SendJsonAsync(evidence, client, "DoctorWithdrawals", "/api/doctor/withdrawals", HttpMethod.Post, "Valid withdrawal requires settled earnings guard", "Doctor", new { amount = 1m }, tokens["Doctor"], new[] { HttpStatusCode.Created });
                return;
            }

            async Task<string> CreateWithdrawalAsync(string scenario)
            {
                var created = await SendJsonAsync(evidence, client, "DoctorWithdrawals", "/api/doctor/withdrawals", HttpMethod.Post, scenario, "Doctor", new { amount }, tokens["Doctor"], new[] { HttpStatusCode.Created });
                return GetEnvelopeDataString(created.Body, "withdrawalId");
            }

            var rejectWithdrawalId = await CreateWithdrawalAsync("Doctor creates withdrawal for rejection");
            evidence.TestContext["RejectedWithdrawalId"] = rejectWithdrawalId;
            await SendRawAsync(evidence, client, "AdminWithdrawals", "/api/admin/withdrawals", HttpMethod.Get, "Admin withdrawal list filtered", "Admin", $"/api/admin/withdrawals?pageNumber=1&pageSize=20&doctorId={state.DoctorProfileId}", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminWithdrawals", $"/api/admin/withdrawals/{rejectWithdrawalId}/reject", HttpMethod.Put, "Admin rejects withdrawal and releases hold", "Admin", new { reason = "E2E rejected withdrawal details", note = "E2E rejection" }, tokens["Admin"], new[] { HttpStatusCode.OK });

            var paidWithdrawalId = await CreateWithdrawalAsync("Doctor creates withdrawal for paid payout");
            evidence.TestContext["PaidWithdrawalId"] = paidWithdrawalId;
            await SendJsonAsync(evidence, client, "AdminWithdrawals", $"/api/admin/withdrawals/{paidWithdrawalId}/approve", HttpMethod.Put, "Admin approves withdrawal for payout", "Admin", new { note = "E2E approval" }, tokens["Admin"], new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminWithdrawals", $"/api/admin/withdrawals/{paidWithdrawalId}/mark-paid", HttpMethod.Put, "Admin marks withdrawal paid", "Admin", new { payoutReference = $"E2E-PAYOUT-{Guid.NewGuid():N}" }, tokens["Admin"], new[] { HttpStatusCode.OK });

            var failedWithdrawalId = await CreateWithdrawalAsync("Doctor creates withdrawal for failed payout");
            evidence.TestContext["FailedWithdrawalId"] = failedWithdrawalId;
            await SendJsonAsync(evidence, client, "AdminWithdrawals", $"/api/admin/withdrawals/{failedWithdrawalId}/approve", HttpMethod.Put, "Admin approves withdrawal before failure", "Admin", new { note = "E2E approval before failed payout" }, tokens["Admin"], new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminWithdrawals", $"/api/admin/withdrawals/{failedWithdrawalId}/mark-failed", HttpMethod.Put, "Admin marks withdrawal failed and releases hold", "Admin", new { reason = "E2E bank payout rejected" }, tokens["Admin"], new[] { HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminWithdrawals", $"/api/admin/withdrawals/{paidWithdrawalId}/mark-failed", HttpMethod.Put, "Invalid paid withdrawal failure transition", "Admin", new { reason = "E2E invalid transition" }, tokens["Admin"], new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest });
        }

        private static async Task RunAdminReadCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state)
        {
            await SendRawAsync(evidence, client, "AdminStatistics", "/api/admin/statistics", HttpMethod.Get, "Admin statistics default", "Admin", "/api/admin/statistics", null, tokens["Admin"], null, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            await SendRawAsync(evidence, client, "AdminStatistics", "/api/admin/statistics", HttpMethod.Get, "Admin statistics invalid range", "Admin", "/api/admin/statistics?fromDateEgypt=2026-01-01&toDateEgypt=2026-07-13", null, tokens["Admin"], null, new[] { HttpStatusCode.BadRequest });
            await SendRawAsync(evidence, client, "AdminWorkQueue", "/api/admin/work-queue", HttpMethod.Get, "Admin work queue default", "Admin", "/api/admin/work-queue?PageNumber=1&PageSize=20", null, tokens["Admin"], null, new[] { HttpStatusCode.OK });
            await SendRawAsync(evidence, client, "AdminWorkQueue", "/api/admin/work-queue", HttpMethod.Get, "Admin work queue filtered", "Admin", "/api/admin/work-queue?PageNumber=1&PageSize=100&category=Withdrawal&status=Requested", null, tokens["Admin"], null, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
            await SendRawAsync(evidence, client, "AdminActivityEnforcement", "/api/admin/violations", HttpMethod.Get, "Admin violations filters", "Admin", $"/api/admin/violations?doctorId={state.DoctorProfileId}&PageNumber=1&PageSize=20", null, tokens["Admin"], null, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest });
            await SendRawAsync(evidence, client, "WeatherForecast", "/WeatherForecast", HttpMethod.Get, "Weather public sample endpoint", "Anonymous", "/WeatherForecast?count=3", null, null, null, new[] { HttpStatusCode.OK });
        }

        private static async Task RunDoctorStatusCoverageAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state)
        {
            var suspendUntil = DateTime.UtcNow.AddHours(2).ToString("O");
            await SendJsonAsync(evidence, client, "AdminActivityEnforcement", $"/api/admin/doctors/{state.DoctorProfileId}/status", HttpMethod.Put, "Suspend doctor status", "Admin", new { ActionType = "Suspend", Reason = "E2E suspension test", SuspendedUntilUtc = suspendUntil }, tokens["Admin"], new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest });
            await SendJsonAsync(evidence, client, "Auth", "/api/auth/login", HttpMethod.Post, "Suspended doctor login denied or token unaffected", "Anonymous", new { Username = state.DoctorEmail, Password = state.DoctorPassword }, null, new[] { HttpStatusCode.Forbidden, HttpStatusCode.OK });
            await SendJsonAsync(evidence, client, "AdminActivityEnforcement", $"/api/admin/doctors/{state.DoctorProfileId}/status", HttpMethod.Put, "Reactivate doctor status", "Admin", new { ActionType = "Reactivate", Reason = "E2E reactivate after suspension test" }, tokens["Admin"], new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest });
            await SendJsonAsync(evidence, client, "AdminActivityEnforcement", $"/api/admin/doctors/{state.DoctorProfileId}/status", HttpMethod.Put, "Invalid doctor status transition payload", "Admin", new { ActionType = "InvalidStatus", Reason = "" }, tokens["Admin"], new[] { HttpStatusCode.BadRequest });
        }

        private static async Task MarkUntestedEndpointsAsync(LiveE2EEvidence evidence, HttpClient client, Dictionary<string, string> tokens, E2EState state)
        {
            foreach (var endpoint in evidence.Endpoints.Where(e => e.Status == "untested").ToList())
            {
                if (endpoint.Method == "GET")
                {
                    var role = RoleForPath(endpoint.Path);
                    var token = role != "Anonymous" && tokens.TryGetValue(role, out var found) ? found : null;
                    var resolved = ResolvePath(endpoint.Path, state, evidence.TestContext.TryGetValue("DeliveryId", out var deliveryId) ? deliveryId : null, evidence.TestContext.TryGetValue("PaidWithdrawalId", out var withdrawalId) ? withdrawalId : null);
                    await SendRawAsync(evidence, client, endpoint.Controller, endpoint.Path, HttpMethod.Get, "Swagger inventory fallback GET", role, resolved, null, token, null, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.Conflict, HttpStatusCode.ServiceUnavailable });
                }
                else
                {
                    var method = new HttpMethod(endpoint.Method);
                    var role = RoleForPath(endpoint.Path);
                    var token = role != "Anonymous" && tokens.TryGetValue(role, out var found) ? found : null;
                    var resolved = ResolvePath(
                        endpoint.Path,
                        state,
                        evidence.TestContext.TryGetValue("DeliveryId", out var deliveryId) ? deliveryId : null,
                        evidence.TestContext.TryGetValue("PaidWithdrawalId", out var withdrawalId) ? withdrawalId : null);
                    await SendRawAsync(
                        evidence,
                        client,
                        endpoint.Controller,
                        endpoint.Path,
                        method,
                        "Swagger inventory fallback validation request",
                        role,
                        resolved,
                        method == HttpMethod.Delete ? null : new { },
                        token,
                        $"swagger-fallback-{Guid.NewGuid():N}",
                        new[]
                        {
                            HttpStatusCode.OK,
                            HttpStatusCode.Created,
                            HttpStatusCode.BadRequest,
                            HttpStatusCode.Unauthorized,
                            HttpStatusCode.Forbidden,
                            HttpStatusCode.NotFound,
                            HttpStatusCode.Conflict,
                            HttpStatusCode.UnsupportedMediaType,
                            HttpStatusCode.ServiceUnavailable
                        });
                }
            }
        }

        private static async Task<RecordedHttp> SendJsonAsync(LiveE2EEvidence evidence, HttpClient client, string controller, string endpointPath, HttpMethod method, string scenario, string role, object? body, string? bearer, HttpStatusCode[] expected, string? idempotencyKey = null)
        {
            var response = await SendJsonRequestWithRetryAsync(client, method, endpointPath, body, bearer, idempotencyKey, expected);
            await RecordAsync(evidence, client, controller, TemplateFromConcrete(endpointPath, evidence), method, scenario, role, body, response, expected, "Live HTTP JSON request.", new() { ["idempotencyKeyUsed"] = idempotencyKey is null ? "false" : "true" });
            return new RecordedHttp(response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static async Task<RecordedHttp> SendRawAsync(LiveE2EEvidence evidence, HttpClient client, string controller, string templatePath, HttpMethod method, string scenario, string role, string path, object? body, string? bearer, string? idempotencyKey, HttpStatusCode[] expected)
        {
            var response = await SendJsonRequestWithRetryAsync(client, method, path, body, bearer, idempotencyKey, expected);
            await RecordAsync(evidence, client, controller, templatePath, method, scenario, role, body, response, expected, "Live HTTP request.", new() { ["idempotencyKeyUsed"] = idempotencyKey is null ? "false" : "true" });
            return new RecordedHttp(response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static async Task<RecordedHttp> SendMultipartAsync(LiveE2EEvidence evidence, HttpClient client, string controller, string templatePath, string scenario, string role, string path, string bearer, Dictionary<string, (byte[] Bytes, string FileName, string ContentType)> files, Dictionary<string, string>? fields, HttpStatusCode[] expected)
        {
            var response = await SendMultipartRequestWithRetryAsync(client, path, bearer, files, fields, expected);
            var sanitizedBody = new { multipart = files.ToDictionary(f => f.Key, f => new { f.Value.FileName, f.Value.ContentType, SizeBytes = f.Value.Bytes.Length }), fields };
            await RecordAsync(evidence, client, controller, templatePath, HttpMethod.Post, scenario, role, sanitizedBody, response, expected, "Live HTTP multipart request.", new());
            return new RecordedHttp(response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static async Task<HttpResponseMessage> SendJsonRequestWithRetryAsync(HttpClient client, HttpMethod method, string path, object? body, string? bearer, string? idempotencyKey, HttpStatusCode[] expected)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var request = new HttpRequestMessage(method, path);
                if (bearer is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                }
                if (idempotencyKey is not null)
                {
                    request.Headers.Add("Idempotency-Key", idempotencyKey);
                }
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body);
                }
                var response = await client.SendAsync(request);
                if (response.StatusCode != (HttpStatusCode)429 || expected.Contains((HttpStatusCode)429) || attempt == 1)
                {
                    return response;
                }
                response.Dispose();
                Console.WriteLine("[WAIT] Rate limit hit; waiting 65 seconds before one retry.");
                await Task.Delay(TimeSpan.FromSeconds(65));
            }
            throw new InvalidOperationException("Unreachable retry loop state.");
        }

        private static async Task<HttpResponseMessage> SendMultipartRequestWithRetryAsync(HttpClient client, string path, string bearer, Dictionary<string, (byte[] Bytes, string FileName, string ContentType)> files, Dictionary<string, string>? fields, HttpStatusCode[] expected)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var content = new MultipartFormDataContent();
                if (fields is not null)
                {
                    foreach (var field in fields)
                    {
                        content.Add(new StringContent(field.Value), field.Key);
                    }
                }
                foreach (var file in files)
                {
                    var bytes = new ByteArrayContent(file.Value.Bytes);
                    bytes.Headers.ContentType = MediaTypeHeaderValue.Parse(file.Value.ContentType);
                    content.Add(bytes, file.Key, file.Value.FileName);
                }
                var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                var response = await client.SendAsync(request);
                if (response.StatusCode != (HttpStatusCode)429 || expected.Contains((HttpStatusCode)429) || attempt == 1)
                {
                    return response;
                }
                response.Dispose();
                Console.WriteLine("[WAIT] Rate limit hit; waiting 65 seconds before one retry.");
                await Task.Delay(TimeSpan.FromSeconds(65));
            }
            throw new InvalidOperationException("Unreachable retry loop state.");
        }

        private static async Task RecordAsync(LiveE2EEvidence evidence, HttpClient client, string controller, string endpointPath, HttpMethod method, string scenario, string role, object? requestBody, HttpResponseMessage response, HttpStatusCode expected, string dbEffect, Dictionary<string, string?> ids)
        {
            await RecordAsync(evidence, client, controller, endpointPath, method, scenario, role, requestBody, response, new[] { expected }, dbEffect, ids);
        }

        private static async Task RecordAsync(LiveE2EEvidence evidence, HttpClient client, string controller, string endpointPath, HttpMethod method, string scenario, string role, object? requestBody, HttpResponseMessage response, HttpStatusCode[] expected, string dbEffect, Dictionary<string, string?> ids)
        {
            var body = await response.Content.ReadAsStringAsync();
            var sanitizedResponse = SanitizeEvidence(body);
            var unsafeLeak = ContainsUnsafeLeak(sanitizedResponse);
            var passed = expected.Contains(response.StatusCode) && !unsafeLeak;
            var record = new EvidenceScenario
            {
                Controller = controller,
                Endpoint = endpointPath,
                Method = method.Method,
                ScenarioName = scenario,
                RoleUsed = role,
                SanitizedRequest = SanitizeEvidence(requestBody is null ? "" : JsonSerializer.Serialize(requestBody)),
                HttpStatus = (int)response.StatusCode,
                SanitizedResponse = sanitizedResponse,
                Result = passed ? "pass" : "fail",
                ImportantDatabaseEffect = unsafeLeak ? $"{dbEffect}; unsafe response leakage marker detected." : dbEffect,
                RelatedGeneratedIds = ids
            };
            evidence.Scenarios.Add(record);
            var endpoint = evidence.Endpoints.FirstOrDefault(e => e.Method == method.Method && e.Path == endpointPath);
            if (endpoint is not null)
            {
                endpoint.Status = "tested";
                endpoint.Scenarios.Add(scenario);
            }
            Console.WriteLine($"[{record.Result.ToUpperInvariant()}] {method} {endpointPath} :: {scenario} -> {(int)response.StatusCode}");
        }

        private static void Block(LiveE2EEvidence evidence, string controller, string method, string path, string scenario, string role, string reason)
        {
            evidence.Scenarios.Add(new EvidenceScenario
            {
                Controller = controller,
                Endpoint = path,
                Method = method,
                ScenarioName = scenario,
                RoleUsed = role,
                SanitizedRequest = "",
                HttpStatus = null,
                SanitizedResponse = "",
                Result = "blocked",
                ImportantDatabaseEffect = reason,
                RelatedGeneratedIds = new()
            });
            var endpoint = evidence.Endpoints.FirstOrDefault(e => e.Method == method && e.Path == path);
            if (endpoint is not null && endpoint.Status != "tested")
            {
                endpoint.Status = "blocked";
                endpoint.BlockedReason = reason;
                endpoint.Scenarios.Add(scenario);
            }
            Console.WriteLine($"[BLOCKED] {method} {path} :: {reason}");
        }

        private static async Task<LoginResult> LoginAsync(HttpClient client, string username, string password)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = password });
            var body = await response.Content.ReadAsStringAsync();
            return new LoginResult(response, GetEnvelopeDataString(body, "AccessToken"), GetEnvelopeDataString(body, "RefreshToken"));
        }

        private static async Task<string?> ScalarStringAsync(string connectionString, string sql, Dictionary<string, object?> args)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            foreach (var arg in args)
            {
                cmd.Parameters.AddWithValue(arg.Key, arg.Value ?? DBNull.Value);
            }
            var value = await cmd.ExecuteScalarAsync();
            return value?.ToString();
        }

        private static async Task<int> ScalarIntAsync(string connectionString, string sql, Dictionary<string, object?> args)
        {
            var value = await ScalarStringAsync(connectionString, sql, args);
            return int.TryParse(value, out var result) ? result : 0;
        }

        private static async Task<decimal> ScalarDecimalAsync(string connectionString, string sql, Dictionary<string, object?> args)
        {
            var value = await ScalarStringAsync(connectionString, sql, args);
            return decimal.TryParse(value, out var result) ? result : 0m;
        }

        private static async Task<string> PollDeliveryIdAsync(string connectionString, string campaignId, string doctorId)
        {
            for (var i = 0; i < 24; i++)
            {
                var id = await ScalarStringAsync(connectionString,
                    "SELECT TOP 1 Id FROM DoctorAdDeliveries WHERE CampaignId = @campaignId AND DoctorId = @doctorId ORDER BY CreatedAtUtc DESC",
                    new() { ["@campaignId"] = campaignId, ["@doctorId"] = doctorId });
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            return string.Empty;
        }

        private static object NewDoctorRegistration(string email, string phone, string suffix)
        {
            return new
            {
                Email = email,
                Password = "Password1!",
                PhoneNumber = phone,
                Specialization = "Cardiology",
                ExperienceYears = 10,
                Location = "Cairo",
                VerificationMetadata = Verification("License", $"doctor-{suffix}.pdf", $"doctor-ref-{suffix}")
            };
        }

        private static object Verification(string type, string fileName, string reference)
        {
            return new { DocumentType = type, OriginalFileName = fileName, ContentType = "application/pdf", SizeBytes = 2048, Reference = reference };
        }

        private static string GetEnvelopeDataString(string body, string property)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }
            foreach (var item in data.EnumerateObject())
            {
                if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase))
                {
                    return item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() ?? string.Empty : item.Value.ToString();
                }
            }
            return string.Empty;
        }

        private static void LoadCoverageMatrix(string swagger, LiveE2EEvidence evidence)
        {
            using var doc = JsonDocument.Parse(swagger);
            foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
            {
                foreach (var operation in path.Value.EnumerateObject())
                {
                    var method = operation.Name.ToUpperInvariant();
                    if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE"))
                    {
                        continue;
                    }
                    var tags = operation.Value.TryGetProperty("tags", out var tagElement) && tagElement.ValueKind == JsonValueKind.Array
                        ? tagElement.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => t.Length > 0).ToArray()
                        : Array.Empty<string>();
                    evidence.Endpoints.Add(new EndpointCoverage
                    {
                        Method = method,
                        Path = path.Name,
                        Controller = tags.FirstOrDefault() ?? "Unknown",
                        Purpose = operation.Value.TryGetProperty("summary", out var summary) ? summary.GetString() : null
                    });
                }
            }
        }

        private static void ImportPriorReportScenarios(LiveE2EEvidence evidence, E2ETestReport report)
        {
            foreach (var scenario in report.Scenarios)
            {
                if (string.IsNullOrWhiteSpace(scenario.RequestUrl) || string.IsNullOrWhiteSpace(scenario.RequestMethod))
                {
                    continue;
                }

                var uri = new Uri(scenario.RequestUrl);
                var concretePath = uri.AbsolutePath;
                var template = TemplateFromConcrete(concretePath, evidence);
                var endpoint = evidence.Endpoints.FirstOrDefault(e => e.Path == template && e.Method == scenario.RequestMethod);
                var controller = endpoint?.Controller ?? "ImportedStage2";
                var imported = new EvidenceScenario
                {
                    Controller = controller,
                    Endpoint = template,
                    Method = scenario.RequestMethod,
                    ScenarioName = $"Imported stage2: {scenario.ScenarioName}",
                    RoleUsed = InferRoleFromStage2Scenario(scenario),
                    SanitizedRequest = SanitizeEvidence(scenario.RequestBody ?? string.Empty),
                    HttpStatus = (int)scenario.ActualStatus,
                    SanitizedResponse = SanitizeEvidence(scenario.ResponseBody),
                    Result = scenario.Pass ? "pass" : "fail",
                    ImportantDatabaseEffect = $"Imported from prior real HTTP stage2 report: {scenario.Notes}",
                    RelatedGeneratedIds = new()
                };
                evidence.Scenarios.Add(imported);
                if (endpoint is not null)
                {
                    endpoint.Status = "tested";
                    endpoint.Scenarios.Add(imported.ScenarioName);
                }
            }
        }

        private static string InferRoleFromStage2Scenario(ScenarioResult scenario)
        {
            if (scenario.RequestHeaders.ContainsKey("Authorization"))
            {
                if (scenario.RequestUrl?.Contains("/api/admin/", StringComparison.OrdinalIgnoreCase) == true) return "Admin";
                if (scenario.RequestUrl?.Contains("/api/company/", StringComparison.OrdinalIgnoreCase) == true) return "Company";
                if (scenario.RequestUrl?.Contains("/api/doctor/", StringComparison.OrdinalIgnoreCase) == true) return "Doctor";
                return "Authenticated";
            }
            return "Anonymous";
        }

        private static string RoleForPath(string path)
        {
            if (path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase)) return "Admin";
            if (path.StartsWith("/api/company/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/campaigns/", StringComparison.OrdinalIgnoreCase)) return "Company";
            if (path.StartsWith("/api/doctor/", StringComparison.OrdinalIgnoreCase)) return "Doctor";
            if (path.StartsWith("/api/files/", StringComparison.OrdinalIgnoreCase)) return "Doctor";
            return "Anonymous";
        }

        private static bool IsProtected(string path)
        {
            return path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/company/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/doctor/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/files/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/campaigns/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolvePath(string template, E2EState state, string? deliveryId, string? withdrawalId)
        {
            return template
                .Replace("{id}", state.DoctorUserId, StringComparison.OrdinalIgnoreCase)
                .Replace("{doctorId}", state.DoctorProfileId, StringComparison.OrdinalIgnoreCase)
                .Replace("{campaignId}", state.CampaignId, StringComparison.OrdinalIgnoreCase)
                .Replace("{fileId}", state.FileId, StringComparison.OrdinalIgnoreCase)
                .Replace("{assetId}", state.FileId, StringComparison.OrdinalIgnoreCase)
                .Replace("{deliveryId}", string.IsNullOrWhiteSpace(deliveryId) ? "missing-delivery-id" : deliveryId, StringComparison.OrdinalIgnoreCase)
                .Replace("{withdrawalId}", string.IsNullOrWhiteSpace(withdrawalId) ? "missing-withdrawal-id" : withdrawalId, StringComparison.OrdinalIgnoreCase);
        }

        private static string TemplateFromConcrete(string concrete, LiveE2EEvidence evidence)
        {
            var pathOnly = concrete.Split('?', 2)[0];
            var exact = evidence.Endpoints.FirstOrDefault(e => string.Equals(e.Path, pathOnly, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact.Path;
            }

            foreach (var endpoint in evidence.Endpoints.OrderByDescending(e => e.Path.Length))
            {
                var pattern = "^" + Regex.Escape(endpoint.Path).Replace("\\{", "{").Replace("\\}", "}");
                pattern = Regex.Replace(pattern, "\\{[^/]+\\}", "[^/]+") + "$";
                if (Regex.IsMatch(pathOnly, pattern, RegexOptions.IgnoreCase))
                {
                    return endpoint.Path;
                }
            }
            return pathOnly;
        }

        private static string SanitizeEvidence(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
            var sanitized = SanitizeSensitiveJson(value);
            sanitized = Regex.Replace(sanitized, "(\"(?:Url|url|AccessUrl|accessUrl|StorageKey|storageKey|CloudinaryKey|cloudinaryKey|PublicId|publicId)\"\\s*:\\s*\")[^\"]*(\")", "$1[REDACTED]$2", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, "https://api\\.cloudinary\\.com/[^\\s\\\"]+", "[REDACTED_SIGNED_URL]", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, "https://res\\.cloudinary\\.com/[^\\s\\\"]+", "[REDACTED_SIGNED_URL]", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, "Bearer\\s+[A-Za-z0-9._\\-]+", "Bearer [MASKED]", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, "(Idempotency-Key\"?\\s*:?\\s*\"?)[A-Za-z0-9._\\-]+", "$1[REDACTED]", RegexOptions.IgnoreCase);
            return sanitized;
        }

        private static bool ContainsUnsafeLeak(string sanitizedResponse)
        {
            if (string.IsNullOrWhiteSpace(sanitizedResponse))
            {
                return false;
            }
            return sanitizedResponse.Contains("StackTrace", StringComparison.OrdinalIgnoreCase)
                || sanitizedResponse.Contains("SqlException", StringComparison.OrdinalIgnoreCase)
                || sanitizedResponse.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase)
                || sanitizedResponse.Contains("Password=", StringComparison.OrdinalIgnoreCase)
                || sanitizedResponse.Contains("SigningKey", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<(E2ETestReport Report, string SourcePath)> LoadLatestReportAsync(string root)
        {
            var path = Directory.GetFiles(root, "e2e-test-data-*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? throw new FileNotFoundException("No e2e-test-data-*.json report found. Run stage1/stage2 first.");
            var report = JsonSerializer.Deserialize<E2ETestReport>(await File.ReadAllTextAsync(path))
                ?? throw new InvalidOperationException("Failed to deserialize prior E2E report.");
            return (report, path);
        }

        private static string FindRepositoryRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MediBridge.APIs", "MediBridge.APIs.csproj")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public sealed record RecordedHttp(HttpStatusCode StatusCode, string Body);
    public sealed record LoginResult(HttpResponseMessage Response, string AccessToken, string RefreshToken);
    public sealed record DatabaseEffect(string Name, string Effect);
    public sealed record SettlementCampaignIds(string CampaignId, string AssetId);

    public sealed class LiveE2EEvidence
    {
        public DateTime TimestampUtc { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string? SourceReportPath { get; set; }
        public string? OpenApiPath { get; set; }
        public int TotalEndpoints { get; set; }
        public int TestedEndpoints { get; set; }
        public int BlockedEndpoints { get; set; }
        public decimal CoveragePercent { get; set; }
        public int TotalScenarios { get; set; }
        public int PassedScenarios { get; set; }
        public int FailedScenarios { get; set; }
        public int BlockedScenarios { get; set; }
        public Dictionary<string, string?> TestContext { get; set; } = new();
        public List<EndpointCoverage> Endpoints { get; set; } = new();
        public List<EvidenceScenario> Scenarios { get; set; } = new();
        public List<DatabaseEffect> DatabaseEffects { get; set; } = new();
    }

    public sealed class EndpointCoverage
    {
        public string Method { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Controller { get; set; } = string.Empty;
        public string? Purpose { get; set; }
        public string Status { get; set; } = "untested";
        public string? BlockedReason { get; set; }
        public List<string> Scenarios { get; set; } = new();
    }

    public sealed class EvidenceScenario
    {
        public string Controller { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string ScenarioName { get; set; } = string.Empty;
        public string RoleUsed { get; set; } = string.Empty;
        public string SanitizedRequest { get; set; } = string.Empty;
        public int? HttpStatus { get; set; }
        public string SanitizedResponse { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string ImportantDatabaseEffect { get; set; } = string.Empty;
        public Dictionary<string, string?> RelatedGeneratedIds { get; set; } = new();
    }
}
