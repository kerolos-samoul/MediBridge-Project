param(
    [string]$BaseUrl = "http://medibridge-project.runasp.net",
    [string]$AdminUsername = "admin@medibridge.local",
    [string]$AdminPassword = $env:MEDIBRIDGE_E2E_ADMIN_PASSWORD,
    [string]$DoctorOtp = "",
    [string]$CompanyOtp = "",
    [string]$StatePath = ""
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$tracePath = Join-Path $root "e2e-http-trace-$stamp.json"
$dataPath = Join-Path $root "e2e-test-data-$stamp.json"
$assetDir = Join-Path $root "e2e-assets-$stamp"
New-Item -ItemType Directory -Path $assetDir -Force | Out-Null

$trace = New-Object System.Collections.Generic.List[object]
$data = [ordered]@{
    timestamp = $stamp
    baseUrl = $BaseUrl
    otpRetrievalSource = $null
    doctor = [ordered]@{}
    company = [ordered]@{}
    admin = [ordered]@{}
    files = @()
    campaigns = @()
    wallets = @()
    idempotencyKeys = @()
    database = [ordered]@{}
    blockers = @()
}

function Mask-Text([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $value }
    $masked = $value
    $masked = [regex]::Replace($masked, '("?(?:Password|password|RefreshToken|refreshToken|Otp|otp|VerificationToken|verificationToken)"?\s*:\s*")([^"]*)(")', '$1[MASKED]$3')
    $masked = [regex]::Replace($masked, '("?(?:AccessToken|accessToken)"?\s*:\s*")([^"]{8})[^"]*([^"]{6})(")', '$1$2...$3$4')
    $masked = [regex]::Replace($masked, 'Bearer\s+[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+', 'Bearer [JWT-MASKED]')
    return $masked
}

function Save-Artifacts {
    $trace | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $tracePath -Encoding UTF8
    $data | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $dataPath -Encoding UTF8
}

function Add-Trace($scenario, $method, $url, $headers, $role, $requestBody, $status, $responseBody, $durationMs, $pass, $databaseEffect) {
    $safeHeaders = [ordered]@{}
    if ($headers) {
        foreach ($key in $headers.Keys) {
            if ($key -match 'Authorization') { $safeHeaders[$key] = 'Bearer [JWT-MASKED]' }
            elseif ($key -match 'Cookie') { $safeHeaders[$key] = '[MASKED]' }
            else { $safeHeaders[$key] = $headers[$key] }
        }
    }

    $trace.Add([ordered]@{
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
        scenario = $scenario
        method = $method
        url = $url
        safeHeaders = $safeHeaders
        role = $role
        requestBody = if ($requestBody -is [string]) { Mask-Text $requestBody } elseif ($null -ne $requestBody) { Mask-Text ($requestBody | ConvertTo-Json -Depth 10 -Compress) } else { $null }
        httpStatus = $status
        responseBody = Mask-Text ([string]$responseBody)
        durationMs = $durationMs
        pass = [bool]$pass
        databaseEffect = $databaseEffect
        correlationId = if ($headers -and $headers.ContainsKey("X-Correlation-ID")) { $headers["X-Correlation-ID"] } else { $null }
    }) | Out-Null
}

function Invoke-Api {
    param(
        [string]$Scenario,
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [hashtable]$Headers = @{},
        [string]$Role = "Anonymous",
        [int[]]$Expected = @(),
        [string]$ContentType = "application/json",
        [hashtable]$Form = $null,
        [string]$DatabaseEffect = ""
    )

    $url = if ($Path -match '^https?://') { $Path } else { "$BaseUrl$Path" }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $status = 0
    $content = ""
    try {
        if ($null -ne $Form) {
            $response = Invoke-WebRequest -Uri $url -Method $Method -Headers $Headers -Form $Form -SkipHttpErrorCheck -TimeoutSec 60
        } elseif ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 10 -Compress }
            $response = Invoke-WebRequest -Uri $url -Method $Method -Headers $Headers -ContentType $ContentType -Body $json -SkipHttpErrorCheck -TimeoutSec 60
        } else {
            $response = Invoke-WebRequest -Uri $url -Method $Method -Headers $Headers -SkipHttpErrorCheck -TimeoutSec 60
        }
        $status = [int]$response.StatusCode
        $content = [string]$response.Content
    } catch {
        $content = $_.Exception.Message
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $status = [int]$_.Exception.Response.StatusCode
        }
    } finally {
        $sw.Stop()
    }

    $pass = $Expected.Count -eq 0 -or $Expected -contains $status
    Add-Trace $Scenario $Method $url $Headers $Role $Body $status $content $sw.ElapsedMilliseconds $pass $DatabaseEffect
    [pscustomobject]@{ Status = $status; Body = $content; Pass = $pass; Json = $(try { $content | ConvertFrom-Json } catch { $null }) }
}

