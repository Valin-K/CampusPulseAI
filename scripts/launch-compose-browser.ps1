$url = 'http://localhost:8080'
$deadline = (Get-Date).AddMinutes(2)

do {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -eq 200) {
            $chrome = Join-Path ${env:ProgramFiles} 'Google\Chrome\Application\chrome.exe'
            $profile = Join-Path $env:TEMP "CampusPulseAI-Chrome-$PID"
            Start-Process -FilePath $chrome -ArgumentList @(
                '--no-first-run',
                '--no-default-browser-check',
                "--user-data-dir=$profile",
                '--new-window',
                $url
            )
            exit 0
        }
    }
    catch {
    }

    Start-Sleep -Seconds 1
} while ((Get-Date) -lt $deadline)

exit 1