<#
.SYNOPSIS
    Deploys (or updates) the 16 baseline apps and, with -EnableAi, four AI apps. Re-running
    with a new -Tag updates the existing apps to that image; it never creates duplicates.

.PARAMETER GitHubOwner
    GHCR namespace the images were pushed to, e.g. 'valin-k' for ghcr.io/valin-k/campuspulse-*.

.PARAMETER Tag
    The git SHA (or other unique tag) to deploy. Never pass 'latest' - see cloud-deployment-plan.md
    for why Container Apps needs a changing tag to actually pick up a new image.

.PARAMETER EnableAi
    Switch. Omit for the required baseline (AI disabled). Pass it only for the bonus AI profile.
    Reads GEMINI-API-KEY from Key Vault and stores it as an AI gateway secret.

.PARAMETER KeyVaultName
    Key Vault holding CAMPUSPULSE-SIGNING-KEY and, with -EnableAi, GEMINI-API-KEY.
    The deploying identity needs the Key Vault Secrets User role on this vault.

.PARAMETER WarmUp
    Switch. Sets minReplicas to 1 on every app instead of 0, so there's no cold-start delay.
    Use before the showcase/marking window; redeploy without it afterwards.

.EXAMPLE
    ./Deploy-Apps.ps1 -ResourceGroupName rg-campuspulse -GitHubOwner valin-k -Tag abc1234

.EXAMPLE
    ./Deploy-Apps.ps1 -ResourceGroupName rg-campuspulse -GitHubOwner valin-k -Tag abc1234 -WarmUp
#>
param(
    [string]$ResourceGroupName = 'rg-campuspulse',
    [Parameter(Mandatory = $true)]
    [string]$GitHubOwner,
    [Parameter(Mandatory = $true)]
    [string]$Tag,
    [switch]$EnableAi,
    [switch]$WarmUp,
    [string]$KeyVaultName = 'kv-campuspulse'
)

$ErrorActionPreference = 'Stop'
$bicepFile = Join-Path $PSScriptRoot 'infra/main.bicep'
$minReplicas = 0
if ($WarmUp) { $minReplicas = 1 }

function Get-KeyVaultSecret([string]$SecretName) {
    $value = az keyvault secret show --vault-name $KeyVaultName --name $SecretName --query value --output tsv
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
        throw "Could not read secret '$SecretName' from Key Vault '$KeyVaultName'. Confirm it exists and the deploying identity has the Key Vault Secrets User role."
    }
    $value = ($value -join "`n").Trim()
    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Host "::add-mask::$value"
    }
    return $value
}

$geminiApiKey = ''
if ($EnableAi) {
    $geminiApiKey = Get-KeyVaultSecret 'GEMINI-API-KEY'
}

$campusPulseSigningKey = Get-KeyVaultSecret 'CAMPUSPULSE-SIGNING-KEY'
if ([Text.Encoding]::UTF8.GetByteCount($campusPulseSigningKey) -lt 32) {
    throw 'Key Vault secret CAMPUSPULSE-SIGNING-KEY must contain at least 32 bytes.'
}

Write-Host "Deploying image tag '$Tag' to resource group '$ResourceGroupName' (AI enabled: $($EnableAi.IsPresent), minReplicas: $minReplicas)..." -ForegroundColor Cyan

$databaseAppNames = @('academicrecords-db', 'coursemgmt-db', 'campushousing-db', 'finaid-db', 'eventscheduling-db')
if ($EnableAi) { $databaseAppNames += 'multi-agent-server' }
$existingApps = az containerapp list --resource-group $ResourceGroupName --query '[].name' --output json
if ($LASTEXITCODE -ne 0) { throw 'Could not list existing apps; no database revisions have been stopped.' }
$existingAppNames = @($existingApps | ConvertFrom-Json)
$stoppedRevisions = @()
foreach ($appName in $databaseAppNames) {
    if ($appName -notin $existingAppNames) { continue }
    $revisions = az containerapp revision list --resource-group $ResourceGroupName --name $appName --query '[?properties.active].name' --output json
    if ($LASTEXITCODE -ne 0) { throw "Could not list revisions for '$appName'." }
    foreach ($revision in @($revisions | ConvertFrom-Json)) {
        Write-Host "Stopping SQLite revision '$revision' before replacement (brief database downtime)..."
        az containerapp revision deactivate --resource-group $ResourceGroupName --name $appName --revision $revision --output none
        if ($LASTEXITCODE -ne 0) { throw "Could not deactivate '$revision'; deployment stopped to avoid competing SQLite writers." }
        $stoppedRevisions += @{ App = $appName; Revision = $revision }
    }
}

$stopDeadline = (Get-Date).AddMinutes(5)
foreach ($stopped in $stoppedRevisions) {
    do {
        $replicas = az containerapp replica list --resource-group $ResourceGroupName --name $stopped.App --revision $stopped.Revision --query '[].name' --output json
        if ($LASTEXITCODE -ne 0) { throw "Could not confirm '$($stopped.Revision)' stopped; deployment aborted." }
        if (@($replicas | ConvertFrom-Json).Count -eq 0) { break }
        if ((Get-Date) -ge $stopDeadline) { throw "Timed out waiting for '$($stopped.Revision)' to stop; deployment aborted to protect SQLite." }
        Start-Sleep -Seconds 5
    } while ($true)
}

$deployment = az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file $bicepFile `
    --parameters githubOwner=$GitHubOwner imageTag=$Tag enableAi=$($EnableAi.IsPresent) minReplicas=$minReplicas geminiApiKey=$geminiApiKey campusPulseSigningKey=$campusPulseSigningKey `
    --output json | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) {
    throw "Deployment failed. Database revisions were stopped and may need reactivation. Check the Azure CLI output above and the recovery steps in cloud-deployment-plan.md."
}

foreach ($appName in $databaseAppNames) {
    $latestRevision = az containerapp show --resource-group $ResourceGroupName --name $appName --query 'properties.latestRevisionName' --output tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($latestRevision)) {
        throw "Could not find the latest revision for '$appName'."
    }
    az containerapp revision activate --resource-group $ResourceGroupName --name $appName --revision $latestRevision --output none
    if ($LASTEXITCODE -ne 0) { throw "Could not activate '$latestRevision'." }
}

$portalUrl = $deployment.properties.outputs.portalUrl.value
$frontendUrls = $deployment.properties.outputs.frontendUrls.value

Write-Host "`nDeployment succeeded." -ForegroundColor Green
Write-Host "Portal:    $portalUrl"
Write-Host "Frontends:"
$frontendUrls | ForEach-Object { Write-Host "  - $_" }
