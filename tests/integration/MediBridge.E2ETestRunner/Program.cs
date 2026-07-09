using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace MediBridge.E2ETestRunner
{
    public partial class Program
    {
        private static readonly string BaseUrl = "http://localhost:5246";
        private static readonly string StateFilePath = "e2e-state.json";

        public static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  dotnet run --project tests/integration/MediBridge.E2ETestRunner -- stage1");
                Console.WriteLine("  dotnet run --project tests/integration/MediBridge.E2ETestRunner -- stage2 <doctor_otp> <company_otp>");
                return;
            }

            string mode = args[0].ToLowerInvariant();
            if (mode == "stage1")
            {
                await RunStage1Async();
            }
            else if (mode == "stage2")
            {
                if (args.Length < 3)
                {
                    Console.WriteLine("Error: Please provide both the doctor OTP and the company OTP.");
                    Console.WriteLine("Usage: dotnet run --project tests/integration/MediBridge.E2ETestRunner -- stage2 <doctor_otp> <company_otp>");
                    return;
                }
                string doctorOtp = args[1];
                string companyOtp = args[2];
                await RunStage2Async(doctorOtp, companyOtp);
            }
            else
            {
                Console.WriteLine($"Unknown mode: {mode}");
            }
        }

        private static async Task RunStage1Async()
        {
            Console.WriteLine("=== STARTING STAGE 1: REGISTRATION & INITIAL VERIFICATION ===");
            var state = new E2EState();

            // Connect to database to verify connection and read configured settings
            string connectionString = GetConnectionString();
            Console.WriteLine($"Database target resolved. Server: {MaskConnectionString(connectionString)}");

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                Console.WriteLine("Successfully connected to the real database.");
            }

            var client = new HttpClient();
            client.BaseAddress = new Uri(BaseUrl);

            // Generate unique details
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            state.DoctorEmail = $"doctor-{suffix}@medibridge.local";
            state.DoctorPassword = "Password1!";
            state.DoctorPhone = $"55512{RandomNumberGenerator.GetInt32(10000, 99999)}";
            state.DoctorLocation = "Cairo";
            state.DoctorSpecialization = "Cardiology";
            state.DoctorDocRef = $"lic-doc-{suffix}";

            state.CompanyEmail = $"company-{suffix}@medibridge.local";
            state.CompanyPassword = "Password1!";
            state.CompanyPhone = $"55513{RandomNumberGenerator.GetInt32(10000, 99999)}";
            state.CompanyName = "E2E Pharma Egypt Ltd";
            state.CompanyLicense = $"lic-comp-{suffix}";
            state.CompanyContact = "E2E EGP Contact";
            state.CompanyDocRef = $"lic-comp-ref-{suffix}";

            // Scenario 1: Register Doctor (Happy Path)
            Console.WriteLine($"Registering Doctor: {state.DoctorEmail}");
            var doctorRegPayload = new
            {
                Email = state.DoctorEmail,
                Password = state.DoctorPassword,
                PhoneNumber = state.DoctorPhone,
                Specialization = state.DoctorSpecialization,
                ExperienceYears = 10,
                Location = state.DoctorLocation,
                VerificationMetadata = new
                {
                    DocumentType = "License",
                    OriginalFileName = "doctor-license.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = 2048,
                    Reference = state.DoctorDocRef
                }
            };
            
            var doctorRegResponse = await client.PostAsJsonAsync("/api/auth/register-doctor", doctorRegPayload);
            Console.WriteLine($"Doctor Registration Status: {doctorRegResponse.StatusCode}");
            if (doctorRegResponse.StatusCode != HttpStatusCode.Created)
            {
                string body = await doctorRegResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"Doctor registration failed: {body}");
                return;
            }
            var docRegResult = await doctorRegResponse.Content.ReadFromJsonAsync<JsonElement>();
            state.DoctorUserId = GetDataString(docRegResult, "UserId");

            // Scenario 2: Register Company (Happy Path)
            Console.WriteLine($"Registering Company: {state.CompanyEmail}");
            var companyRegPayload = new
            {
                Email = state.CompanyEmail,
                Password = state.CompanyPassword,
                PhoneNumber = state.CompanyPhone,
                CompanyName = state.CompanyName,
                LicenseNumber = state.CompanyLicense,
                ContactName = state.CompanyContact,
                VerificationMetadata = new
                {
                    DocumentType = "TradeLicense",
                    OriginalFileName = "company-license.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = 4096,
                    Reference = state.CompanyDocRef
                }
            };
            
            var companyRegResponse = await client.PostAsJsonAsync("/api/auth/register-company", companyRegPayload);
            Console.WriteLine($"Company Registration Status: {companyRegResponse.StatusCode}");
            if (companyRegResponse.StatusCode != HttpStatusCode.Created)
            {
                string body = await companyRegResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"Company registration failed: {body}");
                return;
            }
            var compRegResult = await companyRegResponse.Content.ReadFromJsonAsync<JsonElement>();
            state.CompanyUserId = GetDataString(compRegResult, "UserId");

            // Scenario 3: Verify login fails before OTP verification (Forbidden / 403)
            Console.WriteLine("Verifying login fails before OTP verification...");
            var doctorLoginPayload = new { Username = state.DoctorEmail, Password = state.DoctorPassword };
            var loginBeforeOtpResponse = await client.PostAsJsonAsync("/api/auth/login", doctorLoginPayload);
            Console.WriteLine($"Login before OTP Status (Expected: Forbidden): {loginBeforeOtpResponse.StatusCode}");

            // Verify Database record existence and query details
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("SELECT AccountStatus, EmailVerified FROM Users WHERE Id = @UserId", conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", state.DoctorUserId);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            string status = reader.GetString(0);
                            bool verified = reader.GetBoolean(1);
                            Console.WriteLine($"DB Status check - Doctor: status={status}, emailVerified={verified}");
                        }
                    }
                }
            }

            // Save State
            string stateJson = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(StateFilePath, stateJson);
            Console.WriteLine("E2E State saved successfully to e2e-state.json.");
            Console.WriteLine("\n=============================================");
            Console.WriteLine("ACTION REQUIRED: OTPs have been sent via SMTP.");
            Console.WriteLine("Emails resolved to: medibridge7@gmail.com (overridden)");
            Console.WriteLine($"Please retrieve the verification OTPs for:");
            Console.WriteLine($"1. Doctor: {state.DoctorEmail} (UserId: {state.DoctorUserId})");
            Console.WriteLine($"2. Company: {state.CompanyEmail} (UserId: {state.CompanyUserId})");
            Console.WriteLine("Provide them in the next command like so:");
            Console.WriteLine("  dotnet run --project tests/integration/MediBridge.E2ETestRunner -- stage2 <doctor_otp> <company_otp>");
            Console.WriteLine("=============================================");
        }

        /// <summary>Reads a response body once, records the scenario, and returns the parsed JSON element.</summary>
        private static async Task<JsonElement?> RecordAndParseAsync(E2ETestReport report, string name, string purpose,
            object? requestPayload, HttpResponseMessage response, HttpStatusCode expectedStatus, string notes)
        {
            string body = await response.Content.ReadAsStringAsync();
            AddScenarioResultWithBody(report, name, purpose, requestPayload, response, body, expectedStatus, notes);
            if (string.IsNullOrWhiteSpace(body)) return null;
            try { return JsonDocument.Parse(body).RootElement; } catch { return null; }
        }

        private static async Task RunStage2Async(string doctorOtp, string companyOtp)
        {
            Console.WriteLine("=== STARTING STAGE 2: OTP VERIFICATION & COMPLETE E2E WORKFLOW ===");
            
            if (!File.Exists(StateFilePath))
            {
                Console.WriteLine("Error: e2e-state.json file not found. Run stage1 first.");
                return;
            }

            string stateJson = await File.ReadAllTextAsync(StateFilePath);
            var state = JsonSerializer.Deserialize<E2EState>(stateJson);
            if (state == null)
            {
                Console.WriteLine("Error: Failed to deserialize E2E state.");
                return;
            }

            string connectionString = GetConnectionString();
            var client = new HttpClient();
            client.BaseAddress = new Uri(BaseUrl);

            var report = new E2ETestReport
            {
                Timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"),
                DatabaseServer = MaskConnectionString(connectionString),
                E2EState = state
            };

            // Scenario 4: Try Wrong OTP (Unhappy Path)
            Console.WriteLine("Testing incorrect OTP validation...");
            var wrongOtpPayload = new { Email = state.DoctorEmail, Channel = "Email", Otp = "000000" };
            var wrongOtpResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", wrongOtpPayload);
            await RecordAndParseAsync(report, "Wrong OTP Verification", "Verify incorrect OTP is rejected",
                wrongOtpPayload, wrongOtpResponse, HttpStatusCode.BadRequest, "Rejected as expected.");

            // Scenario 5: Verify Doctor OTP (Happy Path)
            Console.WriteLine("Verifying Doctor OTP.");
            var docOtpPayload = new { Email = state.DoctorEmail, Channel = "Email", Otp = doctorOtp };
            var docOtpResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", docOtpPayload);
            await RecordAndParseAsync(report, "Doctor OTP Verification", "Verify doctor OTP successfully",
                docOtpPayload, docOtpResponse, HttpStatusCode.OK, "Verified successfully.");

            // Scenario 6: Verify Company OTP (Happy Path)
            Console.WriteLine("Verifying Company OTP.");
            var compOtpPayload = new { Email = state.CompanyEmail, Channel = "Email", Otp = companyOtp };
            var compOtpResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", compOtpPayload);
            await RecordAndParseAsync(report, "Company OTP Verification", "Verify company OTP successfully",
                compOtpPayload, compOtpResponse, HttpStatusCode.OK, "Verified successfully.");

            // Scenario 7: Admin Login — read body ONCE, extract token from it
            Console.WriteLine("Admin logging in...");
            var adminLoginPayload = new
            {
                Username = Environment.GetEnvironmentVariable("MEDIBRIDGE_E2E_ADMIN_USERNAME") ?? "admin@medibridge.local",
                Password = GetRequiredEnvironmentVariable("MEDIBRIDGE_E2E_ADMIN_PASSWORD")
            };
            var adminLoginResponse = await client.PostAsJsonAsync("/api/auth/login", adminLoginPayload);
            var adminLoginJson = await RecordAndParseAsync(report, "Admin Login", "Login as admin user",
                adminLoginPayload, adminLoginResponse, HttpStatusCode.OK, "Logged in.");
            string adminToken = GetDataString(adminLoginJson, "AccessToken");
            if (string.IsNullOrEmpty(adminToken))
            {
                Console.WriteLine("FATAL: Could not extract admin token. Aborting.");
                await SaveReportAsync(report);
                return;
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            Console.WriteLine("Admin token acquired.");

            // Scenario 8: Admin Approves Doctor Account
            Console.WriteLine($"Admin approving Doctor User ID: {state.DoctorUserId}");
            var approvePayload = new { Decision = "Approve", Notes = "E2E approved account" };
            var docApproveResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{state.DoctorUserId}/decision", approvePayload);
            await RecordAndParseAsync(report, "Admin Approve Doctor", "Approve doctor account status",
                approvePayload, docApproveResponse, HttpStatusCode.OK, "Approved.");

            // Scenario 9: Admin Approves Company Account
            Console.WriteLine($"Admin approving Company User ID: {state.CompanyUserId}");
            var compApproveResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{state.CompanyUserId}/decision", approvePayload);
            await RecordAndParseAsync(report, "Admin Approve Company", "Approve company account status",
                approvePayload, compApproveResponse, HttpStatusCode.OK, "Approved.");

            // Get Doctor Profile ID to set pricing
            string doctorProfileId = "";
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("SELECT Id FROM DoctorProfiles WHERE UserId = @UserId", conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", state.DoctorUserId);
                    doctorProfileId = (string?)await cmd.ExecuteScalarAsync() ?? "";
                    state.DoctorProfileId = doctorProfileId;
                    Console.WriteLine($"Doctor Profile ID: {doctorProfileId}");
                }
            }

            // Scenario 10: Admin Sets Doctor Price
            Console.WriteLine($"Admin setting Doctor pricing for profile: {doctorProfileId}");
            var pricingPayload = new { PricePerMessage = 75.00m, Reason = "E2E pricing setup" };
            var pricingResponse = await client.PutAsJsonAsync($"/api/admin/doctors/{doctorProfileId}/price", pricingPayload);
            await RecordAndParseAsync(report, "Set Doctor Pricing", "Set custom per-message price for doctor",
                pricingPayload, pricingResponse, HttpStatusCode.OK, "Price set to 75.00 EGP.");

            // Scenario 11: Company Login — read body ONCE, extract token
            Console.WriteLine("Company logging in...");
            var companyLoginPayload = new { Username = state.CompanyEmail, Password = state.CompanyPassword };
            var companyLoginResponse = await client.PostAsJsonAsync("/api/auth/login", companyLoginPayload);
            var companyLoginJson = await RecordAndParseAsync(report, "Company Login", "Login as company user",
                companyLoginPayload, companyLoginResponse, HttpStatusCode.OK, "Logged in.");
            string companyToken = GetDataString(companyLoginJson, "AccessToken");
            if (string.IsNullOrEmpty(companyToken))
            {
                Console.WriteLine("FATAL: Could not extract company token. Aborting.");
                await SaveReportAsync(report);
                return;
            }
            Console.WriteLine("Company token acquired.");

            // Scenario 12: Doctor Login — read body ONCE, extract token
            Console.WriteLine("Doctor logging in...");
            var docLoginPayload = new { Username = state.DoctorEmail, Password = state.DoctorPassword };
            var docLoginResponse = await client.PostAsJsonAsync("/api/auth/login", docLoginPayload);
            var docLoginJson = await RecordAndParseAsync(report, "Doctor Login", "Login as doctor user",
                docLoginPayload, docLoginResponse, HttpStatusCode.OK, "Logged in.");
            string doctorToken = GetDataString(docLoginJson, "AccessToken");
            if (string.IsNullOrEmpty(doctorToken))
            {
                Console.WriteLine("FATAL: Could not extract doctor token. Aborting.");
                await SaveReportAsync(report);
                return;
            }
            Console.WriteLine("Doctor token acquired.");

            // Switch HttpClient auth to Company for wallet operations
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", companyToken);

            // Get Company Profile ID from DB
            string companyProfileId = "";
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("SELECT Id FROM CompanyProfiles WHERE UserId = @UserId", conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", state.CompanyUserId);
                    companyProfileId = (string?)await cmd.ExecuteScalarAsync() ?? "";
                    state.CompanyProfileId = companyProfileId;
                    Console.WriteLine($"Company Profile ID: {companyProfileId}");
                }
            }

            // Scenario 13: Company Wallet Query
            Console.WriteLine("Querying Company wallet...");
            var walletQueryResponse = await client.GetAsync("/api/company/wallet");
            await RecordAndParseAsync(report, "Query Wallet", "Retrieve company wallet balance",
                null, walletQueryResponse, HttpStatusCode.OK, "Wallet details retrieved.");

            // Get Wallet ID from DB
            string walletId = "";
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("SELECT Id FROM Wallets WHERE OwnerId = @OwnerId", conn))
                {
                    cmd.Parameters.AddWithValue("@OwnerId", companyProfileId);
                    walletId = (string?)await cmd.ExecuteScalarAsync() ?? "";
                    state.WalletId = walletId;
                    Console.WriteLine($"Wallet ID: {walletId}");
                }
            }

            // Scenario 14: Company Wallet top-up (mock-checkout)
            Console.WriteLine("Performing mock checkout wallet top-up...");
            string checkoutKey = $"checkout-{Guid.NewGuid():N}";
            var checkoutPayload = new { Amount = 1500.00m, Currency = "EGP" };
            var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
            {
                Content = JsonContent.Create(checkoutPayload)
            };
            checkoutReq.Headers.Add("Idempotency-Key", checkoutKey);
            var checkoutResponse = await client.SendAsync(checkoutReq);
            await RecordAndParseAsync(report, "Wallet Mock Checkout", "Top-up wallet balance",
                checkoutPayload, checkoutResponse, HttpStatusCode.OK, "Wallet credited with 1500.00 EGP.");

            // Scenario 15: Replay top-up with same idempotency key
            Console.WriteLine("Replaying wallet top-up with same idempotency key...");
            var replayReq = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
            {
                Content = JsonContent.Create(checkoutPayload)
            };
            replayReq.Headers.Add("Idempotency-Key", checkoutKey);
            var replayResponse = await client.SendAsync(replayReq);
            await RecordAndParseAsync(report, "Wallet Mock Checkout Replay", "Verify idempotency prevents double top-up",
                checkoutPayload, replayResponse, HttpStatusCode.OK, "Idempotent response returned.");

            // Scenario 16: Conflict top-up (same key, different amount)
            Console.WriteLine("Testing wallet top-up conflict with same key but different amount...");
            var conflictPayload = new { Amount = 2000.00m, Currency = "EGP" };
            var conflictReq = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
            {
                Content = JsonContent.Create(conflictPayload)
            };
            conflictReq.Headers.Add("Idempotency-Key", checkoutKey);
            var conflictResponse = await client.SendAsync(conflictReq);
            await RecordAndParseAsync(report, "Wallet Mock Checkout Conflict", "Verify key reuse with different payload returns conflict",
                conflictPayload, conflictResponse, HttpStatusCode.Conflict, "Conflict status returned as expected.");

            // Scenario 17: Create Campaign Draft — read body ONCE, extract campaignId
            Console.WriteLine("Creating Campaign Draft...");
            var campaignDraftPayload = new
            {
                Title = "Anti-infection Campaign Egypt E2E",
                Description = "A trial campaign focusing on modern cardiology delivery and medical details.",
                ClinicalResearchInfo = "Details regarding Cardiology medicine efficacy and trials."
            };
            var draftReq = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns/drafts")
            {
                Content = JsonContent.Create(campaignDraftPayload)
            };
            var draftResponse = await client.SendAsync(draftReq);
            var draftJson = await RecordAndParseAsync(report, "Create Campaign Draft", "Create new campaign draft in system",
                campaignDraftPayload, draftResponse, HttpStatusCode.Created, "Draft created.");
            string campaignId = GetDataString(draftJson, "CampaignId");
            state.CampaignId = campaignId;
            Console.WriteLine($"Campaign ID: {campaignId}");
            if (string.IsNullOrEmpty(campaignId)) { Console.WriteLine("FATAL: No campaign ID. Aborting."); await SaveReportAsync(report); return; }

            // Scenario 18: Upload Campaign Asset — read body ONCE, extract assetId
            Console.WriteLine($"Uploading private asset to Cloudinary for Campaign ID: {campaignId}");
            byte[] fileBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lV9h6QAAAABJRU5ErkJggg==");
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            var multipartContent = new MultipartFormDataContent();
            multipartContent.Add(fileContent, "file", "campaign-media.png");
            var uploadResponse = await client.PostAsync($"/api/company/campaigns/{campaignId}/assets", multipartContent);
            var uploadJson = await RecordAndParseAsync(report, "Upload Campaign Asset", "Upload privately signed media to Cloudinary",
                "[Multipart Media Content]", uploadResponse, HttpStatusCode.Created, "Uploaded and registered successfully.");
            string assetId = GetDataString(uploadJson, "Id");
            string storageKey = GetDataString(uploadJson, "StorageKey");
            state.FileId = assetId;
            state.CloudinaryKey = storageKey;
            Console.WriteLine($"Asset ID: {assetId}");

            // Switch to Admin to review asset
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            // Scenario 19: Admin Reviews and Approves Campaign Asset
            Console.WriteLine($"Admin approving campaign asset ID: {assetId}");
            var assetReviewPayload = new { Decision = "Approved", Comments = "Approved by Admin E2E" };
            var fileReviewResponse = await client.PostAsJsonAsync($"/api/admin/campaign-assets/{assetId}/review", assetReviewPayload);
            await RecordAndParseAsync(report, "Admin Review Asset", "Approve campaign asset",
                assetReviewPayload, fileReviewResponse, HttpStatusCode.OK, "Asset approved.");

            // Switch back to Company
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", companyToken);

            // Scenario 20: Company Views Target Preview
            Console.WriteLine("Company viewing campaign target preview...");
            var targetPreviewResponse = await client.GetAsync($"/api/company/campaigns/{campaignId}/target-preview");
            await RecordAndParseAsync(report, "Campaign Target Preview", "View targeted doctor profile summary",
                null, targetPreviewResponse, HttpStatusCode.OK, "Target preview retrieved.");

            // Scenario 21: Company Submits Campaign
            Console.WriteLine($"Company submitting Campaign: {campaignId}");
            var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/company/campaigns/{campaignId}/submit");
            submitReq.Headers.Add("Idempotency-Key", $"submit-{Guid.NewGuid():N}");
            var submitResponse = await client.SendAsync(submitReq);
            await RecordAndParseAsync(report, "Submit Campaign", "Submit campaign for final admin approval",
                null, submitResponse, HttpStatusCode.OK, "Campaign submitted.");

            // Switch back to Admin for campaign review
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            // Scenario 22: Admin Reviews and Approves Campaign (triggers FIFO queue generation)
            Console.WriteLine($"Admin reviewing/approving Campaign: {campaignId}");
            string campaignReviewKey = $"review-{Guid.NewGuid():N}";
            var campaignReviewPayload = new { Decision = "Approved", Notes = "FIFO delivery approved" };
            var campaignReviewReq = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
            {
                Content = JsonContent.Create(campaignReviewPayload)
            };
            campaignReviewReq.Headers.Add("Idempotency-Key", campaignReviewKey);
            var campaignReviewResponse = await client.SendAsync(campaignReviewReq);
            await RecordAndParseAsync(report, "Admin Approve Campaign", "Review and approve campaign. Triggers queue generation.",
                campaignReviewPayload, campaignReviewResponse, HttpStatusCode.OK, "Campaign approved and queue generated.");

            // Switch to Doctor
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", doctorToken);

            // Wait briefly for Hangfire to process and create queue records
            await Task.Delay(2000);

            // Get Doctor Message Queue item from Database
            string deliveryId = "";
            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("SELECT TOP 1 Id FROM DoctorAdDeliveries WHERE CampaignId = @CampaignId AND DoctorId = @DoctorId ORDER BY CreatedAtUtc DESC", conn))
                {
                    cmd.Parameters.AddWithValue("@CampaignId", campaignId);
                    cmd.Parameters.AddWithValue("@DoctorId", doctorProfileId);
                    deliveryId = (string?)await cmd.ExecuteScalarAsync() ?? "";
                    state.QueueId = deliveryId;
                    Console.WriteLine($"Delivery ID: {deliveryId}");
                }
            }

            // Scenario 23: Doctor reads today's inbox message (Happy Path)
            Console.WriteLine("Doctor checking today's message inbox...");
            var docInboxResponse = await client.GetAsync("/api/doctor/messages/today");
            await RecordAndParseAsync(report, "Doctor Inbox Messages", "Verify Doctor can retrieve active campaign messages",
                null, docInboxResponse, HttpStatusCode.OK, "Inbox messages retrieved.");

            // Scenario 24: Doctor Accesses campaign asset via private signed URL
            if (!string.IsNullOrWhiteSpace(deliveryId) && !string.IsNullOrWhiteSpace(assetId))
            {
                Console.WriteLine($"Doctor accessing delivery message asset: deliveryId={deliveryId}, assetId={assetId}");
                var assetAccessResponse = await client.GetAsync($"/api/doctor/messages/{deliveryId}/assets/{assetId}/access");
                await RecordAndParseAsync(report, "Doctor Asset Access", "Retrieve Cloudinary signed access URL for private media",
                    null, assetAccessResponse, HttpStatusCode.OK, "Signed URL generated.");
            }
            else
            {
                Console.WriteLine("[WARN] Skipping Doctor Asset Access — deliveryId or assetId was empty.");
            }

            // Scenario 25: Unauthorized Access test (No token)
            Console.WriteLine("Testing unauthorized access to protected endpoint...");
            using var unauthClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            var unauthResponse = await unauthClient.GetAsync("/api/company/wallet");
            await RecordAndParseAsync(report, "Unauthorized Access", "Anonymous access to protected endpoint",
                null, unauthResponse, HttpStatusCode.Unauthorized, "Unauthorized status code returned as expected.");

            // Scenario 26: Forbidden Access test (Doctor accesses company wallet)
            Console.WriteLine("Testing forbidden access to company wallet using Doctor token...");
            var forbiddenResponse = await client.GetAsync("/api/company/wallet"); // client has Doctor token
            await RecordAndParseAsync(report, "Forbidden Access", "Access company wallet using Doctor role",
                null, forbiddenResponse, HttpStatusCode.Forbidden, "Forbidden status code returned as expected.");

            // Write final report
            await SaveReportAsync(report);
        }

        private static async Task SaveReportAsync(E2ETestReport report)
        {
            string finalReportPath = $"e2e-test-data-{report.Timestamp}.json";
            string reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(finalReportPath, reportJson);
            int passed = report.Scenarios.Count(s => s.Pass);
            int total = report.Scenarios.Count;
            Console.WriteLine("\n=============================================");
            Console.WriteLine("E2E WORKFLOW RUN COMPLETED.");
            Console.WriteLine($"Results: {passed}/{total} scenarios PASSED");
            foreach (var s in report.Scenarios)
                Console.WriteLine($"  [{(s.Pass ? "PASS" : "FAIL")}] {s.ScenarioName} — Expected: {s.ExpectedStatus}, Actual: {s.ActualStatus}");
            Console.WriteLine($"E2E Test Data File written: {Path.GetFullPath(finalReportPath)}");
            Console.WriteLine("=============================================");
        }

        /// <summary>Records a scenario result using a pre-read response body string (avoids double-read stream issues).</summary>
        private static void AddScenarioResultWithBody(E2ETestReport report, string name, string purpose,
            object? requestPayload, HttpResponseMessage response, string responseBody,
            HttpStatusCode expectedStatus, string notes)
        {
            var result = new ScenarioResult
            {
                ScenarioName = name,
                Purpose = purpose,
                ExpectedStatus = expectedStatus,
                ActualStatus = response.StatusCode,
                Pass = response.StatusCode == expectedStatus,
                Notes = notes,
                RequestUrl = response.RequestMessage?.RequestUri?.ToString(),
                RequestMethod = response.RequestMessage?.Method.ToString(),
                ResponseBody = SanitizeSensitiveJson(responseBody),
                RequestBody = requestPayload != null ? SanitizeSensitiveJson(JsonSerializer.Serialize(requestPayload)) : null
            };

            if (response.RequestMessage?.Headers.Authorization != null)
                result.RequestHeaders["Authorization"] = "Bearer [MASKED]";

            if (response.RequestMessage != null)
            {
                foreach (var header in response.RequestMessage.Headers)
                {
                    if (header.Key != "Authorization")
                        result.RequestHeaders[header.Key] = string.Join(", ", header.Value);
                }
            }

            report.Scenarios.Add(result);
            Console.WriteLine($"Scenario: [{name}] - Status: {(result.Pass ? "PASS" : "FAIL")} (Expected: {expectedStatus}, Actual: {response.StatusCode})");
        }

        private static string SanitizeSensitiveJson(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return SensitiveJsonPropertyRegex().Replace(value, match => $"{match.Groups[1].Value}[REDACTED]\"");
        }

        [GeneratedRegex("(\"(?:password|newPassword|otp|verificationToken|resetToken|accessToken|refreshToken)\"\\s*:\\s*\")[^\"]*(\")", RegexOptions.IgnoreCase)]
        private static partial Regex SensitiveJsonPropertyRegex();

        private static string GetDataString(JsonElement? envelope, string propertyName)
        {
            if (envelope is not { } root
                || !root.TryGetProperty("Data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty(propertyName, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return string.Empty;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        private static string GetConnectionString()
        {
            // Try all likely relative paths from AppContext.BaseDirectory (bin/Debug/net8.0)
            // going upward through: net8.0 -> Debug -> bin -> E2ETestRunner -> integration -> tests -> solution root
            string[] candidates = new[]
            {
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../MediBridge.APIs/appsettings.json")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../MediBridge.APIs/appsettings.json")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../MediBridge.APIs/appsettings.json")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../MediBridge.APIs/appsettings.json")),
                Path.GetFullPath("MediBridge.APIs/appsettings.json"),
                Path.GetFullPath("appsettings.json"),
            };

            string? appSettingsPath = null;
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    appSettingsPath = candidate;
                    Console.WriteLine($"Resolved appsettings.json at: {appSettingsPath}");
                    break;
                }
            }

            if (appSettingsPath == null)
            {
                throw new FileNotFoundException(
                    $"Could not find appsettings.json. Searched:\n" +
                    string.Join("\n", candidates));
            }
            
            string json = File.ReadAllText(appSettingsPath);
            using (var doc = JsonDocument.Parse(json))
            {
                return doc.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()
                    ?? throw new InvalidOperationException("DefaultConnection is missing from appsettings.json.");
            }
        }

        private static string MaskConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            builder.Password = "********";
            return builder.ConnectionString;
        }

        private static string GetRequiredEnvironmentVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{name} must be set before running the E2E stage.");
            }

            return value;
        }
    }

    public class E2EState
    {
        public string DoctorEmail { get; set; } = string.Empty;
        public string DoctorPassword { get; set; } = string.Empty;
        public string DoctorPhone { get; set; } = string.Empty;
        public string DoctorLocation { get; set; } = string.Empty;
        public string DoctorSpecialization { get; set; } = string.Empty;
        public string DoctorDocRef { get; set; } = string.Empty;
        public string DoctorUserId { get; set; } = string.Empty;
        public string DoctorProfileId { get; set; } = string.Empty;

        public string CompanyEmail { get; set; } = string.Empty;
        public string CompanyPassword { get; set; } = string.Empty;
        public string CompanyPhone { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyLicense { get; set; } = string.Empty;
        public string CompanyContact { get; set; } = string.Empty;
        public string CompanyDocRef { get; set; } = string.Empty;
        public string CompanyUserId { get; set; } = string.Empty;
        public string CompanyProfileId { get; set; } = string.Empty;

        public string WalletId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string FileId { get; set; } = string.Empty;
        public string CloudinaryKey { get; set; } = string.Empty;
        public string QueueId { get; set; } = string.Empty;
    }

    public class E2ETestReport
    {
        public string Timestamp { get; set; } = string.Empty;
        public string DatabaseServer { get; set; } = string.Empty;
        public E2EState E2EState { get; set; } = new E2EState();
        public List<ScenarioResult> Scenarios { get; set; } = new List<ScenarioResult>();
    }

    public class ScenarioResult
    {
        public string ScenarioName { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public HttpStatusCode ExpectedStatus { get; set; }
        public HttpStatusCode ActualStatus { get; set; }
        public bool Pass { get; set; }
        public string? RequestUrl { get; set; }
        public string? RequestMethod { get; set; }
        public Dictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>();
        public string? RequestBody { get; set; }
        public string ResponseBody { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}
