[CmdletBinding()]
param(
    [ValidateSet("implementation", "database", "architecture", "devops")]
    [string]$Focus = "implementation",

    [string]$Goal = "Improve the Student 2 Course Management microservices.",

    [string]$ContextPath =
        "docs\release-0\agentic-loop-context\student-2-eligibility-context.txt",

    [string]$ApprovalText = "",

    [string]$ApprovedPatchPath,

    [switch]$ApplyApprovedPatch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$GeminiModel = "gemini-3-flash-preview"
$QwenModel = "qwen3.8:27b"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$evidenceDirectory = Join-Path $repositoryRoot "docs\release-0\agentic-loop-evidence"
$runId = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ")
$evidencePath = Join-Path $evidenceDirectory "student-2-$runId.json"

function Get-GeminiApiKey {
    $apiKey = $env:GEMINI_API_KEY
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        $apiKey = [Environment]::GetEnvironmentVariable(
            "GEMINI_API_KEY",
            "User")
    }

    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        throw "Set GEMINI_API_KEY before running the agentic loop."
    }

    $apiKey
}

function Invoke-Gemini {
    param([string]$Prompt)

    $body = @{
        contents = @(
            @{
                parts = @(
                    @{ text = $Prompt }
                )
            }
        )
        generationConfig = @{
            temperature = 0.2
            maxOutputTokens = 2500
        }
    } | ConvertTo-Json -Depth 8

    $response = Invoke-RestMethod `
        -Uri "https://generativelanguage.googleapis.com/v1beta/models/$GeminiModel`:generateContent" `
        -Method Post `
        -Headers @{ "x-goog-api-key" = (Get-GeminiApiKey) } `
        -ContentType "application/json" `
        -Body $body `
        -TimeoutSec 180

    $text = $response.candidates[0].content.parts[0].text
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Gemini returned an empty response."
    }

    $text.Trim()
}

