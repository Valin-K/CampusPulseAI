<#
.SYNOPSIS
    Creates (or confirms) the Azure resource group for the CampusPulseAI cloud deployment.
    Idempotent - safe to run again; it won't recreate or reset anything that already exists.

.PARAMETER ResourceGroupName
    Defaults to 'rg-campuspulse'.

.PARAMETER Location
    Azure region. Must be one allowed by the subscription - check with:
    az policy assignment list --query "[].{name:displayName, params:parameters}" -o json

.EXAMPLE
    ./Deploy-Infrastructure.ps1 -Location australiaeast
#>
param(
    [string]$ResourceGroupName = 'rg-campuspulse',
    [Parameter(Mandatory = $true)]
    [string]$Location
)

$ErrorActionPreference = 'Stop'

Write-Host "Checking Azure CLI login..." -ForegroundColor Cyan
az account show --output none
if ($LASTEXITCODE -ne 0) {
    throw "Not logged in. Run 'az login' first."
}

Write-Host "Registering required resource providers (safe to re-run; no-op if already registered)..." -ForegroundColor Cyan
foreach ($provider in @('Microsoft.App', 'Microsoft.OperationalInsights', 'Microsoft.Storage', 'Microsoft.ContainerRegistry')) {
    az provider register --namespace $provider --wait
}

Write-Host "Creating resource group '$ResourceGroupName' in '$Location' (no-op if it already exists)..." -ForegroundColor Cyan
az group create --name $ResourceGroupName --location $Location --output none

Write-Host "Done. Resource group '$ResourceGroupName' is ready." -ForegroundColor Green
