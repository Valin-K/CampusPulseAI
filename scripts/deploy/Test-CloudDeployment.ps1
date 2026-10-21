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

    It also does not assert "AI is disabled" as a single HTTP check across all services: Financial
    Aid Billing's endpoints return an explicit 403 when disabled (checked below), but the other
    4 services' chat orchestrators degrade by silently not offering the RAG/MCP route rather than
    returning an error code - that's correct, graceful behaviour, but not something a single
    automated assertion can observe. Confirm those via the UI screenshots in the report instead.
#>
param(
    [string]$ResourceGroupName = 'rg-campuspulse',
    [switch]$EnableAi
)

$ErrorActionPreference = 'Stop'

# One known, real GET endpoint per frontend - the exact ones manually verified in Phase 1.
$services = @(
    @{ App = 'academicrecords-web'; Path = '/api/users' }
    @{ App = 'coursemgmt-web'; Path = '/api/course-access/users' }
    @{ App = 'campushousing-web'; Path = '/api/housing/rooms' }
    @{ App = 'finaid-web'; Path = '/api/invoices' }
    @{ App = 'eventscheduling-web'; Path = '/api/rooms' }
)

function Get-AppFqdn([string]$AppName) {
    $fqdn = az containerapp show --resource-group $ResourceGroupName --name $AppName --query 'properties.configuration.ingress.fqdn' --output tsv
    if ([string]::IsNullOrWhiteSpace($fqdn)) {
        throw "Could not resolve the FQDN for '$AppName' - is it deployed?"
    }
    return $fqdn
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

foreach ($service in $services) {
    $fqdn = Get-AppFqdn -AppName $service.App
    $url = "https://$fqdn$($service.Path)"
    Write-Host "Checking $($service.App)$($service.Path)..." -ForegroundColor Cyan
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 60
        $results += [pscustomobject]@{ Check = "$($service.App) CRUD read (web -> api -> db)"; Url = $url; Status = $response.StatusCode; Passed = ($response.StatusCode -eq 200) }
    }
    catch {
        $statusCode = 'error'
        if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
        $results += [pscustomobject]@{ Check = "$($service.App) CRUD read (web -> api -> db)"; Url = $url; Status = $statusCode; Passed = $false }
    }
}

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
        $response = Invoke-WebRequest -Uri $url -Method Post -Body '{}' -ContentType 'application/json' -UseBasicParsing -TimeoutSec 60
        $passed = $response.StatusCode -eq $check.ExpectedStatus
        if ($EnableAi) { $passed = $passed -and (($response.Content | ConvertFrom-Json).isError -eq $false) }
        $results += [pscustomobject]@{ Check = "AI profile: $($check.Path) returns $($check.ExpectedStatus)"; Url = $url; Status = $response.StatusCode; Passed = $passed }
    }
    catch {
        $statusCode = 'error'
        if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
        $results += [pscustomobject]@{ Check = "AI profile: $($check.Path) returns $($check.ExpectedStatus)"; Url = $url; Status = $statusCode; Passed = ($statusCode -eq $check.ExpectedStatus) }
    }
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
