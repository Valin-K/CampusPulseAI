[CmdletBinding()]
param(
    [ValidateRange(15, 300)]
    [int] $StartupTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$mcpProject = 'ai-services\mcp-server\McpServer\McpServer.csproj'
$mcpDll = 'ai-services\mcp-server\McpServer\bin\Release\net10.0\McpServer.dll'
$mcpUrl = 'http://localhost:5268'
$script:startedServices = [System.Collections.Generic.List[object]]::new()
$script:validationSucceeded = $false

function Invoke-CheckedCommand {
    param(
        [string] $Name,
        [string] $Executable,
        [string[]] $Arguments
    )

    Write-Host "`n== $Name =="
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Write-Pass {
    param([string] $Name)
    Write-Host "PASS $Name" -ForegroundColor Green
}

function Test-HttpService {
    param(
        [string] $Url,
        [string] $ServiceName = '',
        [string] $ExpectedStatus = ''
    )

    try {
        $response = Invoke-RestMethod -Method Get -Uri $Url -TimeoutSec 3
        if (-not [string]::IsNullOrWhiteSpace($ExpectedStatus) -and $response.status -ne $ExpectedStatus) {
            return $false
        }
        return [string]::IsNullOrWhiteSpace($ServiceName) -or $response.service -eq $ServiceName
    }
    catch {
        return $false
    }
}

function Wait-HttpService {
    param(
        [string] $Url,
        [string] $ServiceName = '',
        [string] $ExpectedStatus = '',
        [int] $TimeoutSeconds = $StartupTimeoutSeconds,
        [System.Diagnostics.Process] $Process
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-HttpService -Url $Url -ServiceName $ServiceName -ExpectedStatus $ExpectedStatus) {
            return
        }
        if ($Process -and $Process.HasExited) {
            throw "Service at $Url exited with code $($Process.ExitCode)."
        }
        Start-Sleep -Milliseconds 500
    }

    throw "Service at $Url did not become healthy within $TimeoutSeconds seconds."
}

function Start-LocalService {
    param(
        [string] $Name,
        [string] $DllPath,
        [string] $ProjectDirectory,
        [string] $Url,
        [hashtable] $Environment = @{}
    )

    $logStem = "campuspulse-$Name-$([guid]::NewGuid().ToString('N'))"
    $stdoutLog = Join-Path ([System.IO.Path]::GetTempPath()) "$logStem.out.log"
    $stderrLog = Join-Path ([System.IO.Path]::GetTempPath()) "$logStem.err.log"
    $previousEnvironment = @{}
    foreach ($key in $Environment.Keys) {
        $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, [string] $Environment[$key], 'Process')
    }

    try {
        $absoluteDllPath = Join-Path $repoRoot $DllPath
        $process = Start-Process -FilePath 'dotnet' `
            -ArgumentList @('"' + $absoluteDllPath + '"', '--urls', $Url) `
            -WorkingDirectory (Join-Path $repoRoot $ProjectDirectory) `
            -WindowStyle Hidden `
            -PassThru `
            -RedirectStandardOutput $stdoutLog `
            -RedirectStandardError $stderrLog
    }
    finally {
        foreach ($key in $previousEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previousEnvironment[$key], 'Process')
        }
    }

    $service = [pscustomobject] @{
        Name = $Name
        Process = $process
        Logs = @($stdoutLog, $stderrLog)
    }
    $script:startedServices.Add($service)
    return $service
}

function Invoke-McpTool {
    param(
        [string] $Name,
        [hashtable] $Arguments = @{},
        [switch] $ExpectError
    )

    $body = @{ tool = $Name; arguments = $Arguments } | ConvertTo-Json -Depth 10
    $response = Invoke-RestMethod -Method Post `
        -Uri "$mcpUrl/api/mcp/tools/call" `
        -ContentType 'application/json' `
        -Body $body `
        -TimeoutSec 30

    if ($response.tool -ne $Name) {
        throw "MCP returned tool '$($response.tool)' for '$Name'."
    }
    if ($ExpectError) {
        if ($response.isError -ne $true) {
            throw "MCP tool '$Name' was expected to reject the supplied arguments."
        }
        return $response
    }
    if ($response.isError -ne $false) {
        throw "MCP tool '$Name' failed: $($response.result | ConvertTo-Json -Depth 8 -Compress)"
    }

    return $response.result
}

function Assert-NonNegativeInteger {
    param(
        [object] $Value,
        [string] $Name
    )

    if (($Value -isnot [int] -and $Value -isnot [long]) -or $Value -lt 0) {
        throw "$Name must return a non-negative integer; received '$Value'."
    }
}

function Stop-StartedServices {
    foreach ($service in $script:startedServices) {
        if (-not $service.Process.HasExited) {
            Stop-Process -Id $service.Process.Id -Force -ErrorAction SilentlyContinue
            [void] $service.Process.WaitForExit(5000)
        }

        if (-not $script:validationSucceeded) {
            Write-Host "`n$($service.Name) logs:"
            foreach ($log in $service.Logs) {
                if (Test-Path $log) {
                    Get-Content $log
                }
            }
        }
        Remove-Item $service.Logs -Force -ErrorAction SilentlyContinue
    }
}

