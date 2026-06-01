param(
    [string]$BaseUrl = "https://localhost:5001",
    [double]$TargetMilliseconds = 2000,
    [string]$AdminAccessToken = "",
    [string]$ResubmissionToken = "",
    [string]$ResetToken = "",
    [string]$VerificationToken = "",
    [switch]$SkipAdminApproval,
    [switch]$SkipReset,
    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"

if ($TargetMilliseconds -le 0) {
    throw "TargetMilliseconds must be greater than zero."
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.ServerCertificateCustomValidationCallback = { $true }
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$base = $BaseUrl.TrimEnd("/")
$results = [System.Collections.Generic.List[object]]::new()

function Invoke-SmokeRequest {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$BearerToken = ""
    )

    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$base$Path")
    if ($BearerToken) {
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $BearerToken)
    }

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 8
        $request.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, "application/json")
    }

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $watch.Stop()

    $results.Add([PSCustomObject]@{
        Step = $Name
        StatusCode = [int]$response.StatusCode
        ElapsedMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 2)
        TargetMs = $TargetMilliseconds
        Status = if ($watch.Elapsed.TotalMilliseconds -le $TargetMilliseconds) { "PASS" } else { "FAIL" }
    })

    return $response
}

function Read-EnvelopeData {
    param(
        [System.Net.Http.HttpResponseMessage]$Response
    )

    $json = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([string]::IsNullOrWhiteSpace($json)) {
        return $null
    }

    return ($json | ConvertFrom-Json).Data
}

try {
    $suffix = [Guid]::NewGuid().ToString("N")
    $doctorEmail = "phase2-smoke-doctor-$suffix@example.com"
    $password = "Password1!"
    $metadata = @{
        documentType = "License"
        originalFileName = "license.pdf"
        contentType = "application/pdf"
        sizeBytes = 1024
        reference = "smoke-$suffix"
    }

    $registrationResponse = Invoke-SmokeRequest "Register Doctor" "POST" "/api/auth/register-doctor" @{
        email = $doctorEmail
        password = $password
        phoneNumber = "555$((Get-Random -Minimum 1000000 -Maximum 9999999))"
        specialization = "Cardiology"
        experienceYears = 5
        location = "Lagos"
        verificationMetadata = $metadata
    }

    $registeredUserId = (Read-EnvelopeData $registrationResponse).userId

    Invoke-SmokeRequest "Pending Login Denial" "POST" "/api/auth/login" @{
        username = $doctorEmail
        password = $password
    } | Out-Null

    if (-not $SkipAdminApproval -and $AdminAccessToken) {
        Invoke-SmokeRequest "List Pending Accounts" "GET" "/api/admin/pending-accounts?PageNumber=1&PageSize=20" $null $AdminAccessToken | Out-Null

        Invoke-SmokeRequest "Approve Doctor" "PUT" "/api/admin/accounts/$registeredUserId/decision" @{
            decision = "Approve"
            notes = "Phase 2 smoke approval"
        } $AdminAccessToken | Out-Null

        $loginResponse = Invoke-SmokeRequest "Approved Login" "POST" "/api/auth/login" @{
            username = $doctorEmail
            password = $password
        }

        $loginData = Read-EnvelopeData $loginResponse
        $accessToken = $loginData.accessToken
        $refreshToken = $loginData.refreshToken

        $refreshResponse = Invoke-SmokeRequest "Refresh" "POST" "/api/auth/refresh" @{
            refreshToken = $refreshToken
        }

        $refreshData = Read-EnvelopeData $refreshResponse
        $rotatedRefreshToken = $refreshData.refreshToken

        Invoke-SmokeRequest "Logout" "POST" "/api/auth/logout" @{
            refreshToken = $rotatedRefreshToken
        } $accessToken | Out-Null
    }
    elseif (-not $SkipAdminApproval) {
        Write-Host "AdminAccessToken was not supplied; approval, approved login, refresh, and logout smoke steps were skipped."
    }

    if (-not $SkipReset -and $ResetToken) {
        Invoke-SmokeRequest "Reset Password" "POST" "/api/auth/reset-password" @{
            resetToken = $ResetToken
            newPassword = "Password2!"
        } | Out-Null
    }

    if (-not $SkipVerification -and $VerificationToken) {
        Invoke-SmokeRequest "Verify Contact" "POST" "/api/auth/verify-contact" @{
            channel = "Email"
            verificationToken = $VerificationToken
        } | Out-Null
    }

    if ($ResubmissionToken) {
        Invoke-SmokeRequest "Resubmit Registration" "POST" "/api/auth/resubmit-registration" @{
            resubmissionToken = $ResubmissionToken
            specialization = "Cardiology"
            experienceYears = 6
            location = "Lagos"
            verificationMetadata = $metadata
        } | Out-Null
    }

    $results | Format-Table -AutoSize

    if (($results | Where-Object Status -eq "FAIL").Count -gt 0) {
        exit 1
    }
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
