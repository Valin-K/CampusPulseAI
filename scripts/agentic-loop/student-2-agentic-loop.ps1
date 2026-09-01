[CmdletBinding()]
param(
    [ValidateSet("implementation", "database", "architecture", "devops")]
    [string]$Focus = "implementation",

    [string]$Goal = "Improve the Student 2 Course Management microservices.",

    [string]$Model = "gemini-3.5-flash-lite",

    [string]$ApprovedPatchPath,

    [switch]$ApplyApprovedPatch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$evidenceDirectory = Join-Path $repositoryRoot "docs\release-0\agentic-loop-evidence"
$runId = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ")
$evidencePath = Join-Path $evidenceDirectory "student-2-$runId.json"

function Invoke-CommandCheck {
    param(
        [string]$Name,
        [string]$Command,
        [string[]]$Arguments
    )

    Write-Host "  $Name"
    $startedAt = Get-Date
    & $Command @Arguments 2>&1 |
        ForEach-Object { Write-Host $_ }
    $exitCode = $LASTEXITCODE

    [ordered]@{
        name = $Name
        passed = $exitCode -eq 0
        exitCode = $exitCode
        durationSeconds = [Math]::Round(
            ((Get-Date) - $startedAt).TotalSeconds,
            2)
    }
}

function Invoke-FrontendOutputCheck {
    $manifestPath =
        "student-2\frontend\CourseManagement.Frontend\bin\Debug\net10.0\" +
        "CourseManagement.Frontend.staticwebassets.endpoints.json"
    $wasmPath =
        "student-2\frontend\CourseManagement.Frontend\bin\Debug\net10.0\" +
        "wwwroot\_framework\CourseManagement.Frontend.*.wasm"
    $passed = (Test-Path $manifestPath) -and (Test-Path $wasmPath)

    Write-Host "  Student 2 frontend output smoke check"

    [ordered]@{
        name = "Student 2 frontend output smoke check"
        passed = $passed
        exitCode = if ($passed) { 0 } else { 1 }
        durationSeconds = 0
    }
}

function Invoke-AllChecks {
    Write-Host "ACT - Run deterministic checks" -ForegroundColor Yellow

    @(
        Invoke-CommandCheck `
            -Name "Git diff validation" `
            -Command "git" `
            -Arguments @("diff", "--check")
        Invoke-CommandCheck `
            -Name "Student 2 automated tests" `
            -Command "dotnet" `
            -Arguments @(
                "test",
                "student-2\tests\CourseManagement.Tests\CourseManagement.Tests.csproj",
                "--no-restore",
                "--nologo"
            )
        Invoke-CommandCheck `
            -Name "Student 2 frontend build" `
            -Command "dotnet" `
            -Arguments @(
                "build",
                "student-2\frontend\CourseManagement.Frontend\CourseManagement.Frontend.csproj",
                "--no-restore",
                "--nologo"
            )
        Invoke-FrontendOutputCheck
        Invoke-CommandCheck `
            -Name "Docker Compose validation" `
            -Command "docker" `
            -Arguments @("compose", "config", "--quiet")
    )
}

function Get-GeminiReview {
    param([object[]]$Checks)

    $apiKey = $env:GEMINI_API_KEY
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        $apiKey = [Environment]::GetEnvironmentVariable(
            "GEMINI_API_KEY",
            "User")
    }
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        throw "Set GEMINI_API_KEY before running the agentic loop."
    }

    $results = $Checks |
        ForEach-Object {
            "$($_.name): $(if ($_.passed) { "PASS" } else { "FAIL" })"
        }

    $prompt = @"
Review this Student 2 software-development evidence.

Goal: $Goal
Focus: $Focus
Checks:
$($results -join [Environment]::NewLine)

Answer these three questions:
1. What does this evidence prove?
2. What is one small improvement to consider next?
3. Which check should verify the improvement?

Reply using exactly these headings:
Evidence:
Improvement:
Verification:

Use only the check results above. Do not claim that you inspected source code,
logs, databases, or architecture files.
"@

    $body = @{
        contents = @(
            @{
                parts = @(
                    @{ text = $prompt }
                )
            }
        )
    } | ConvertTo-Json -Depth 6

    $escapedModel = [Uri]::EscapeDataString($Model)
    $response = Invoke-RestMethod `
        -Uri "https://generativelanguage.googleapis.com/v1beta/models/$escapedModel`:generateContent" `
        -Method Post `
        -Headers @{ "x-goog-api-key" = $apiKey } `
        -ContentType "application/json" `
        -Body $body `
        -TimeoutSec 120

    $review = $response.candidates[0].content.parts[0].text
    if ([string]::IsNullOrWhiteSpace($review)) {
        throw "Gemini returned an empty review."
    }

    $review.Trim()
}

