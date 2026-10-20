<#
.SYNOPSIS
	Validates an isolated, running, keyless Compose stack. Recreates Multi-Agent
	to check persistence and adds a uniquely identified RAG document and workflow.
#>
param(
	[Parameter(Mandatory = $true)]
	[string]$ComposeProjectName
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ai-authorization-helpers.ps1"
$headers = New-CampusPulseTestHeaders
$task = 'Container connectivity verification evidence'
$documentId = "container-smoke-$([guid]::NewGuid())"

foreach ($port in @(5267, 5268, 5270)) {
	$healthy = $false
	for ($attempt = 0; $attempt -lt 30; $attempt++) {
		try {
			$health = Invoke-RestMethod "http://localhost:$port/health" -TimeoutSec 5
			if ($health.status -eq 'healthy') { $healthy = $true; break }
		}
		catch {
			Write-Host "Waiting for AI port ${port}: $($_.Exception.Message)"
		}
		Start-Sleep -Seconds 2
	}
	if (-not $healthy) { throw "AI service on port $port did not become healthy." }
}

$gateway = Invoke-RestMethod 'http://localhost:5269/api/ai/status' -Headers $headers -TimeoutSec 10
if ($gateway.generationAvailable -ne $false) {
	throw 'This deterministic smoke test requires a keyless gateway; unset GEMINI_API_KEY in the validation stack.'
}

$ingest = Invoke-RestMethod 'http://localhost:5267/api/rag/eventscheduling/documents' `
	-Headers $headers -Method Post -ContentType 'application/json' -TimeoutSec 30 `
	-Body (@{ documents = @(@{ id = $documentId; text = $task }) } | ConvertTo-Json -Depth 5)
if ($ingest.documents -ne 1 -or $ingest.chunks -ne 1) {
	throw 'RAG did not ingest the smoke-test document through the containerised gateway.'
}

$request = @{
	feature = 'eventscheduling'
	task = $task
	evidence = @(@{
		id = 'container-baseline'
		source = 'Container smoke test'
		content = $task
		collectedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
	})
	useMcp = $true
	useRag = $true
	allowFallback = $false
} | ConvertTo-Json -Depth 5
$workflow = Invoke-RestMethod 'http://localhost:5270/api/workflows/' `
	-Headers $headers -Method Post -ContentType 'application/json' -Body $request -TimeoutSec 30
$uri = "http://localhost:5270/api/workflows/$($workflow.id)?feature=eventscheduling"

for ($attempt = 0; $attempt -lt 30; $attempt++) {
	$workflow = Invoke-RestMethod $uri -Headers $headers -TimeoutSec 10
	if ($workflow.status -ne 'running') { break }
	Start-Sleep -Seconds 2
}

foreach ($service in @('mcp', 'rag')) {
	$dependency = @($workflow.dependencies | Where-Object { $_.service -eq $service })
	if ($dependency.Count -ne 1 -or $dependency[0].status -ne 'available') {
		throw "Multi-Agent could not collect $service evidence over container DNS: $($workflow | ConvertTo-Json -Depth 8)"
	}
}
if (@($workflow.evidence | Where-Object { $_.source -like "RAG $documentId chunk*" }).Count -ne 1) {
	throw 'Multi-Agent did not retrieve the exact smoke-test RAG document.'
}
if ($workflow.status -ne 'failed' -or $workflow.error -ne 'The AI gateway returned HTTP 503.') {
	throw 'The keyless gateway must produce an explicit workflow failure, not a fabricated success.'
}

docker compose -p $ComposeProjectName up --detach --no-deps --force-recreate multiagent.server
if ($LASTEXITCODE -ne 0) { throw 'Failed to recreate the Multi-Agent container.' }

$restored = $null
for ($attempt = 0; $attempt -lt 30; $attempt++) {
	try {
		$restored = Invoke-RestMethod $uri -Headers $headers -TimeoutSec 5
		break
	}
	catch {
		Write-Host "Waiting for persisted workflow: $($_.Exception.Message)"
		Start-Sleep -Seconds 2
	}
}
if ($null -eq $restored -or $restored.id -ne $workflow.id -or $restored.version -ne $workflow.version `
	-or $restored.status -ne $workflow.status -or $restored.error -ne $workflow.error) {
	throw 'Workflow state was not preserved when the container was recreated.'
}

Write-Host 'AI container health, gateway connectivity, MCP/RAG evidence, explicit keyless failure, and workflow persistence passed.'