function Get-Data($response, [string]$property) {
    if ($response.Json -and $response.Json.Data) { return $response.Json.Data.$property }
    return $null
}

function Get-ConnectionBuilder {
    $cfg = Get-Content -LiteralPath (Join-Path $root "MediBridge.APIs\appsettings.json") -Raw | ConvertFrom-Json
    return New-Object System.Data.SqlClient.SqlConnectionStringBuilder $cfg.ConnectionStrings.DefaultConnection
}

function Invoke-SqlRows([string]$query) {
    $b = Get-ConnectionBuilder
    $out = & sqlcmd -S $b.DataSource -d $b.InitialCatalog -U $b.UserID -P $b.Password -C -W -s "|" -h -1 -Q "SET NOCOUNT ON; $query"
    return @($out | Where-Object { $_ -and $_ -notmatch '^\(\d+ rows affected\)$' })
}

function Get-SqlScalar([string]$query) {
    $rows = @(Invoke-SqlRows $query)
    if ($rows.Count -gt 0) { return ([string]$rows[0]).Trim() }
    return ""
}

function AuthHeader([string]$token) {
    return @{ Authorization = "Bearer $token" }
}

if ([string]::IsNullOrWhiteSpace($AdminPassword)) {
    throw "AdminPassword or MEDIBRIDGE_E2E_ADMIN_PASSWORD is required."
}

$b = Get-ConnectionBuilder
$data.database.server = $b.DataSource
$data.database.name = $b.InitialCatalog
$data.database.user = "[MASKED]"

$resume = $false
if (-not [string]::IsNullOrWhiteSpace($StatePath) -and (Test-Path -LiteralPath $StatePath)) {
    $resume = $true
    $prior = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
    $data.resumedFrom = (Resolve-Path -LiteralPath $StatePath).Path
}

