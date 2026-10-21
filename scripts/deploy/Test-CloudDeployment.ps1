<#
.SYNOPSIS
    Smoke-tests the deployed cloud application: confirms the portal and all 5 frontends are
    reachable, and that each frontend's nginx proxy genuinely reaches its real backend/database
    (using one known GET endpoint per service, the same ones manually verified during Phase 1).
    Writes evidence.json and report.md for the technical report.

.PARAMETER ResourceGroupName
    Defaults to 'rg-campuspulse'.

.PARAMETER EnableAi
    Checks MCP connectivity instead of the baseline AI-disabled gates.

.NOTES
    This checks the externally-reachable apps (frontends + portal) and, through their /api/
    proxy, the full web -> api -> database chain. It does not separately probe the internal-only
    api/db apps directly (they have no public endpoint to reach from outside Azure - see
    cloud-deployment-plan.md for the internal-ingress design).

    Every backend route requires a valid JWT by default (FallbackPolicy). This script signs in as
    the seeded student STU001 (password "test", seeded by AcademicRecordsSeedData.cs - the same
    account for local and cloud, since both come from the same seed) via academicrecords-web's
    /api/auth/token proxy, then sends that token as a Bearer header on every check below.

    It also does not assert "AI is disabled" as a single HTTP check across all services: Financial
    Aid Billing's endpoints return an explicit 403 when disabled (checked below), but the other
    4 services' chat orchestrators degrade by silently not offering the RAG/MCP route rather than
    returning an error code - that's correct, graceful behaviour, but not something a single
    automated assertion can observe. Confirm those via the UI screenshots in the report instead.

    On any failed check, it prints that check's own apps' Container App revision health and any
    recent error-looking console log lines directly into the CI log (via Write-AppDiagnostics) -
    the same evidence that otherwise takes a manual `az containerapp revision list` /
    `az monitor log-analytics query` round trip to find.
#>
param(
    [string]$ResourceGroupName = 'rg-campuspulse',
    [switch]$EnableAi
)

$ErrorActionPreference = 'Stop'

# One known, real GET endpoint per frontend - the exact ones manually verified in Phase 1 - plus
# the api/db pair behind it, so a failure can pull diagnostics for the actual apps involved.
$services = @(
    @{ App = 'academicrecords-web'; Path = '/api/users'; Api = 'academicrecords-api'; Db = 'academicrecords-db' }
    @{ App = 'coursemgmt-web'; Path = '/api/courses'; Api = 'coursemgmt-api'; Db = 'coursemgmt-db' }
    @{ App = 'campushousing-web'; Path = '/api/housing/rooms'; Api = 'campushousing-api'; Db = 'campushousing-db' }
    @{ App = 'finaid-web'; Path = '/api/invoices'; Api = 'finaid-api'; Db = 'finaid-db' }
    @{ App = 'eventscheduling-web'; Path = '/api/rooms'; Api = 'eventscheduling-api'; Db = 'eventscheduling-db' }
)

function Get-AppFqdn([string]$AppName) {
    $fqdn = az containerapp show --resource-group $ResourceGroupName --name $AppName --query 'properties.configuration.ingress.fqdn' --output tsv
    if ([string]::IsNullOrWhiteSpace($fqdn)) {
        throw "Could not resolve the FQDN for '$AppName' - is it deployed?"
    }
    return $fqdn
}

# Lazily resolved once, only if a check actually fails - avoids the extra az call on a clean run.
$script:logAnalyticsWorkspaceId = $null
function Get-LogAnalyticsWorkspaceId {
    if (-not $script:logAnalyticsWorkspaceId) {
        $script:logAnalyticsWorkspaceId = az monitor log-analytics workspace list --resource-group $ResourceGroupName --query '[0].customerId' --output tsv
    }
    return $script:logAnalyticsWorkspaceId
}

