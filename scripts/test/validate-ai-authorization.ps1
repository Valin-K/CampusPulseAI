<#
.SYNOPSIS
	Checks authenticated AI data paths in an isolated, running, AI-provider-keyless Compose stack.
	Adds test RAG documents and workflows; temporarily disables and restores scholarships.
#>
param(
	[Parameter(Mandatory = $true)]
	[switch]$IsolatedTestStack
)

$ErrorActionPreference = 'Stop'
if (-not $IsolatedTestStack) { throw 'Run this only against an isolated test stack.' }
. "$PSScriptRoot/ai-authorization-helpers.ps1"

function Assert-Rejected([string]$Uri, [hashtable]$Headers, [int]$ExpectedStatus,
	[string]$Body = '{"question":"Test","task":"Test"}') {
	try {
		Invoke-WebRequest $Uri -Method Post -Headers $Headers -ContentType 'application/json' `
			-Body $Body -UseBasicParsing -TimeoutSec 30 | Out-Null
	}
	catch {
		if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq $ExpectedStatus) { return }
		throw
	}
	throw "Expected HTTP $ExpectedStatus from $Uri."
}

$headers = New-CampusPulseTestHeaders $true
$unscopedHeaders = New-CampusPulseTestHeaders $false
$staffHeaders = New-CampusPulseTestHeaders -Role 'Staff'
$gateway = Invoke-RestMethod 'http://localhost:5269/api/ai/status' -Headers $headers -TimeoutSec 10
if ($gateway.generationAvailable -ne $false) { throw 'This test requires a keyless gateway.' }
$features = @(
	@{ Name = 'academicrecords'; Url = 'http://localhost:5020'; Tool = 'calculate_student_academic_statistics'; Arguments = @{ student_id = 'STU004' } }
	@{ Name = 'coursemanagement'; Url = 'http://localhost:5118'; Tool = 'course.list_courses'; Arguments = @{} }
	@{ Name = 'campushousing'; Url = 'http://localhost:5083'; Tool = 'room_status'; Arguments = @{} }
	@{ Name = 'financialaidbilling'; Url = 'http://localhost:8089'; Tool = 'get_feature_capabilities'; Arguments = @{ feature = 'student-4' } }
	@{ Name = 'eventscheduling'; Url = 'http://localhost:8090'; Tool = 'get_feature_capabilities'; Arguments = @{ feature = 'student-5' } }
)

foreach ($feature in $features) {
	$healthy = $false
	for ($attempt = 0; $attempt -lt 30; $attempt++) {
		try {
			$health = Invoke-RestMethod "$($feature.Url)/health" -TimeoutSec 5
			if ($health.status -eq 'healthy') { $healthy = $true; break }
		}
		catch { Write-Host "Waiting for $($feature.Name): $($_.Exception.Message)" }
		Start-Sleep -Seconds 2
	}
	if (-not $healthy) { throw "$($feature.Name) did not become healthy." }

	Assert-Rejected "$($feature.Url)/api/ai/knowledge" @{} 401
	Assert-Rejected "$($feature.Url)/api/ai/knowledge" $unscopedHeaders 403
	Assert-Rejected "$($feature.Url)/api/multi-agent/workflows" @{} 401
	Assert-Rejected "$($feature.Url)/api/multi-agent/workflows" $unscopedHeaders 403

	$tool = Invoke-RestMethod 'http://localhost:5268/api/mcp/tools/call' -Headers $headers `
		-Method Post -ContentType 'application/json' -TimeoutSec 30 `
		-Body (@{ tool = $feature.Tool; arguments = $feature.Arguments } | ConvertTo-Json -Depth 5)
	if ($tool.isError -ne $false -or $tool.tool -ne $feature.Tool -or $null -eq $tool.result) {
		throw "$($feature.Name) MCP call failed (tool='$($tool.tool)', isError='$($tool.isError)', resultPresent=$($null -ne $tool.result))."
	}

	$documentId = "authorization-test-$([guid]::NewGuid())"
	$task = "Authorization connectivity evidence for $($feature.Name) $documentId"
	$ingest = Invoke-RestMethod "http://localhost:5267/api/rag/$($feature.Name)/documents" `
		-Headers $headers -Method Post -ContentType 'application/json' -TimeoutSec 30 `
		-Body (@{ documents = @(@{ id = $documentId; text = $task }) } | ConvertTo-Json -Depth 5)
	if ($ingest.documents -ne 1) { throw "$($feature.Name) RAG ingestion failed." }

	$workflow = Invoke-RestMethod "$($feature.Url)/api/multi-agent/workflows" -Headers $headers `
		-Method Post -ContentType 'application/json' -TimeoutSec 30 `
		-Body (@{ task = $task; useMcp = $true; useRag = $true; allowFallback = $false } | ConvertTo-Json)
	$uri = "$($feature.Url)/api/multi-agent/workflows/$($workflow.id)"
	for ($attempt = 0; $attempt -lt 30; $attempt++) {
		$workflow = Invoke-RestMethod $uri -Headers $headers -TimeoutSec 10
		if ($workflow.status -ne 'running') { break }
		Start-Sleep -Seconds 1
	}
	foreach ($dependency in @('mcp', 'rag')) {
		$result = @($workflow.dependencies | Where-Object { $_.service -eq $dependency })
		if ($result.Count -ne 1 -or $result[0].status -ne 'available') {
			throw "$($feature.Name) queued $dependency evidence collection failed: $($workflow.error)"
		}
	}
	if (@($workflow.evidence | Where-Object { $_.source -like "RAG $documentId chunk*" }).Count -ne 1) {
		throw "$($feature.Name) did not retrieve its exact RAG document."
	}
	if ($workflow.status -ne 'failed' -or $workflow.error -ne 'The AI gateway returned HTTP 503.') {
		throw "$($feature.Name) did not surface the expected missing-provider-key failure."
	}
	$export = Invoke-WebRequest "$uri/evidence" -Headers $headers -UseBasicParsing -TimeoutSec 10
	if ($export.Content.Contains($headers.Authorization.Substring(7))) { throw 'A workflow export contains credentials.' }
	if ($feature.Name -eq 'financialaidbilling') {
		$knowledge = Invoke-RestMethod "$($feature.Url)/api/ai/knowledge" -Headers $headers `
			-Method Post -ContentType 'application/json' -Body '{"question":"Scholarships"}' -TimeoutSec 30
		if ($knowledge.status -notin @('generation_unavailable', 'insufficient_context') `
			-or $knowledge.confidenceCategory -ne 'Insufficient') {
			throw 'Financial Aid knowledge did not report its keyless retrieval state.'
		}
	}
	else {
		Assert-Rejected "$($feature.Url)/api/ai/knowledge" $headers 503
	}
	Write-Host "PASS $($feature.Name): protected AI endpoints, MCP, RAG, live catalogue, queued authorization, and explicit keyless failure."
}

$analysis = Invoke-RestMethod 'http://localhost:5020/api/students/STU004/ai-analysis' `
	-Headers $headers -Method Post -TimeoutSec 30
if ($analysis.succeeded -ne $false -or $analysis.status -ne 'Failed' `
	-or $analysis.message -ne 'The AI service is unavailable.' -or $analysis.metrics.requiredCredits -ne 144) {
	throw 'Academic progress did not preserve live metrics and report unavailable generation.'
}
Assert-Rejected 'http://localhost:8090/api/ai/assistant' $headers 503 `
	'{"userId":4,"messages":[{"role":"user","content":"Find a study room"}]}'
Write-Host 'PASS academic progress data and booking-assistant keyless failure.'

$billingUrl = 'http://localhost:8089'
$scholarships = Invoke-RestMethod "$billingUrl/api/scholarships" -Headers $headers -TimeoutSec 10
if (@($scholarships).Count -eq 0) { throw 'Expected seeded scholarships in the isolated stack.' }
try {
	foreach ($award in $scholarships) {
		$disabled = $award | Select-Object *
		$disabled.isActive = $false
		Invoke-RestMethod "$billingUrl/api/scholarships/$($award.id)" -Headers $staffHeaders `
			-Method Put -ContentType 'application/json' -Body ($disabled | ConvertTo-Json) -TimeoutSec 10 | Out-Null
	}
	$recommendation = Invoke-RestMethod "$billingUrl/api/scholarships/recommendations/student/4" `
		-Headers $headers -Method Post -TimeoutSec 30
	if ($recommendation.studentId -ne 4 -or $recommendation.matchCount -ne 0 `
		-or $recommendation.academicContext.studentId -ne 'STU004' `
		-or $recommendation.academicContext.tool -ne 'calculate_student_academic_statistics') {
		throw 'Scholarship profile or authenticated MCP statistics lookup failed.'
	}
	Write-Host 'PASS scholarship profile and MCP authorization with a real no-match response.'
}
finally {
	foreach ($award in $scholarships) {
		Invoke-RestMethod "$billingUrl/api/scholarships/$($award.id)" -Headers $staffHeaders `
			-Method Put -ContentType 'application/json' -Body ($award | ConvertTo-Json) -TimeoutSec 10 | Out-Null
	}
}

$answer = Invoke-RestMethod "$billingUrl/api/assistant/rag/scholarships" -Headers $headers `
	-Method Post -ContentType 'application/json' -Body '{"question":"Scholarships"}' -TimeoutSec 30
if ($answer.collection -ne 'financialaidbilling' -or $answer.status -notin @('generation_unavailable', 'insufficient_context')) {
	throw 'Scholarship RAG did not return an explicit keyless retrieval response.'
}
Write-Host 'PASS scholarship RAG corpus ingestion and keyless retrieval.'