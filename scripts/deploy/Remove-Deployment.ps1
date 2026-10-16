<#
.SYNOPSIS
    Deletes the entire CampusPulseAI cloud resource group - every app, the environment, storage
    and logs. Irreversible. Run this after marking/the showcase to stop any further cost.

.PARAMETER ResourceGroupName
    Defaults to 'rg-campuspulse'.

.PARAMETER Confirm
    Must be passed explicitly (-Confirm) to actually delete anything; otherwise this only prints
    what would be deleted.
#>
param(
    [string]$ResourceGroupName = 'rg-campuspulse',
    [switch]$Confirm
)

$ErrorActionPreference = 'Stop'

$exists = az group exists --name $ResourceGroupName
if ($exists -ne 'true') {
    Write-Host "Resource group '$ResourceGroupName' does not exist - nothing to delete." -ForegroundColor Yellow
    return
}

if (-not $Confirm) {
    Write-Host "This would permanently delete resource group '$ResourceGroupName' and everything in it." -ForegroundColor Yellow
    Write-Host "Re-run with -Confirm to actually delete it." -ForegroundColor Yellow
    return
}

Write-Host "Deleting resource group '$ResourceGroupName'..." -ForegroundColor Cyan
az group delete --name $ResourceGroupName --yes --no-wait

Write-Host "Deletion started (runs in the background on Azure's side). Check progress with:" -ForegroundColor Green
Write-Host "  az group show --name $ResourceGroupName"