function Invoke-Qwen {
    param([string]$Prompt)

    $body = @{
        model = $QwenModel
        prompt = $Prompt
        stream = $false
        think = $false
        keep_alive = "30m"
        options = @{
            num_ctx = 8192
            temperature = 0.1
            num_predict = 800
        }
    } | ConvertTo-Json -Depth 8 -Compress

    $response = Invoke-RestMethod `
        -Uri "http://localhost:11434/api/generate" `
        -Method Post `
        -ContentType "application/json" `
        -Body $body `
        -TimeoutSec 1800

    if ([string]::IsNullOrWhiteSpace($response.response)) {
        throw "Qwen returned an empty response."
    }

    $response.response.Trim()
}

function Invoke-CommandCheck {
    param(
        [string]$Name,
        [string]$Command,
        [string[]]$Arguments
    )

    Write-Host "  $Name"
    $startedAt = Get-Date
    $output = @(& $Command @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }

    [ordered]@{
        name = $Name
        passed = $exitCode -eq 0
        exitCode = $exitCode
        durationSeconds = [Math]::Round(
            ((Get-Date) - $startedAt).TotalSeconds,
            2)
        output = ($output -join [Environment]::NewLine)
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
        output = if ($passed) {
            "Frontend manifest and WebAssembly output exist."
        }
        else {
            "Frontend output is missing."
        }
    }
}

function Invoke-AllChecks {
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

function Get-CheckSummary {
    param([object[]]$Checks)

    ($Checks | ForEach-Object {
        "$($_.name): $(if ($_.passed) { "PASS" } else { "FAIL" })"
    }) -join [Environment]::NewLine
}

function Get-ApprovedPatchTargets {
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
    if ($targets.Count -eq 0) {
        throw "The approved patch does not contain a changed file."
    }

    foreach ($target in $targets) {
        $allowed = $target.StartsWith(
            "student-2/",
            [StringComparison]::Ordinal) -or
            $target -eq ".github/workflows/student-2-ci.yml"
        if (!$allowed) {
            throw "Approved patches may change only Student 2 or student-2-ci.yml."
        }
    }

    $targets
}

$originalLocation = Get-Location
$planPrompt = $null
$planOutput = $null
$actPrompt = $null
$actOutput = $null
$observePrompt = $null
$observeOutput = $null
$checksBefore = @()
$checksAfter = @()
$context = $null
$commitBefore = $null
$commitAfter = $null
$runError = $null
$adaptation = [ordered]@{
    humanApproval = $ApprovalText
    patchApproved = $ApplyApprovedPatch.IsPresent
    patchApplied = $false
    patchPath = $ApprovedPatchPath
    targetFiles = @()
}

try {
    Set-Location $repositoryRoot
    $resolvedContextPath = (Resolve-Path $ContextPath).Path
    $context = Get-Content $resolvedContextPath -Raw
    $commitBefore = (& git rev-parse HEAD).Trim()

    Write-Host "PLAN - Gemini 3 Flash" -ForegroundColor Yellow
    $planPrompt = @"
You are the planning agent for Student 2 Course Management.

Goal: $Goal
Focus: $Focus

Approved Student 2 context:
$context

Create a small implementation plan that a beginner can follow.
Use only the approved context above.
Do not add student-status, financial-hold, authentication, waiver, or other
business rules unless the approved context explicitly includes them.
Do not inspect or invent source code.

Reply using exactly:
User journey:
Business rule:
Implementation steps:
Acceptance criteria:
Risks:
"@
    $planOutput = Invoke-Gemini -Prompt $planPrompt
    Write-Host $planOutput

    Write-Host "ACT - Qwen3.8-27B" -ForegroundColor Yellow
    $actPrompt = @"
You are the local implementation agent for Student 2 Course Management.

Goal:
$Goal

Gemini plan:
$planOutput

Local context:
$context

The local context overrides any conflicting statement in the Gemini plan.
Do not introduce business rules or dependencies that are absent from it.
Use the exact filenames listed in the context.
Review the existing implementation and propose only a remaining change that is
necessary. If it already meets the goal, say that no code adaptation is needed.
Do not apply files and do not claim tests ran.

Reply using exactly:
Plan interpretation:
File-by-file changes:
Tests:
Risks or questions:
"@
    $actOutput = Invoke-Qwen -Prompt $actPrompt
    Write-Host $actOutput

    Write-Host "OBSERVE - deterministic checks" -ForegroundColor Yellow
    $checksBefore = @(Invoke-AllChecks)

    Write-Host "OBSERVE - Gemini 3 Flash review" -ForegroundColor Yellow
    $observePrompt = @"
You are the independent reviewer.

Goal:
$Goal

Gemini plan:
$planOutput

Qwen proposal:
$actOutput

Approved Student 2 context:
$context

Objective checks:
$(Get-CheckSummary -Checks $checksBefore)

Identify what should be kept, corrected, or rejected.
Reject any suggestion that adds a business rule or dependency not present in
the approved context.
Do not claim that you inspected source code.

Reply using exactly:
Keep:
Correct:
Reject:
Recommended adaptation:
Verification:
"@
    $observeOutput = Invoke-Gemini -Prompt $observePrompt
    Write-Host $observeOutput

    Write-Host "ADAPT - human approval" -ForegroundColor Yellow
    if ($ApplyApprovedPatch) {
        if ([string]::IsNullOrWhiteSpace($ApprovalText)) {
            throw "Provide -ApprovalText when applying an approved patch."
        }
        if ([string]::IsNullOrWhiteSpace($ApprovedPatchPath)) {
            throw "Provide -ApprovedPatchPath with -ApplyApprovedPatch."
        }

        $patchPath = (Resolve-Path $ApprovedPatchPath).Path
        $adaptation.patchPath = $patchPath
        $adaptation.targetFiles = @(
            Get-ApprovedPatchTargets -PatchPath $patchPath)

        & git apply --check --whitespace=error-all $patchPath
        if ($LASTEXITCODE -ne 0) {
            throw "The approved patch failed validation."
        }

        & git apply --whitespace=error-all $patchPath
        if ($LASTEXITCODE -ne 0) {
            throw "The approved patch could not be applied."
        }

        $adaptation.patchApplied = $true

        Write-Host "VERIFY - repeat deterministic checks" -ForegroundColor Yellow
        $checksAfter = @(Invoke-AllChecks)
        if ($checksAfter.Where({ !$_.passed }).Count -gt 0) {
            throw "The patch was applied, but a verification check failed."
        }
    }
    else {
        Write-Host "  No patch applied. Human approval is still required."
    }

    $commitAfter = (& git rev-parse HEAD).Trim()
}
catch {
    $runError = $_.Exception.Message
    Write-Host "Agentic loop failed: $runError" -ForegroundColor Red
}
finally {
    Set-Location $repositoryRoot
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null

    [ordered]@{
        schemaVersion = 3
        assessmentEvidence = $true
        workflow = "Plan -> Act -> Observe -> Adapt -> Verify"
        runId = $runId
        recordedAtUtc = [DateTime]::UtcNow.ToString("O")
        goal = $Goal
        focus = $Focus
        models = @(
            [ordered]@{
                stage = "Plan and Observe"
                provider = "Google Gemini API"
                name = "Gemini 3 Flash"
                exactModel = $GeminiModel
            }
            [ordered]@{
                stage = "Act"
                provider = "Local Ollama"
                name = "Qwen3.8-27B"
                exactModel = $QwenModel
            }
        )
        plan = [ordered]@{
            prompt = $planPrompt
            rawOutput = $planOutput
        }
        act = [ordered]@{
            contextPath = $ContextPath
            localContext = $context
            prompt = $actPrompt
            rawOutput = $actOutput
        }
        observe = [ordered]@{
            checks = $checksBefore
            prompt = $observePrompt
            rawOutput = $observeOutput
        }
        adapt = $adaptation
        verify = [ordered]@{
            checks = $checksAfter
        }
        privacy = [ordered]@{
            sourceCodeSentToGemini = $false
            projectContextSentToGemini = $true
            qwenRunsLocally = $true
            apiKeysRecorded = $false
        }
        git = [ordered]@{
            branch = (& git branch --show-current).Trim()
            commitBefore = $commitBefore
            commitAfter = $commitAfter
        }
        error = $runError
    } |
        ConvertTo-Json -Depth 10 |
        Set-Content -Path $evidencePath -Encoding utf8

    Set-Location $originalLocation
    Write-Host "Evidence: $evidencePath" -ForegroundColor Green
}

if ($null -ne $runError) {
    exit 1
}

if ($checksBefore.Where({ !$_.passed }).Count -gt 0) {
    exit 1
}