# Prints, directly into the CI log, exactly the diagnostics that otherwise take a manual
# `az containerapp revision list` + `az monitor log-analytics query` round trip to find: each
# app's revision health, and any recent error-looking lines from its own console output - the
# same evidence that traced the SQLite "database is locked" failures back to their real cause.
function Write-AppDiagnostics([string[]]$AppNames) {
    Write-Host "`n--- Diagnostics ---" -ForegroundColor Yellow
    foreach ($appName in $AppNames) {
        Write-Host "`n[$appName] revisions:" -ForegroundColor Yellow
        az containerapp revision list --resource-group $ResourceGroupName --name $appName `
            --query '[].{name:name, active:properties.active, health:properties.healthState, running:properties.runningState, replicas:properties.replicas}' `
            --output table
    }

    $workspaceId = Get-LogAnalyticsWorkspaceId
    if ([string]::IsNullOrWhiteSpace($workspaceId)) {
        Write-Host "`nNo Log Analytics workspace found for $ResourceGroupName - skipping log query." -ForegroundColor Yellow
        return
    }

    foreach ($appName in $AppNames) {
        Write-Host "`n[$appName] recent error-looking log lines (last 10 minutes):" -ForegroundColor Yellow
        # A multi-line here-string query broke silently when passed through `az` as a single CLI
        # argument on Windows (the where-clauses were dropped, dumping every log line unfiltered)
        # - keep this on one line to avoid that.
        $query = "ContainerAppConsoleLogs_CL | where ContainerAppName_s == '$appName' | where TimeGenerated > ago(10m) | where Log_s contains 'error' or Log_s contains 'exception' or Log_s contains 'fail:' or Log_s contains 'locked' or Log_s contains 'unhandled' | project TimeGenerated, Log_s | order by TimeGenerated asc | take 30"
        $lines = az monitor log-analytics query --workspace $workspaceId --analytics-query $query --output tsv 2>&1
        if ([string]::IsNullOrWhiteSpace($lines)) {
            Write-Host "  (none found)"
        } else {
            $lines | ForEach-Object { Write-Host "  $_" }
        }
    }
    Write-Host "--- End diagnostics ---`n" -ForegroundColor Yellow
}

$results = @()

Write-Host "Checking the portal..." -ForegroundColor Cyan
$portalFqdn = Get-AppFqdn -AppName 'campuspulse-portal'
try {
    $response = Invoke-WebRequest -Uri "https://$portalFqdn/" -UseBasicParsing -TimeoutSec 60
    $results += [pscustomobject]@{ Check = 'portal root'; Url = "https://$portalFqdn/"; Status = $response.StatusCode; Passed = ($response.StatusCode -eq 200) }
}
catch {
    $results += [pscustomobject]@{ Check = 'portal root'; Url = "https://$portalFqdn/"; Status = 'error'; Passed = $false }
}

Write-Host "Signing in as the seeded test student (STU001) to authenticate the checks below..." -ForegroundColor Cyan
$academicRecordsFqdn = Get-AppFqdn -AppName 'academicrecords-web'
$tokenBody = @{ AccountId = 'STU001'; Role = 'Student'; Password = 'test' } | ConvertTo-Json
$authHeaders = $null
$authUrl = "https://$academicRecordsFqdn/api/auth/token"
try {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $tokenResponse = Invoke-WebRequest -Uri $authUrl -Method Post -Body $tokenBody -ContentType 'application/json' -UseBasicParsing -TimeoutSec 60
    $sw.Stop()
    Write-Host "  Sign-in responded in $($sw.Elapsed.TotalSeconds)s with status $($tokenResponse.StatusCode)."
    $accessToken = ($tokenResponse.Content | ConvertFrom-Json).accessToken
    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        throw 'Sign-in returned 200 but no accessToken in the response body.'
    }
    $authHeaders = @{ Authorization = "Bearer $accessToken" }
    $results += [pscustomobject]@{ Check = 'authentication (STU001 sign-in)'; Url = $authUrl; Status = $tokenResponse.StatusCode; Passed = $true }
}
catch {
    $statusCode = 'error'
    if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
    Write-Host "  Sign-in failed: $($_.Exception.GetType().Name): $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host "  Inner: $($_.Exception.InnerException.GetType().Name): $($_.Exception.InnerException.Message)" -ForegroundColor Red
    }
    $results += [pscustomobject]@{ Check = 'authentication (STU001 sign-in)'; Url = $authUrl; Status = $statusCode; Passed = $false }
    Write-AppDiagnostics -AppNames @('academicrecords-web', 'academicrecords-api', 'academicrecords-db')
}

