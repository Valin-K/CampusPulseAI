[CmdletBinding()]
param(
    [ValidateSet('Start', 'List', 'Get', 'Review', 'Export')]
    [string]$Mode = 'Start',
    [string]$BackendUrl = 'http://localhost:8089',
    [string]$SignInUrl = 'http://localhost:5020/api/auth/token',
    [string]$AccountId,
    [ValidateSet('Student', 'Staff')]
    [string]$Role = 'Student',
    [string]$Task,
    [string]$AdditionalEvidence,
    [string]$EvidenceFile,
    [Guid]$WorkflowId,
    [ValidateSet('approve', 'correct', 'reject')]
    [string]$Decision,
    [string]$Reviewer,
    [string]$Comments = '',
    [int]$ExpectedVersion,
    [switch]$NoMcp,
    [switch]$NoRag,
    [switch]$RequireDependencies,
    [switch]$Wait,
    [int]$WaitSeconds = 360,
    [string]$OutputDirectory,
    [switch]$DirectServer,
    [ValidateSet('academicrecords', 'coursemanagement', 'campushousing', 'financialaidbilling', 'eventscheduling')]
    [string]$Feature
)

$ErrorActionPreference = 'Stop'
if ($WaitSeconds -lt 1) { throw 'WaitSeconds must be positive.' }
$base = $BackendUrl.TrimEnd('/')
$api = "$base/api/multi-agent/workflows"
$query = ''
if ($DirectServer) {
    if (-not $Feature) { throw 'DirectServer requires Feature.' }
    $api = "$base/api/workflows"
    $query = "?feature=$Feature"
}