function Get-ApprovedPatchTarget {
    param([string]$PatchPath)

    $patchText = Get-Content $PatchPath -Raw
    if ($patchText.Contains("GIT binary patch")) {
        throw "Binary patches are not allowed."
    }

    $targets = @(
        [regex]::Matches($patchText, "(?m)^\+\+\+ b/(.+)$") |
            ForEach-Object { $_.Groups[1].Value.Trim() } |
            Sort-Object -Unique
    )
    if ($targets.Count -ne 1) {
        throw "The approved patch must change exactly one file."
    }

    $target = $targets[0]
    $allowed = $target.StartsWith(
        "student-2/",
        [StringComparison]::Ordinal) -or
        $target -eq ".github/workflows/student-2-ci.yml"
    if (!$allowed) {
        throw "The approved patch must target Student 2 or student-2-ci.yml."
    }

    $target
}

$originalLocation = Get-Location
$checksBefore = @()
$checksAfter = @()
$geminiReview = $null
$adaptation = [ordered]@{
    patchApproved = $ApplyApprovedPatch.IsPresent
    patchApplied = $false
    targetFile = $null
}
$runError = $null

try {
    Set-Location $repositoryRoot

    Write-Host "PLAN" -ForegroundColor Yellow
    Write-Host "  Goal: $Goal"
    Write-Host "  Focus: $Focus"

    $checksBefore = @(Invoke-AllChecks)

    Write-Host "OBSERVE - Ask Gemini three review questions" -ForegroundColor Yellow
    $geminiReview = Get-GeminiReview -Checks $checksBefore
    Write-Host $geminiReview

    Write-Host "ADAPT - Apply one human-approved improvement" -ForegroundColor Yellow
    if ($ApplyApprovedPatch) {
        if ([string]::IsNullOrWhiteSpace($ApprovedPatchPath)) {
            throw "Provide -ApprovedPatchPath with -ApplyApprovedPatch."
        }

        $patchPath = (Resolve-Path $ApprovedPatchPath).Path
        $adaptation.targetFile = Get-ApprovedPatchTarget -PatchPath $patchPath

        & git apply --check --whitespace=error-all $patchPath
        if ($LASTEXITCODE -ne 0) {
            throw "The approved patch failed validation."
        }

        & git apply --whitespace=error-all $patchPath
        if ($LASTEXITCODE -ne 0) {
            throw "The approved patch could not be applied."
        }

        $adaptation.patchApplied = $true
        $checksAfter = @(Invoke-AllChecks)
        if ($checksAfter.Where({ !$_.passed }).Count -gt 0) {
            throw "The improvement was applied, but a check failed."
        }

        Write-Host "  Improvement applied and verified." -ForegroundColor Green
    }
    else {
        Write-Host "  No patch supplied. Human approval is still required."
    }
}
catch {
    $runError = $_.Exception.Message
    Write-Host "Agentic loop failed: $runError" -ForegroundColor Red
}
finally {
    Set-Location $repositoryRoot
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null

    [ordered]@{
        runId = $runId
        recordedAtUtc = [DateTime]::UtcNow.ToString("O")
        plan = [ordered]@{
            goal = $Goal
            focus = $Focus
            model = $Model
        }
        act = [ordered]@{
            checksBefore = $checksBefore
        }
        observe = [ordered]@{
            geminiReview = $geminiReview
        }
        adapt = $adaptation
        verify = [ordered]@{
            checksAfter = $checksAfter
        }
        humanApprovalRequired = $true
        branch = (& git branch --show-current)
        commit = (& git rev-parse HEAD)
        error = $runError
    } |
        ConvertTo-Json -Depth 8 |
        Set-Content -Path $evidencePath -Encoding utf8

    Set-Location $originalLocation
    Write-Host "Evidence: $evidencePath" -ForegroundColor Green
}

if ($null -ne $runError) {
    exit 1
}

if ($checksBefore.Where({ !$_.passed }).Count -gt 0 -and !$ApplyApprovedPatch) {
    exit 1
}
