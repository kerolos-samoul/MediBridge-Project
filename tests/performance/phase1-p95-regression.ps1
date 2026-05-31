param(
    [Parameter(Mandatory = $true)]
    [double]$BaselineP95Ms,

    [string]$BaseUrl = "https://localhost:5001",
    [string]$Path = "/weatherforecast",
    [int]$Runs = 3,
    [int]$DurationSeconds = 300,
    [int]$Concurrency = 10,
    [int]$MinRequestsPerRun = 1000,
    [double]$AllowedRegressionPercent = 10
)

$ErrorActionPreference = "Stop"

if ($BaselineP95Ms -le 0) {
    throw "BaselineP95Ms must be greater than zero."
}

$targetP95 = $BaselineP95Ms * (1 + ($AllowedRegressionPercent / 100.0))
$uri = [Uri]::new($BaseUrl.TrimEnd("/") + $Path)
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.ServerCertificateCustomValidationCallback = { $true }
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(30)

try {
    $results = @()

    for ($run = 1; $run -le $Runs; $run++) {
        $latencies = [System.Collections.Concurrent.ConcurrentBag[double]]::new()
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
        $workers = @()

        for ($worker = 1; $worker -le $Concurrency; $worker++) {
            $workers += [System.Threading.Tasks.Task]::Run([Action]{
                while ([DateTimeOffset]::UtcNow -lt $deadline) {
                    $sw = [System.Diagnostics.Stopwatch]::StartNew()
                    $response = $client.GetAsync($uri).GetAwaiter().GetResult()
                    $response.Dispose()
                    $sw.Stop()
                    $latencies.Add($sw.Elapsed.TotalMilliseconds)
                }
            })
        }

        [System.Threading.Tasks.Task]::WaitAll($workers)

        $ordered = @($latencies.ToArray() | Sort-Object)
        if ($ordered.Count -lt $MinRequestsPerRun) {
            throw "Run $run completed $($ordered.Count) requests, below required minimum $MinRequestsPerRun."
        }

        $p95Index = [Math]::Min($ordered.Count - 1, [Math]::Ceiling($ordered.Count * 0.95) - 1)
        $p95 = [double]$ordered[$p95Index]
        $passed = $p95 -le $targetP95
        $results += [PSCustomObject]@{
            Run = $run
            Requests = $ordered.Count
            P95Ms = [Math]::Round($p95, 2)
            TargetP95Ms = [Math]::Round($targetP95, 2)
            Status = if ($passed) { "PASS" } else { "FAIL" }
        }
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
