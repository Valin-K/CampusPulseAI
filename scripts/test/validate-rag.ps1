[CmdletBinding()]
param(
    [ValidateRange(15, 300)]
    [int] $StartupTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ragTests = 'ai-services\rag-server\RagServer.Tests\RagServer.Tests.csproj'
$gatewayProject = 'shared\backend\CampusPulse.Shared.Ai.Gateway\CampusPulse.Shared.Ai.Gateway.csproj'
$ragDll = 'ai-services\rag-server\RagServer\bin\Release\net10.0\RagServer.dll'
$gatewayDll = 'shared\backend\CampusPulse.Shared.Ai.Gateway\bin\Release\net10.0\CampusPulse.Shared.Ai.Gateway.dll'
$ragUrl = 'http://localhost:5267'
$gatewayUrl = 'http://localhost:5269'
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
        [string] $ServiceName,
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
$validationCollection = $null
$collectionWasNew = $false
$expectedChunkCount = 0
$cleanupSucceeded = $true
try {
    Invoke-CheckedCommand `
        -Name 'RAG server integration tests' `
        -Executable 'dotnet' `
        -Arguments @('test', $ragTests, '--configuration', 'Release', '--nologo')

    $gatewayProcess = $null
    if (-not (Test-HttpService -Url "$gatewayUrl/health" -ServiceName 'ai-gateway')) {
        Invoke-CheckedCommand `
            -Name 'Build AI gateway' `
            -Executable 'dotnet' `
            -Arguments @('build', $gatewayProject, '--configuration', 'Release', '--nologo')

        $gatewayService = Start-LocalService `
            -Name 'ai-gateway' `
            -DllPath $gatewayDll `
            -ProjectDirectory 'shared\backend\CampusPulse.Shared.Ai.Gateway' `
            -Url $gatewayUrl
        $gatewayProcess = $gatewayService.Process
    }
    else {
        Write-Host 'Reusing healthy AI gateway at ' $gatewayUrl
    }
    Wait-HttpService -Url "$gatewayUrl/health" -ServiceName 'ai-gateway' -TimeoutSeconds $StartupTimeoutSeconds -Process $gatewayProcess

    $ragProcess = $null
    if (-not (Test-HttpService -Url "$ragUrl/health" -ServiceName 'rag')) {
        $ragService = Start-LocalService `
            -Name 'rag' `
            -DllPath $ragDll `
            -ProjectDirectory 'ai-services\rag-server\RagServer' `
            -Url $ragUrl `
            -Environment @{ Services__AiGateway = $gatewayUrl }
        $ragProcess = $ragService.Process
    }
    else {
        Write-Host 'Reusing healthy RAG server at ' $ragUrl
    }
    Wait-HttpService -Url "$ragUrl/health" -ServiceName 'rag' -TimeoutSeconds $StartupTimeoutSeconds -Process $ragProcess

    $health = Invoke-RestMethod -Method Get -Uri "$ragUrl/health" -TimeoutSec 5
    if ($null -eq $health.allowedCollections -or $health.allowedCollections.Count -eq 0) {
        throw 'RAG health response did not declare any allowed collections.'
    }
    Write-Pass "RAG health (embedder=$($health.embedder); allowed collections=$($health.allowedCollections.Count))"

    $existingCollections = Invoke-RestMethod -Method Get -Uri "$ragUrl/api/rag/collections" -TimeoutSec 10
    $existingNames = @($existingCollections | Where-Object {
        $null -ne $_ -and -not [string]::IsNullOrWhiteSpace($_.name)
    } | ForEach-Object { $_.name.ToLowerInvariant() })
    foreach ($allowedCollection in $health.allowedCollections) {
        if ($allowedCollection.ToLowerInvariant() -notin $existingNames) {
            $validationCollection = $allowedCollection
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($validationCollection)) {
        throw 'No unused allowed RAG collection is available; existing collections were left untouched.'
    }
    $collectionWasNew = $true

    $fixtureId = "terminal-validation-$([guid]::NewGuid().ToString('N'))"
    $marker = "CAMPUSPULSEVALIDATION$([guid]::NewGuid().ToString('N').Substring(0, 12))"
    $fixtureText = "CampusPulseAI terminal validation marker $marker confirms grounded citation retrieval."
    $encodedCollection = [System.Uri]::EscapeDataString($validationCollection)

    try {
        $ingestionBody = @{
            documents = @(@{
                id = $fixtureId
                text = $fixtureText
                metadata = @{ source = 'terminal-validation' }
            })
        } | ConvertTo-Json -Depth 10
        $ingestion = Invoke-RestMethod -Method Post `
            -Uri "$ragUrl/api/rag/$encodedCollection/documents" `
            -ContentType 'application/json' `
            -Body $ingestionBody `
            -TimeoutSec 30
        $expectedChunkCount = [int] $ingestion.chunks
        if ($ingestion.collection -ne $validationCollection -or $ingestion.documents -ne 1 -or $expectedChunkCount -lt 1) {
            throw 'RAG ingestion did not confirm one document and at least one chunk.'
        }
        Write-Pass "RAG ingested one fixture into $validationCollection ($expectedChunkCount chunks)"

        $answerBody = @{
            question = "What does marker $marker confirm?"
            topK = 2
            minimumConfidence = 0
        } | ConvertTo-Json -Depth 10
        $answer = Invoke-RestMethod -Method Post `
            -Uri "$ragUrl/api/rag/$encodedCollection/answer" `
            -ContentType 'application/json' `
            -Body $answerBody `
            -TimeoutSec 60
        $sources = @($answer.sources)
        $citation = $sources | Where-Object {
            $_.documentId -eq $fixtureId -and $_.text.Contains($marker)
        } | Select-Object -First 1
        if ($answer.status -notin @('answered', 'generation_unavailable') -or $null -eq $citation) {
            throw "RAG answer did not cite the validation fixture; status='$($answer.status)'."
        }
        if ($answer.status -eq 'answered' -and [string]::IsNullOrWhiteSpace($answer.answer)) {
            throw 'RAG returned answered status without answer text.'
        }
        Write-Pass "RAG answer returned status '$($answer.status)' with a matching source citation"
    }
    finally {
        if ($collectionWasNew) {
            try {
                $currentCollections = Invoke-RestMethod -Method Get -Uri "$ragUrl/api/rag/collections" -TimeoutSec 10
                $currentCollection = $currentCollections | Where-Object {
                    $_.name -eq $validationCollection
                } | Select-Object -First 1
                if ($null -eq $currentCollection) {
                    Write-Pass 'RAG validation collection is absent after the check'
                }
                elseif ($expectedChunkCount -gt 0 -and $currentCollection.documents -eq 1 -and $currentCollection.chunks -eq $expectedChunkCount) {
                    $deleted = Invoke-RestMethod -Method Delete `
                        -Uri "$ragUrl/api/rag/$encodedCollection" `
                        -TimeoutSec 10
                    if ($deleted.deleted -ne $true) {
                        throw 'RAG server did not confirm removal of the isolated validation collection.'
                    }
                    Write-Pass 'Removed the isolated RAG validation collection'
                }
                else {
                    Write-Warning 'RAG collection changed during validation; it was left untouched.'
                    $cleanupSucceeded = $false
                }
            }
            catch {
                Write-Warning "Could not safely remove the RAG validation collection: $($_.Exception.Message)"
                $cleanupSucceeded = $false
            }
        }
    }

    if (-not $cleanupSucceeded) {
        throw 'RAG validation collection cleanup did not complete safely.'
    }
    $script:validationSucceeded = $true
    Write-Host "`nRAG terminal validation passed."
}
finally {
    Pop-Location
    Stop-StartedServices
}