if ($authHeaders) {
    foreach ($service in $services) {
        $fqdn = Get-AppFqdn -AppName $service.App
        $url = "https://$fqdn$($service.Path)"
        Write-Host "Checking $($service.App)$($service.Path)..." -ForegroundColor Cyan
        try {
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $response = Invoke-WebRequest -Uri $url -Headers $authHeaders -UseBasicParsing -TimeoutSec 60
            $sw.Stop()
            Write-Host "  Responded in $($sw.Elapsed.TotalSeconds)s with status $($response.StatusCode)."
            $results += [pscustomobject]@{ Check = "$($service.App) CRUD read (web -> api -> db)"; Url = $url; Status = $response.StatusCode; Passed = ($response.StatusCode -eq 200) }
        }
        catch {
            $statusCode = 'error'
            if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
            Write-Host "  Failed: $($_.Exception.GetType().Name): $($_.Exception.Message)" -ForegroundColor Red
            $results += [pscustomobject]@{ Check = "$($service.App) CRUD read (web -> api -> db)"; Url = $url; Status = $statusCode; Passed = $false }
            Write-AppDiagnostics -AppNames @($service.App, $service.Api, $service.Db)
        }
    }
} else {
    Write-Host "Skipping all CRUD checks - sign-in failed, there's no token to send." -ForegroundColor Yellow
    foreach ($service in $services) {
        $results += [pscustomobject]@{ Check = "$($service.App) CRUD read (web -> api -> db)"; Url = ''; Status = 'skipped (auth failed)'; Passed = $false }
    }
}

if ($authHeaders) {
    Write-Host "Checking Financial Aid's AI profile gates..." -ForegroundColor Cyan
    $finaidFqdn = Get-AppFqdn -AppName 'finaid-web'
    $aiChecks = @(
        @{ Path = '/api/assistant/mcp/integration'; ExpectedStatus = 403 }
        @{ Path = '/api/ai/knowledge'; ExpectedStatus = 403 }
    )
    if ($EnableAi) {
        $aiChecks = @(@{ Path = '/api/assistant/mcp/integration'; ExpectedStatus = 200 })
    }
    foreach ($check in $aiChecks) {
        $url = "https://$finaidFqdn$($check.Path)"
        # A 403 makes Invoke-WebRequest throw (it's not a 2xx), so the expected "disabled" response
        # is read from the exception, not a successful return - this also works on Windows
        # PowerShell 5.1, which has no -SkipHttpErrorCheck parameter (added in PS 7.4+).
        try {
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $response = Invoke-WebRequest -Uri $url -Method Post -Headers $authHeaders -Body '{}' -ContentType 'application/json' -UseBasicParsing -TimeoutSec 60
            $sw.Stop()
            Write-Host "  Responded in $($sw.Elapsed.TotalSeconds)s with status $($response.StatusCode)."
            $passed = $response.StatusCode -eq $check.ExpectedStatus
            if ($EnableAi) { $passed = $passed -and (($response.Content | ConvertFrom-Json).isError -eq $false) }
            $results += [pscustomobject]@{ Check = "AI profile: $($check.Path) returns $($check.ExpectedStatus)"; Url = $url; Status = $response.StatusCode; Passed = $passed }
        }
        catch {
            $statusCode = 'error'
            if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
            $passed = ($statusCode -eq $check.ExpectedStatus)
            if (-not $passed) {
                Write-Host "  Failed: $($_.Exception.GetType().Name): $($_.Exception.Message)" -ForegroundColor Red
                Write-AppDiagnostics -AppNames @('finaid-web', 'finaid-api', 'finaid-db')
            }
            $results += [pscustomobject]@{ Check = "AI profile: $($check.Path) returns $($check.ExpectedStatus)"; Url = $url; Status = $statusCode; Passed = $passed }
        }
    }
} else {
    Write-Host "Skipping AI profile gate checks - sign-in failed, there's no token to send." -ForegroundColor Yellow
    $results += [pscustomobject]@{ Check = 'AI profile gates'; Url = ''; Status = 'skipped (auth failed)'; Passed = $false }
}

$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$evidenceDir = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "docs/release-2/cloud-deployment-evidence/$runId"
New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null

$results | ConvertTo-Json | Out-File (Join-Path $evidenceDir 'evidence.json') -Encoding utf8

$passCount = ($results | Where-Object { $_.Passed }).Count
$totalCount = $results.Count
$reportLines = @("# Cloud smoke test - $runId", '', "$passCount / $totalCount checks passed.", '', '| Check | URL | Status | Passed |', '|---|---|---|---|')
foreach ($result in $results) {
    $reportLines += "| $($result.Check) | $($result.Url) | $($result.Status) | $($result.Passed) |"
}
$reportLines -join "`n" | Out-File (Join-Path $evidenceDir 'report.md') -Encoding utf8

Write-Host "`n$passCount / $totalCount checks passed. Evidence written to $evidenceDir" -ForegroundColor $(if ($passCount -eq $totalCount) { 'Green' } else { 'Red' })

if ($passCount -ne $totalCount) {
    exit 1
}