$pdfPath = Join-Path $assetDir "verification.pdf"
$pngPath = Join-Path $assetDir "campaign-media.png"
[IO.File]::WriteAllBytes($pdfPath, [Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj`n2 0 obj<</Type/Pages/Count 0>>endobj`ntrailer<</Root 1 0 R>>`n%%EOF"))
[IO.File]::WriteAllBytes($pngPath, [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lV9h6QAAAABJRU5ErkJggg=="))
$data.assets = @($pdfPath, $pngPath)

foreach ($p in @("/", "/swagger", "/swagger/index.html", "/swagger/v1/swagger.json", "/WeatherForecast?count=1")) {
    Invoke-Api -Scenario "initial GET $p" -Method GET -Path $p -Expected @(200,404) -DatabaseEffect "none" | Out-Null
}

$adminLogin = Invoke-Api -Scenario "admin valid login" -Method POST -Path "/api/auth/login" -Body @{ Username = $AdminUsername; Password = $AdminPassword } -Expected @(200) -DatabaseEffect "RefreshCredentials insert; AuthenticationAuditEvents insert"
$adminToken = Get-Data $adminLogin "AccessToken"
$adminRefresh = Get-Data $adminLogin "RefreshToken"
$data.admin.role = Get-Data $adminLogin "Role"
$data.admin.expiresAtUtc = Get-Data $adminLogin "ExpiresAtUtc"

if (-not $resume) {
    Invoke-Api -Scenario "admin invalid password" -Method POST -Path "/api/auth/login" -Body @{ Username = $AdminUsername; Password = "Wrong123!" } -Expected @(401) -DatabaseEffect "AuthenticationAuditEvents insert" | Out-Null
    Invoke-Api -Scenario "login missing username" -Method POST -Path "/api/auth/login" -Body @{ Password = $AdminPassword } -Expected @(400) -DatabaseEffect "none" | Out-Null
    Invoke-Api -Scenario "login missing password" -Method POST -Path "/api/auth/login" -Body @{ Username = $AdminUsername } -Expected @(400) -DatabaseEffect "none" | Out-Null

    $ref1 = Invoke-Api -Scenario "admin refresh rotation" -Method POST -Path "/api/auth/refresh" -Body @{ RefreshToken = $adminRefresh } -Expected @(200) -DatabaseEffect "RefreshCredentials rotate"
    $adminToken2 = Get-Data $ref1 "AccessToken"
    $adminRefresh2 = Get-Data $ref1 "RefreshToken"
    Invoke-Api -Scenario "reuse revoked refresh token" -Method POST -Path "/api/auth/refresh" -Body @{ RefreshToken = $adminRefresh } -Expected @(409) -DatabaseEffect "RefreshCredentials family revoked" | Out-Null
    $adminLogin2 = Invoke-Api -Scenario "admin relogin after refresh-reuse test" -Method POST -Path "/api/auth/login" -Body @{ Username = $AdminUsername; Password = $AdminPassword } -Expected @(200) -DatabaseEffect "RefreshCredentials insert"
    $adminToken = Get-Data $adminLogin2 "AccessToken"
    $adminRefresh = Get-Data $adminLogin2 "RefreshToken"
}

$suffix = if ($resume) {
    (($prior.doctor.email -replace '^doctor-', '') -replace '@medibridge\.local$', '')
} else {
    (Get-Date -Format "yyyyMMddHHmmss") + "-" + ((New-Guid).Guid.Substring(0,8))
}
$doctorPassword = "Password1!"
$companyPassword = "Password1!"
$doctorEmail = if ($resume) { [string]$prior.doctor.email } else { "doctor-$suffix@medibridge.local" }
$companyEmail = if ($resume) { [string]$prior.company.email } else { "company-$suffix@medibridge.local" }
$doctorPhone = if ($resume) { [string]$prior.doctor.phone } else { "55512$((Get-Random -Minimum 10000 -Maximum 99999))" }
$companyPhone = if ($resume) { [string]$prior.company.phone } else { "55513$((Get-Random -Minimum 10000 -Maximum 99999))" }
$companyLicense = if ($resume) { [string]$prior.company.license } else { "lic-comp-$suffix" }
$data.doctor.email = $doctorEmail
$data.doctor.password = "[MASKED]"
$data.doctor.phone = $doctorPhone
$data.company.email = $companyEmail
$data.company.password = "[MASKED]"
$data.company.phone = $companyPhone
$data.company.license = $companyLicense

if ($resume) {
    $doctorUserId = [string]$prior.doctor.userId
    $companyUserId = [string]$prior.company.userId
    $data.doctor.userId = $doctorUserId
    $data.company.userId = $companyUserId
    Add-Trace "resume existing OTP-gated accounts" "N/A" "local-state:$StatePath" @{} "Local" $null 0 "Resumed existing generated accounts" 0 $true "none"
} else {
    $doctorReg = Invoke-Api -Scenario "register doctor" -Method POST -Path "/api/auth/register-doctor" -Body @{
        Email = $doctorEmail; Password = $doctorPassword; PhoneNumber = $doctorPhone; Specialization = "Cardiology"; ExperienceYears = 10; Location = "Cairo";
        VerificationMetadata = @{ DocumentType = "License"; OriginalFileName = "doctor-license.pdf"; ContentType = "application/pdf"; SizeBytes = 2048; Reference = "doc-ref-$suffix" }
    } -Expected @(201) -DatabaseEffect "Users, DoctorProfiles, ContactVerificationFlows, AuthenticationAuditEvents inserts"
    $doctorUserId = Get-Data $doctorReg "UserId"
    $data.doctor.userId = $doctorUserId

    $companyReg = Invoke-Api -Scenario "register company" -Method POST -Path "/api/auth/register-company" -Body @{
        Email = $companyEmail; Password = $companyPassword; PhoneNumber = $companyPhone; CompanyName = "E2E Pharma Egypt $suffix"; LicenseNumber = $companyLicense; ContactName = "E2E Contact";
        VerificationMetadata = @{ DocumentType = "TradeLicense"; OriginalFileName = "company-license.pdf"; ContentType = "application/pdf"; SizeBytes = 4096; Reference = "comp-ref-$suffix" }
    } -Expected @(201) -DatabaseEffect "Users, CompanyProfiles, ContactVerificationFlows, Wallets, AuthenticationAuditEvents inserts"
    $companyUserId = Get-Data $companyReg "UserId"
    $data.company.userId = $companyUserId

    Invoke-Api -Scenario "doctor login denied before verification approval" -Method POST -Path "/api/auth/login" -Body @{ Username = $doctorEmail; Password = $doctorPassword } -Expected @(403) -DatabaseEffect "AuthenticationAuditEvents insert" | Out-Null
    Invoke-Api -Scenario "company login denied before verification approval" -Method POST -Path "/api/auth/login" -Body @{ Username = $companyEmail; Password = $companyPassword } -Expected @(403) -DatabaseEffect "AuthenticationAuditEvents insert" | Out-Null
}

if ([string]::IsNullOrWhiteSpace($DoctorOtp) -or [string]::IsNullOrWhiteSpace($CompanyOtp)) {
    $data.blockers += "OTP_REQUIRED"
    $data.otpNeeded = [ordered]@{
        doctorEmail = $doctorEmail
        companyEmail = $companyEmail
        doctorUserId = $doctorUserId
        companyUserId = $companyUserId
        endpoint = "/api/auth/verify-contact"
        step = "after registration, before contact verification"
    }
    Save-Artifacts
    Write-Host "OTP_REQUIRED"
    Write-Host "DoctorEmail=$doctorEmail"
    Write-Host "CompanyEmail=$companyEmail"
    Write-Host "TracePath=$tracePath"
    Write-Host "DataPath=$dataPath"
    exit 2
}

$data.otpRetrievalSource = "Gmail connector or manual input; OTP values masked"
$doctorIdentityState = Get-SqlScalar "SELECT CONCAT(AccountStatus, '|verified=', EmailVerified) FROM Users WHERE Id = '$doctorUserId';"
$companyIdentityState = Get-SqlScalar "SELECT CONCAT(AccountStatus, '|verified=', EmailVerified) FROM Users WHERE Id = '$companyUserId';"
if ($doctorIdentityState -match 'verified=True' -and $companyIdentityState -match 'verified=True') {
    Add-Trace "skip otp verification already complete" "N/A" "sql:Users.EmailVerified" @{} "Local" $null 0 "Both generated users already verified" 0 $true "read Users"
} else {
    Invoke-Api -Scenario "doctor wrong otp rejected" -Method POST -Path "/api/auth/verify-contact" -Body @{ Email = $doctorEmail; Channel = "Email"; Otp = "000000" } -Expected @(400) -DatabaseEffect "ContactVerificationFlows failed-attempt increment; AuthenticationAuditEvents insert" | Out-Null
    Invoke-Api -Scenario "doctor otp verification" -Method POST -Path "/api/auth/verify-contact" -Body @{ Email = $doctorEmail; Channel = "Email"; Otp = $DoctorOtp } -Expected @(200) -DatabaseEffect "Users.EmailVerified update; ContactVerificationFlows consumed"
    Invoke-Api -Scenario "company otp verification" -Method POST -Path "/api/auth/verify-contact" -Body @{ Email = $companyEmail; Channel = "Email"; Otp = $CompanyOtp } -Expected @(200) -DatabaseEffect "Users.EmailVerified update; ContactVerificationFlows consumed"
}

$adminHeaders = AuthHeader $adminToken
Invoke-Api -Scenario "admin list pending accounts" -Method GET -Path "/api/admin/pending-accounts?pageNumber=1&pageSize=20" -Headers $adminHeaders -Role Admin -Expected @(200) -DatabaseEffect "read Users/Profiles" | Out-Null
$doctorIdentityState = Get-SqlScalar "SELECT CONCAT(AccountStatus, '|verified=', EmailVerified) FROM Users WHERE Id = '$doctorUserId';"
$companyIdentityState = Get-SqlScalar "SELECT CONCAT(AccountStatus, '|verified=', EmailVerified) FROM Users WHERE Id = '$companyUserId';"
if ($doctorIdentityState -match '^Approved' -and $companyIdentityState -match '^Approved') {
    Add-Trace "skip account approvals already complete" "N/A" "sql:Users.AccountStatus" @{} "Local" $null 0 "Both generated users already approved" 0 $true "read Users"
} else {
    Invoke-Api -Scenario "admin approve doctor" -Method PUT -Path "/api/admin/accounts/$doctorUserId/decision" -Headers $adminHeaders -Role Admin -Body @{ Decision = "Approve"; Notes = "E2E approved doctor" } -Expected @(200) -DatabaseEffect "Users.AccountStatus update; AdminAccountDecisions insert"
    Invoke-Api -Scenario "admin approve company" -Method PUT -Path "/api/admin/accounts/$companyUserId/decision" -Headers $adminHeaders -Role Admin -Body @{ Decision = "Approve"; Notes = "E2E approved company" } -Expected @(200) -DatabaseEffect "Users.AccountStatus update; AdminAccountDecisions insert"
}

$doctorProfileId = Get-SqlScalar "SELECT Id FROM DoctorProfiles WHERE UserId = '$doctorUserId';"
$companyProfileId = Get-SqlScalar "SELECT Id FROM CompanyProfiles WHERE UserId = '$companyUserId';"
$walletId = Get-SqlScalar "SELECT TOP(1) Id FROM Wallets WHERE OwnerId = '$companyProfileId';"
$data.doctor.profileId = $doctorProfileId
$data.company.companyId = $companyProfileId
$data.company.walletId = $walletId
$data.wallets += [ordered]@{ id = $walletId; owner = $companyProfileId; ownerType = "Company" }

Invoke-Api -Scenario "admin set doctor price" -Method PUT -Path "/api/admin/doctors/$doctorProfileId/price" -Headers $adminHeaders -Role Admin -Body @{ PricePerMessage = 75.00; Reason = "E2E pricing setup" } -Expected @(200) -DatabaseEffect "DoctorProfiles/DoctorPriceHistories update"

$doctorLogin = Invoke-Api -Scenario "doctor login after approval" -Method POST -Path "/api/auth/login" -Body @{ Username = $doctorEmail; Password = $doctorPassword } -Expected @(200) -DatabaseEffect "RefreshCredentials insert; AuthenticationAuditEvents insert"
$doctorToken = Get-Data $doctorLogin "AccessToken"
$companyLogin = Invoke-Api -Scenario "company login after approval" -Method POST -Path "/api/auth/login" -Body @{ Username = $companyEmail; Password = $companyPassword } -Expected @(200) -DatabaseEffect "RefreshCredentials insert; AuthenticationAuditEvents insert"
$companyToken = Get-Data $companyLogin "AccessToken"
$doctorHeaders = AuthHeader $doctorToken
$companyHeaders = AuthHeader $companyToken

Invoke-Api -Scenario "anonymous company wallet rejected" -Method GET -Path "/api/company/wallet" -Expected @(401) -DatabaseEffect "none" | Out-Null
Invoke-Api -Scenario "doctor wrong-role company wallet rejected" -Method GET -Path "/api/company/wallet" -Headers $doctorHeaders -Role Doctor -Expected @(403) -DatabaseEffect "none" | Out-Null
Invoke-Api -Scenario "company wallet query" -Method GET -Path "/api/company/wallet" -Headers $companyHeaders -Role Company -Expected @(200) -DatabaseEffect "read Wallets/WalletTransactions" | Out-Null
$topupKey = "topup-$((New-Guid).Guid)"
$data.idempotencyKeys += $topupKey
Invoke-Api -Scenario "company mock topup" -Method POST -Path "/api/company/wallet/mock-checkout" -Headers ($companyHeaders + @{ "Idempotency-Key" = $topupKey }) -Role Company -Body @{ amount = 1000.00; currency = "EGP" } -Expected @(200) -DatabaseEffect "Wallets balance credit; WalletTransactions/WalletLedgerEntries/MockPaymentTransactions insert"
Invoke-Api -Scenario "company mock topup replay same key" -Method POST -Path "/api/company/wallet/mock-checkout" -Headers ($companyHeaders + @{ "Idempotency-Key" = $topupKey }) -Role Company -Body @{ amount = 1000.00; currency = "EGP" } -Expected @(200) -DatabaseEffect "idempotent replay; no duplicate financial effect"
Invoke-Api -Scenario "company invalid topup amount" -Method POST -Path "/api/company/wallet/mock-checkout" -Headers ($companyHeaders + @{ "Idempotency-Key" = "bad-$((New-Guid).Guid)" }) -Role Company -Body @{ amount = 50.00; currency = "EGP" } -Expected @(400) -DatabaseEffect "none"

Invoke-Api -Scenario "doctor upload verification document" -Method POST -Path "/api/files/verification-documents" -Headers $doctorHeaders -Role Doctor -Form @{ File = (Get-Item $pdfPath) } -Expected @(201) -DatabaseEffect "StoredFiles insert; Cloudinary upload" | Tee-Object -Variable doctorFileResp | Out-Null
$doctorFileId = Get-Data $doctorFileResp "Id"
$data.files += [ordered]@{ id = $doctorFileId; owner = $doctorProfileId; purpose = "VerificationDocument"; createdBy = "Doctor" }
Invoke-Api -Scenario "admin review doctor verification file" -Method PUT -Path "/api/admin/files/$doctorFileId/review" -Headers $adminHeaders -Role Admin -Body @{ Decision = "Approved"; Notes = "E2E verification approved" } -Expected @(200) -DatabaseEffect "FileReviews insert; StoredFiles review status update" | Out-Null
Invoke-Api -Scenario "doctor get signed file access" -Method POST -Path "/api/files/$doctorFileId/access" -Headers $doctorHeaders -Role Doctor -Expected @(200) -DatabaseEffect "FileAccessGrantAudits insert" | Tee-Object -Variable fileGrant | Out-Null
if ($fileGrant.Json -and $fileGrant.Json.Data.Url) {
    Invoke-Api -Scenario "signed Cloudinary URL HEAD" -Method HEAD -Path $fileGrant.Json.Data.Url -Role Anonymous -Expected @(200,401,403) -DatabaseEffect "Cloudinary read only" | Out-Null
}

$draft = Invoke-Api -Scenario "company create campaign draft" -Method POST -Path "/api/company/campaigns/drafts" -Headers $companyHeaders -Role Company -Body @{ Title = "E2E Campaign $suffix"; Description = "A cardiology campaign created by deployed E2E smoke test."; ClinicalResearchInfo = "E2E clinical info" } -Expected @(201) -DatabaseEffect "Campaigns insert"
$campaignId = Get-Data $draft "CampaignId"
$data.campaigns += [ordered]@{ id = $campaignId; companyId = $companyProfileId }
Invoke-Api -Scenario "company list campaigns" -Method GET -Path "/api/company/campaigns" -Headers $companyHeaders -Role Company -Expected @(200) -DatabaseEffect "read Campaigns" | Out-Null
Invoke-Api -Scenario "company campaign detail" -Method GET -Path "/api/company/campaigns/$campaignId" -Headers $companyHeaders -Role Company -Expected @(200) -DatabaseEffect "read Campaigns/StoredFiles" | Out-Null
Invoke-Api -Scenario "company upload campaign asset" -Method POST -Path "/api/company/campaigns/$campaignId/assets" -Headers $companyHeaders -Role Company -Form @{ file = (Get-Item $pngPath) } -Expected @(201) -DatabaseEffect "StoredFiles insert; Cloudinary upload" | Tee-Object -Variable assetResp | Out-Null
$assetId = Get-Data $assetResp "Id"
$storageRow = Get-SqlScalar "SELECT CONCAT(StorageProvider, '|', StorageKey, '|', ContentType, '|', SizeBytes, '|', UploadStatus) FROM StoredFiles WHERE Id = '$assetId';"
$data.files += [ordered]@{ id = $assetId; owner = $campaignId; purpose = "CampaignMedia"; storage = $storageRow }
Invoke-Api -Scenario "admin review campaign asset" -Method POST -Path "/api/admin/campaign-assets/$assetId/review" -Headers $adminHeaders -Role Admin -Body @{ Decision = "Approved"; Notes = "E2E asset approved" } -Expected @(200) -DatabaseEffect "FileReviews insert; StoredFiles review status update" | Out-Null
Invoke-Api -Scenario "company target preview" -Method GET -Path "/api/company/campaigns/$campaignId/target-preview" -Headers $companyHeaders -Role Company -Expected @(200) -DatabaseEffect "read eligible doctors" | Out-Null
$submitKey = "submit-$((New-Guid).Guid)"
$data.idempotencyKeys += $submitKey
Invoke-Api -Scenario "company submit campaign" -Method POST -Path "/api/company/campaigns/$campaignId/submit" -Headers ($companyHeaders + @{ "Idempotency-Key" = $submitKey }) -Role Company -Expected @(200) -DatabaseEffect "CampaignSubmissionRequests insert; Campaign status PendingReview"
Invoke-Api -Scenario "company duplicate submit invalid state" -Method POST -Path "/api/company/campaigns/$campaignId/submit" -Headers ($companyHeaders + @{ "Idempotency-Key" = "submit-dup-$((New-Guid).Guid)" }) -Role Company -Expected @(409) -DatabaseEffect "no state change"
Invoke-Api -Scenario "admin list pending campaigns" -Method GET -Path "/api/admin/campaigns/pending-review" -Headers $adminHeaders -Role Admin -Expected @(200) -DatabaseEffect "read Campaigns" | Out-Null
Invoke-Api -Scenario "admin campaign review detail" -Method GET -Path "/api/admin/campaigns/$campaignId/review-detail" -Headers $adminHeaders -Role Admin -Expected @(200) -DatabaseEffect "read Campaigns/Files" | Out-Null
$reviewKey = "review-$((New-Guid).Guid)"
$data.idempotencyKeys += $reviewKey
Invoke-Api -Scenario "admin approve campaign" -Method POST -Path "/api/admin/campaigns/$campaignId/review" -Headers ($adminHeaders + @{ "Idempotency-Key" = $reviewKey }) -Role Admin -Body @{ decision = "Approved"; notes = "E2E approved campaign"; reason = $null } -Expected @(200) -DatabaseEffect "Campaigns approved; CampaignTargets/DoctorMessageQueues rows created"
Invoke-Api -Scenario "admin campaign queue rows" -Method GET -Path "/api/admin/campaigns/$campaignId/queue" -Headers $adminHeaders -Role Admin -Expected @(200) -DatabaseEffect "read DoctorMessageQueues" | Tee-Object -Variable queueResp | Out-Null
Invoke-Api -Scenario "doctor today inbox" -Method GET -Path "/api/doctor/messages/today?PageSize=20" -Headers $doctorHeaders -Role Doctor -Expected @(200) -DatabaseEffect "read DoctorAdDeliveries/DoctorMessageQueues" | Tee-Object -Variable inboxResp | Out-Null

$deliveryId = Get-SqlScalar "SELECT TOP(1) Id FROM DoctorAdDeliveries WHERE CampaignId = '$campaignId' AND DoctorId = '$doctorProfileId' ORDER BY CreatedAtUtc DESC;"
$queueId = Get-SqlScalar "SELECT TOP(1) Id FROM DoctorMessageQueues WHERE CampaignId = '$campaignId' AND DoctorId = '$doctorProfileId' ORDER BY CreatedAtUtc DESC;"
$data.campaigns[0].queueId = $queueId
$data.campaigns[0].deliveryId = $deliveryId
if ([string]::IsNullOrWhiteSpace($deliveryId)) {
    $data.blockers += "Delivery activation/interactions blocked: no exposed job endpoint and no activated DoctorAdDelivery was created during this run."
} else {
    Invoke-Api -Scenario "doctor mark delivery read" -Method PUT -Path "/api/doctor/messages/$deliveryId/read" -Headers $doctorHeaders -Role Doctor -Expected @(200) -DatabaseEffect "DoctorAdDeliveries read timestamp update"
    $interactionKey = "interact-$((New-Guid).Guid)"
    $data.idempotencyKeys += $interactionKey
    Invoke-Api -Scenario "doctor accept delivery" -Method POST -Path "/api/doctor/messages/$deliveryId/interact" -Headers ($doctorHeaders + @{ "Idempotency-Key" = $interactionKey }) -Role Doctor -Body @{ Decision = "Accept"; FeedbackText = "E2E accepted." } -Expected @(200) -DatabaseEffect "settlement WalletTransactions/LedgerEntries; Delivery status accepted"
    Invoke-Api -Scenario "doctor accept delivery replay" -Method POST -Path "/api/doctor/messages/$deliveryId/interact" -Headers ($doctorHeaders + @{ "Idempotency-Key" = $interactionKey }) -Role Doctor -Body @{ Decision = "Accept"; FeedbackText = "E2E accepted." } -Expected @(200) -DatabaseEffect "idempotent replay; no duplicate settlement"
}

Invoke-Api -Scenario "admin logout" -Method POST -Path "/api/auth/logout" -Headers (AuthHeader $adminToken) -Role Admin -Body @{ RefreshToken = $adminRefresh } -Expected @(200) -DatabaseEffect "RefreshCredentials family revoked"
Invoke-Api -Scenario "refresh after logout denied" -Method POST -Path "/api/auth/refresh" -Body @{ RefreshToken = $adminRefresh } -Expected @(409,401) -DatabaseEffect "revoked token/session rejected"

$data.database.doctorStatus = Get-SqlScalar "SELECT CONCAT(AccountStatus, '|verified=', EmailVerified) FROM Users WHERE Id = '$doctorUserId';"
$data.database.companyStatus = Get-SqlScalar "SELECT CONCAT(AccountStatus, '|verified=', EmailVerified) FROM Users WHERE Id = '$companyUserId';"
$data.database.walletSummary = Get-SqlScalar "SELECT CONCAT(Id, '|available=', AvailableBalance, '|reserved=', ReservedBalance) FROM Wallets WHERE Id = '$walletId';"
$data.database.walletTransactionCount = Get-SqlScalar "SELECT COUNT(*) FROM WalletTransactions WHERE WalletId = '$walletId';"
$data.database.queueCount = Get-SqlScalar "SELECT COUNT(*) FROM DoctorMessageQueues WHERE CampaignId = '$campaignId';"
$data.database.deliveryCount = Get-SqlScalar "SELECT COUNT(*) FROM DoctorAdDeliveries WHERE CampaignId = '$campaignId';"

Save-Artifacts
Write-Host "TracePath=$tracePath"
Write-Host "DataPath=$dataPath"
Write-Host "AssetDir=$assetDir"
Write-Host "DoctorEmail=$doctorEmail"
Write-Host "CompanyEmail=$companyEmail"
Write-Host "CampaignId=$campaignId"
