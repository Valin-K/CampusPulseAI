# Builds every repository project with the .NET security analyzers switched on (Ruff security rules equivalent).
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projects = Get-ChildItem $repoRoot -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object FullName

# Security CA rules: CA2100, CA2109, CA2119, CA2153, CA23xx, CA3xxx, CA5xxx.
$securityRule = 'warning (CA2100|CA2109|CA2119|CA2153|CA23\d\d|CA3\d{3}|CA5\d{3})|error (CA2100|CA2109|CA2119|CA2153|CA23\d\d|CA3\d{3}|CA5\d{3})'

$failed = @()
foreach ($project in $projects) {
    $name = [IO.Path]::GetRelativePath($repoRoot, $project.FullName)
    Write-Host "Running .NET security analyzers: $name"
    # Passing AnalysisModeSecurity here turns the security rules on without editing each student's .csproj.
    $output = dotnet build $project.FullName -c Release --nologo -v:minimal -p:AnalysisModeSecurity=All 2>&1 | Out-String
    $findings = $output -split "`r?`n" | Where-Object { $_ -match $securityRule } | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0 -or $findings) {
        Write-Host $output
        $failed += $name
    }
}

if ($failed.Count -gt 0) {
    Write-Host "Security analyzer findings or build errors in:" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}

Write-Host "No .NET security analyzer findings in $($projects.Count) projects." -ForegroundColor Green
exit 0
