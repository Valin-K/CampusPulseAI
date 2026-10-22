# Lists vulnerable NuGet packages for every repository project.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projects = Get-ChildItem $repoRoot -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object FullName

$vulnerable = @()
$errored = @()
foreach ($project in $projects) {
    $name = [IO.Path]::GetRelativePath($repoRoot, $project.FullName)
    Write-Host "Auditing NuGet packages: $name"

    # NuGetAudit is off here so restore cannot fail on a vulnerability; the list command below reports it instead.
    dotnet restore $project.FullName --verbosity quiet -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { $errored += $name; continue }

    $output = dotnet list $project.FullName package --vulnerable --include-transitive --no-restore 2>&1 | Out-String

    # dotnet list package exits 0 even when vulnerabilities are found, so the output has to be checked.
    if ($LASTEXITCODE -ne 0) { Write-Host $output; $errored += $name }
    elseif ($output -match 'has the following vulnerable packages') { Write-Host $output; $vulnerable += $name }
}

if ($errored.Count -gt 0) {
    Write-Host "NuGet audit could not run for (check network access to nuget.org and that the project restores):" -ForegroundColor Red
    $errored | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}
if ($vulnerable.Count -gt 0) {
    Write-Host "Vulnerable NuGet packages found in:" -ForegroundColor Red
    $vulnerable | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}
if ($errored.Count -gt 0 -or $vulnerable.Count -gt 0) { exit 1 }

Write-Host "No vulnerable NuGet packages in $($projects.Count) projects." -ForegroundColor Green
exit 0
