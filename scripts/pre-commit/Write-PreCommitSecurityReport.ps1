[CmdletBinding()]
param(
	[string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
	$OutputDirectory = Join-Path $repoRoot 'docs/release-2/testing-evidence'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$commitSha = (& git -C $repoRoot rev-parse HEAD 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commitSha)) { $commitSha = $null }
$startedAtUtc = [DateTime]::UtcNow.ToString('o')
$hookIds = @('detect-secrets', 'dotnet-security-analyzers', 'nuget-vulnerable-packages')
$hooks = @()

foreach ($hookId in $hookIds) {
	$output = ''
	$exitCode = 1
	$errorMessage = $null
	try {
		Push-Location $repoRoot
		try {
			$output = (& pre-commit run $hookId --all-files 2>&1 | Out-String)
			$exitCode = $LASTEXITCODE
		}
		finally {
			Pop-Location
		}
	}
	catch {
		$errorMessage = $_.Exception.Message
		$output = $errorMessage
		$exitCode = 1
	}

	if ($hookId -eq 'detect-secrets') {
		$sanitizedOutput = 'Raw secret-scanner output suppressed to prevent secret disclosure.'
		if ($errorMessage) { $sanitizedOutput = 'Secret-scanner invocation failed; raw error output suppressed.' }
	}
	else {
		$sanitizedOutput = $output -replace '(?i)(password|secret|token|api[_-]?key|connectionstring)(\s*[:=]\s*)("[^"]*"|''[^'']*''|[^\s,;]+)', '$1$2[REDACTED]'
		$sanitizedOutput = $sanitizedOutput -replace '(?i)Bearer\s+[A-Za-z0-9._~+/=-]+', 'Bearer [REDACTED]'
		$sanitizedOutput = $sanitizedOutput -replace '\b(?:gh[pousr]_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9_-]{16,})\b', '[REDACTED]'
		$sanitizedOutput = $sanitizedOutput.Trim()
	}

	$hooks += [pscustomobject]@{
		id = $hookId
		status = $(if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' })
		exitCode = $exitCode
		sanitizedOutput = $sanitizedOutput
	}
}

$completedAtUtc = [DateTime]::UtcNow.ToString('o')
$overallStatus = if (@($hooks | Where-Object { $_.status -ne 'PASS' }).Count -eq 0) { 'PASS' } else { 'FAIL' }
$report = [ordered]@{
	schemaVersion = 1
	mode = 'pre-commit-security'
	status = $overallStatus
	startedAtUtc = $startedAtUtc
	completedAtUtc = $completedAtUtc
	commitSha = $commitSha
	hooks = $hooks
}
$jsonPath = Join-Path $OutputDirectory 'pre-commit-security-report.json'
$markdownPath = Join-Path $OutputDirectory 'pre-commit-security-report.md'
$report | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding utf8
$lines = @(
	'# Pre-Commit Security Report',
	'',
	"- Status: **$overallStatus**",
	"- Started (UTC): ``$startedAtUtc``",
	"- Completed (UTC): ``$completedAtUtc``",
	"- Commit SHA: ``$(if ($commitSha) { $commitSha } else { 'unavailable' })``",
	'',
	'| Hook | Result | Exit code | Sanitized output |',
	'|---|---|---:|---|'
)
foreach ($hook in $hooks) {
	$safeOutput = ($hook.sanitizedOutput -replace '\|', '\|' -replace "`r?`n", '<br>')
	$lines += "| ``$($hook.id)`` | **$($hook.status)** | $($hook.exitCode) | $safeOutput |"
}
$lines -join "`n" | Set-Content -Path $markdownPath -Encoding utf8
Write-Host "Pre-commit security report: $markdownPath"
if ($overallStatus -ne 'PASS') { exit 1 }