function Get-AccessToken {
    if ([string]::IsNullOrWhiteSpace($AccountId)) {
        $AccountId = Read-Host 'CampusPulse account ID'
    }
    if ([string]::IsNullOrWhiteSpace($AccountId)) { throw 'A CampusPulse account ID is required to sign in.' }

    $password = Read-Host "Password for $AccountId ($Role)" -AsSecureString
    $passwordPointer = [IntPtr]::Zero
    try {
        $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
        $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
        $signInBody = @{
            accountId = $AccountId
            role = $Role
            password = $plainPassword
        } | ConvertTo-Json -Compress
        $signInResponse = Invoke-RestMethod -Uri $SignInUrl -Method Post -ContentType 'application/json; charset=utf-8' `
            -Body ([Text.Encoding]::UTF8.GetBytes($signInBody)) -TimeoutSec 30
        if ([string]::IsNullOrWhiteSpace($signInResponse.accessToken)) {
            throw "Sign-in at $SignInUrl succeeded without returning an access token."
        }
        return [string]$signInResponse.accessToken
    }
    catch {
        $status = $null
        if ($_.Exception.Response -and $null -ne $_.Exception.Response.StatusCode) {
            $status = [int]$_.Exception.Response.StatusCode
        }
        if ($status) { throw "CampusPulse sign-in failed at ${SignInUrl} (HTTP $status). Verify the sign-in service, account, role, and password." }
        throw "CampusPulse sign-in failed at ${SignInUrl}: $($_.Exception.Message)"
    }
    finally {
        if ($passwordPointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
        }
        if ($null -ne $password) { $password.Dispose() }
        $plainPassword = $null
        $signInBody = $null
        $signInResponse = $null
    }
}

function Invoke-WorkflowRequest {
    param([string]$Uri, [string]$Method = 'Get', [object]$Body)
    $arguments = @{
        Uri = $Uri
        Method = $Method
        TimeoutSec = 45
        Headers = @{ Authorization = "Bearer $script:AccessToken" }
    }
    if ($null -ne $Body) {
        $arguments.ContentType = 'application/json; charset=utf-8'
        $arguments.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 50 -Compress))
    }
    try { return Invoke-RestMethod @arguments }
    catch {
        $status = $null
        if ($_.Exception.Response -and $null -ne $_.Exception.Response.StatusCode) {
            $status = [int]$_.Exception.Response.StatusCode
        }
        if ($status) { throw "Multi-agent $Method request failed at ${Uri} (HTTP $status): $($_.Exception.Message)" }
        throw "Multi-agent $Method request failed at ${Uri}: $($_.Exception.Message)"
    }
}

$script:AccessToken = Get-AccessToken

if ($Mode -in @('Get', 'Review', 'Export') -and $WorkflowId -eq [Guid]::Empty) {
    throw "$Mode requires WorkflowId."
}

$result = $null
switch ($Mode) {
    'Start' {
        if ([string]::IsNullOrWhiteSpace($Task)) { throw 'Start requires Task.' }
        if ($EvidenceFile) { $AdditionalEvidence = Get-Content -LiteralPath $EvidenceFile -Raw }
        $body = @{
            task = $Task
            additionalEvidence = $AdditionalEvidence
            useMcp = -not $NoMcp
            useRag = -not $NoRag
            allowFallback = -not $RequireDependencies
        }
        if ($DirectServer) {
            if ([string]::IsNullOrWhiteSpace($AdditionalEvidence)) {
                throw 'Direct server execution requires AdditionalEvidence or EvidenceFile. Use the backend caller to collect live application evidence.'
            }
            $body = @{
                feature = $Feature
                task = $Task
                evidence = @(@{
                    id = 'terminal-evidence'
                    source = 'Terminal supplied evidence (unverified)'
                    content = $AdditionalEvidence
                    collectedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                })
                useMcp = -not $NoMcp
                useRag = -not $NoRag
                allowFallback = -not $RequireDependencies
            }
        }
        $result = Invoke-WorkflowRequest -Uri $api -Method Post -Body $body
    }
    'List' { $result = Invoke-WorkflowRequest -Uri "$api$query" }
    'Get' { $result = Invoke-WorkflowRequest -Uri "$api/$WorkflowId$query" }
    'Review' {
        if (-not $Decision -or [string]::IsNullOrWhiteSpace($Reviewer) -or $ExpectedVersion -lt 1) {
            throw 'Review requires Decision, Reviewer, and the ExpectedVersion of the draft you reviewed.'
        }
        $result = Invoke-WorkflowRequest -Uri "$api/$WorkflowId/review$query" -Method Post -Body @{
            decision = $Decision; reviewer = $Reviewer; comments = $Comments; expectedVersion = $ExpectedVersion
        }
    }
    'Export' {
        $result = Invoke-WorkflowRequest -Uri "$api/$WorkflowId/evidence$query"
        if (-not $OutputDirectory) {
            $OutputDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) ".artifacts\multi-agent\$WorkflowId"
        }
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
        $encoding = New-Object Text.UTF8Encoding($false)
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'workflow.json'), ($result.workflow | ConvertTo-Json -Depth 50), $encoding)
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'workflow_history.jsonl'),
            (($result.workflow | ConvertTo-Json -Depth 50 -Compress) + [Environment]::NewLine), $encoding)
        $auditLines = @($result.coordinationAudit | ForEach-Object { $_ | ConvertTo-Json -Depth 50 -Compress })
        [IO.File]::WriteAllLines((Join-Path $OutputDirectory 'coordination_audit.jsonl'), [string[]]$auditLines, $encoding)
        Write-Host "Evidence exported to $OutputDirectory"
    }
}

if ($Wait -and $Mode -in @('Start', 'Review', 'Get')) {
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ($result.status -eq 'running' -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $result = Invoke-WorkflowRequest -Uri "$api/$($result.id)$query"
    }
    if ($result.status -eq 'running') {
        throw "Workflow $($result.id) is still running after $WaitSeconds seconds. Reload it with -Mode Get; do not blindly create another workflow."
    }
}

$result | ConvertTo-Json -Depth 50
if ($result.status -eq 'failed') { throw "Workflow $($result.id) failed: $($result.error)" }