Push-Location $repoRoot
try {
    Invoke-CheckedCommand `
        -Name 'Build MCP server' `
        -Executable 'dotnet' `
        -Arguments @('build', $mcpProject, '--configuration', 'Release', '--nologo')

    $composeServices = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-HttpService -Url 'http://localhost:5021/health')) {
        $composeServices.Add('academicrecords.database')
    }
    if (-not (Test-HttpService -Url 'http://localhost:5020/health' -ExpectedStatus 'healthy')) {
        $composeServices.Add('academicrecords.backend')
    }
    if ($composeServices.Count -gt 0) {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            throw 'Docker is required to start Academic Records dependencies for the MCP data tools.'
        }
        Invoke-CheckedCommand `
            -Name 'Start Academic Records data dependencies' `
            -Executable 'docker' `
            -Arguments (@('compose', 'up', '--detach', '--build', '--no-deps') + $composeServices.ToArray())
    }
    Wait-HttpService -Url 'http://localhost:5021/health' -TimeoutSeconds $StartupTimeoutSeconds
    Wait-HttpService -Url 'http://localhost:5020/health' -ExpectedStatus 'healthy' -TimeoutSeconds $StartupTimeoutSeconds
    Write-Pass 'Academic Records database and backend are ready'

    $mcpProcess = $null
    if (-not (Test-HttpService -Url "$mcpUrl/health" -ServiceName 'mcp')) {
        $mcpService = Start-LocalService `
            -Name 'mcp' `
            -DllPath $mcpDll `
            -ProjectDirectory 'ai-services\mcp-server\McpServer' `
            -Url $mcpUrl `
            -Environment @{ MCP_ENABLED = 'true' }
        $mcpProcess = $mcpService.Process
    }
    else {
        Write-Host 'Reusing healthy MCP server at ' $mcpUrl
    }
    Wait-HttpService -Url "$mcpUrl/health" -ServiceName 'mcp' -TimeoutSeconds $StartupTimeoutSeconds -Process $mcpProcess

    $health = Invoke-RestMethod -Method Get -Uri "$mcpUrl/health" -TimeoutSec 5
    $tools = Invoke-RestMethod -Method Get -Uri "$mcpUrl/api/mcp/tools" -TimeoutSec 10
    $toolNames = @($tools | ForEach-Object { $_.name })
    $requiredTools = @('student_count', 'students_by_subject', 'project_files', 'ci_report')
    $missingTools = @($requiredTools | Where-Object { $_ -notin $toolNames })
    if ($health.toolCount -ne $tools.Count -or $missingTools.Count -gt 0) {
        throw "MCP registry is incomplete. Missing tools: $($missingTools -join ', ')."
    }
    Write-Pass "MCP health and registry ($($tools.Count) tools; all four Lab 07 tools registered)"

    $studentCount = Invoke-McpTool -Name 'student_count'
    Assert-NonNegativeInteger -Value $studentCount.student_count -Name 'student_count'
    Write-Pass "student_count returned $($studentCount.student_count)"

    $subjectCount = Invoke-McpTool -Name 'students_by_subject' -Arguments @{ subject_code = 'CS101' }
    if ($subjectCount.subject_code -ne 'CS101') {
        throw "students_by_subject returned unexpected subject '$($subjectCount.subject_code)'."
    }
    Assert-NonNegativeInteger -Value $subjectCount.student_count -Name 'students_by_subject'
    Write-Pass "students_by_subject returned a count for CS101 ($($subjectCount.student_count))"

    $projectFiles = Invoke-McpTool -Name 'project_files' -Arguments @{ directory_path = '.' }
    if ($projectFiles.path -ne '.' -or $null -eq $projectFiles.files) {
        throw 'project_files did not return the repository root and its entries.'
    }
    Write-Pass "project_files returned $(@($projectFiles.files).Count) repository entries"

    $ciReport = Invoke-McpTool -Name 'ci_report'
    if ([string]::IsNullOrWhiteSpace($ciReport.path) -or [string]::IsNullOrWhiteSpace($ciReport.report)) {
        throw 'ci_report did not return the path and content of a CI evidence report.'
    }
    Write-Pass "ci_report returned $($ciReport.path)"

    foreach ($feature in @('student-1', 'student-2', 'student-3', 'student-4', 'student-5')) {
        $integration = Invoke-McpTool -Name 'validate_integration' -Arguments @{ feature = $feature }
        $failedChecks = @($integration.checks | Where-Object { $_.passed -ne $true })
        if ($integration.valid -ne $true -or $failedChecks.Count -gt 0) {
            throw "MCP integration validation failed for $feature."
        }
    }
    Write-Pass 'validate_integration passed for student-1 through student-5'

    $unsafePath = Invoke-McpTool `
        -Name 'project_files' `
        -Arguments @{ directory_path = '..' } `
        -ExpectError
    $invalidStatistics = Invoke-McpTool `
        -Name 'calculate_student_academic_statistics' `
        -Arguments @{} `
        -ExpectError
    if (-not $unsafePath -or -not $invalidStatistics) {
        throw 'MCP negative-input validation did not return the expected error envelopes.'
    }
    Write-Pass 'MCP rejected repository traversal and missing required tool arguments'

    $script:validationSucceeded = $true
    Write-Host "`nMCP terminal validation passed."
}
finally {
    Pop-Location
    Stop-StartedServices
}