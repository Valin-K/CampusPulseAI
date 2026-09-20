$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Test-ServiceHealth {
    param(
        [string]$Url,
        [string]$ServiceName
    )

    try {
        $response = & curl.exe --silent --fail --max-time 2 $Url | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) {
            return $false
        }

        return $response.service -eq $ServiceName
    }
    catch {
        return $false
    }
}

function Start-LocalService {
    param(
        [string]$ProjectPath,
        [string]$Url
    )

    Start-Process -FilePath dotnet -WorkingDirectory $repositoryRoot -WindowStyle Hidden -ArgumentList @(
        'run',
        '--no-launch-profile',
        '--project',
        $ProjectPath,
        '--',
        '--urls',
        $Url
    )
}

$services = @(
    @{
        ProjectPath = 'ai-services\mcp-server\McpServer\McpServer.csproj'
        Url = 'http://0.0.0.0:5268'
        HealthUrl = 'http://localhost:5268/health'
        ServiceName = 'mcp'
    },
    @{
        ProjectPath = 'ai-services\rag-server\RagServer\RagServer.csproj'
        Url = 'http://0.0.0.0:5267'
        HealthUrl = 'http://localhost:5267/health'
        ServiceName = 'rag'
    }
)

foreach ($service in $services) {
    if (-not (Test-ServiceHealth -Url $service.HealthUrl -ServiceName $service.ServiceName)) {
        Start-LocalService -ProjectPath $service.ProjectPath -Url $service.Url
    }
}

$deadline = (Get-Date).AddSeconds(90)
do {
    $allReady = $true
    foreach ($service in $services) {
        if (-not (Test-ServiceHealth -Url $service.HealthUrl -ServiceName $service.ServiceName)) {
            $allReady = $false
            break
        }
    }

    if ($allReady) {
        exit 0
    }

    Start-Sleep -Seconds 1
} while ((Get-Date) -lt $deadline)

Write-Error 'The local MCP or RAG server did not become healthy within 90 seconds.'
exit 1
